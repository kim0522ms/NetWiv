using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NetWiv.Cli;

internal static class SignerCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help") || args.Length == 0) { await output.WriteLineAsync("Usage: netwiv signer <generate|sign> [OPTIONS]"); return 0; }
        var command = arguments.TakePositional()!;
        return command switch
        {
            "generate" => await GenerateAsync(arguments, output, workingDirectory, cancellationToken),
            "sign" => await SignAsync(arguments, output, workingDirectory, cancellationToken),
            _ => throw new ArgumentException($"Unknown signer command '{command}'.")
        };
    }

    private static async Task<int> GenerateAsync(CommandArguments arguments, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        var password = arguments.TakeOption("--password", "-p");
        var writeKeys = arguments.TakeOption("--write-keys", "-w");
        var force = arguments.TakeFlag("--force", "-f");
        var ci = arguments.TakeFlag("--ci") || IsTrue(Environment.GetEnvironmentVariable("CI"));
        arguments.EnsureEmpty();
        if (ci && password is null) password = string.Empty;

        var secretPath = writeKeys is null ? null : Path.GetFullPath(writeKeys, workingDirectory);
        var publicPath = secretPath + ".pub";
        if (secretPath is not null && File.Exists(secretPath) && !force)
            throw new IOException($"Key generation aborted: {secretPath} already exists. Add --force to overwrite the existing key pair.");

        var minisign = await FindMinisignAsync(workingDirectory, cancellationToken);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "netwiv-signer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var rawSecretPath = Path.Combine(temporaryDirectory, "key");
            var rawPublicPath = Path.Combine(temporaryDirectory, "key.pub");
            var input = password is null ? null : password + Environment.NewLine + password + Environment.NewLine;
            await RunMinisignAsync(minisign, ["-G", "-p", rawPublicPath, "-s", rawSecretPath], input, cancellationToken, interactive: password is null);
            var encodedSecret = EncodeBox(await File.ReadAllTextAsync(rawSecretPath, cancellationToken));
            var encodedPublic = EncodeBox(await File.ReadAllTextAsync(rawPublicPath, cancellationToken));
            if (secretPath is null)
            {
                await output.WriteLineAsync("Your keys were generated successfully!\n\nPrivate: (Keep it secret!)");
                await output.WriteLineAsync(encodedSecret);
                await output.WriteLineAsync("\nPublic:");
                await output.WriteLineAsync(encodedPublic);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(secretPath)!);
                await File.WriteAllTextAsync(secretPath, encodedSecret, cancellationToken);
                await File.WriteAllTextAsync(publicPath!, encodedPublic, cancellationToken);
                await output.WriteLineAsync($"Your keypair was generated successfully:\nPrivate: {secretPath} (Keep it secret!)\nPublic: {publicPath}");
            }
            await output.WriteLineAsync("\nUse TAURI_SIGNING_PRIVATE_KEY or TAURI_SIGNING_PRIVATE_KEY_PATH and optional TAURI_SIGNING_PRIVATE_KEY_PASSWORD to sign.");
            return 0;
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<int> SignAsync(CommandArguments arguments, TextWriter output, string workingDirectory, CancellationToken cancellationToken)
    {
        var privateKey = arguments.TakeOption("--private-key", "-k");
        var privateKeyPath = arguments.TakeOption("--private-key-path", "-f");
        var password = arguments.TakeOption("--password", "-p");
        var fileArgument = arguments.TakePositional();
        arguments.EnsureEmpty();
        if (privateKey is not null && privateKeyPath is not null) throw new ArgumentException("--private-key and --private-key-path cannot be used together.");
        privateKey ??= Environment.GetEnvironmentVariable("TAURI_SIGNING_PRIVATE_KEY") ?? DeprecatedEnvironment("TAURI_PRIVATE_KEY", output);
        privateKeyPath ??= Environment.GetEnvironmentVariable("TAURI_SIGNING_PRIVATE_KEY_PATH") ?? DeprecatedEnvironment("TAURI_PRIVATE_KEY_PATH", output);
        password ??= Environment.GetEnvironmentVariable("TAURI_SIGNING_PRIVATE_KEY_PASSWORD") ?? DeprecatedEnvironment("TAURI_PRIVATE_KEY_PASSWORD", output);
        if (privateKey is not null && privateKeyPath is not null) throw new ArgumentException("Both private key content and a private key path were provided.");
        if (privateKey is null && privateKeyPath is null) throw new ArgumentException("Unable to find the updater private key.");
        if (fileArgument is null) throw new ArgumentException("Missing file to sign.");

        var file = Path.GetFullPath(fileArgument, workingDirectory);
        if (!File.Exists(file)) throw new FileNotFoundException($"File to sign does not exist: {file}");
        if (privateKeyPath is not null)
        {
            var path = Path.GetFullPath(privateKeyPath, workingDirectory);
            if (!File.Exists(path)) throw new FileNotFoundException($"Updater private key does not exist: {path}");
            privateKey = await File.ReadAllTextAsync(path, cancellationToken);
        }

        string rawPrivate;
        try { rawPrivate = DecodeBox(privateKey!); }
        catch (FormatException exception) { throw new ArgumentException("The updater private key is not valid Tauri Base64 key content.", exception); }
        var privateLines = rawPrivate.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (privateLines.Length != 2 || !privateLines[0].StartsWith("untrusted comment: ", StringComparison.Ordinal))
            throw new ArgumentException("The updater private key does not contain a minisign secret key box.");
        try { _ = Convert.FromBase64String(privateLines[1]); }
        catch (FormatException exception) { throw new ArgumentException("The updater private key contains an invalid minisign secret key box.", exception); }

        var minisign = await FindMinisignAsync(workingDirectory, cancellationToken);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "netwiv-signer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var rawSecretPath = Path.Combine(temporaryDirectory, "key");
            var rawSignaturePath = Path.Combine(temporaryDirectory, "signature");
            await WriteTemporarySecretAsync(rawSecretPath, rawPrivate, cancellationToken);
            var trustedComment = $"timestamp:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\tfile:{Path.GetFileName(file)}";
            var input = password is null ? null : password + Environment.NewLine;
            await RunMinisignAsync(minisign, ["-S", "-s", rawSecretPath, "-m", file, "-x", rawSignaturePath, "-c", "signature from NetWiv secret key", "-t", trustedComment], input, cancellationToken, interactive: password is null);
            var rawSignature = await File.ReadAllTextAsync(rawSignaturePath, cancellationToken);
            var encodedSignature = EncodeBox(rawSignature);
            var signaturePath = AddedExtension(file, "sig");
            await File.WriteAllTextAsync(signaturePath, encodedSignature, cancellationToken);
            await output.WriteLineAsync($"Your file was signed successfully.\nSignature: {signaturePath}\n\nPublic signature:\n{encodedSignature}");
            return 0;
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string? DeprecatedEnvironment(string name, TextWriter output)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value is not null) output.WriteLine($"Warning: {name} is deprecated; use {name.Replace("TAURI_PRIVATE", "TAURI_SIGNING_PRIVATE", StringComparison.Ordinal)}.");
        return value;
    }

    private static string AddedExtension(string path, string extension)
    {
        var current = Path.GetExtension(path);
        return current.Length == 0 ? Path.ChangeExtension(path, extension) : Path.ChangeExtension(path, current + "." + extension);
    }

    // Tauri updater configuration stores the complete text minisign box as Base64.
    private static string EncodeBox(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string DecodeBox(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value.Trim()));
    private static bool IsTrue(string? value) => value?.ToLowerInvariant() is "1" or "true";

    private static async Task WriteTemporarySecretAsync(string path, string value, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(path, value, cancellationToken);
            return;
        }
        await using var stream = new FileStream(path, new FileStreamOptions { Access = FileAccess.Write, Mode = FileMode.CreateNew, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(value.AsMemory(), cancellationToken);
    }

    private static async Task<string> FindMinisignAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var names = OperatingSystem.IsWindows() ? new[] { "minisign.exe", "minisign" } : new[] { "minisign" };
        var configured = Environment.GetEnvironmentVariable("NETWIV_MINISIGN_PATH");
        var candidates = new List<string?> { configured };
        candidates.AddRange(names.Select(name => Path.Combine(AppContext.BaseDirectory, name)));
        var architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64";
        candidates.Add(OperatingSystem.IsWindows()
            ? Path.Combine(workingDirectory, "artifacts", "tools", "minisign-0.12", "minisign-win64", architecture, "minisign.exe")
            : Path.Combine(workingDirectory, "artifacts", "tools", "minisign-0.12", "minisign-linux", architecture, "minisign"));
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.AddRange(names.Select(name => Path.Combine(directory.Trim('"'), name)));
        var path = candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate));
        if (path is null) throw new FileNotFoundException("minisign 0.12 was not found. Install the NetWiv CLI sidecar or set NETWIV_MINISIGN_PATH.");
        var version = await RunMinisignAsync(path, ["-v"], null, cancellationToken);
        if (!version.Contains("minisign 0.12", StringComparison.Ordinal)) throw new NotSupportedException($"NetWiv requires minisign 0.12, but '{path}' reported: {version.Trim()}");
        return Path.GetFullPath(path);
    }

    private static async Task<string> RunMinisignAsync(string executable, IReadOnlyList<string> arguments, string? input, CancellationToken cancellationToken, bool interactive = false)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = input is not null, RedirectStandardOutput = !interactive, RedirectStandardError = !interactive } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception exception) { throw new IOException($"Failed to start minisign: {exception.Message}", exception); }
        if (input is not null) { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
        var stdout = interactive ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = interactive ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var combined = (await stdout) + (await stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException($"minisign failed with exit code {process.ExitCode}: {combined.Trim()}");
        return combined;
    }
}
