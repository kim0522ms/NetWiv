using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetWiv.Plugin.Shell;

/// <summary>Scoped child-process operations for the official shell guest.</summary>
public sealed class ShellPlugin : IPlugin
{
    private static readonly string[] Commands = ["execute", "spawn", "kill", "stdin_write", "open"];
    private readonly Dictionary<int, Child> _children = [];
    private readonly object _gate = new();
    private readonly Action<ProcessStartInfo> _open;
    private readonly Func<ProcessStartInfo, CancellationToken, Process?> _start;
    private readonly string _sidecarDirectory;

    public ShellPlugin() : this(StartOpen) { }
    internal ShellPlugin(Action<ProcessStartInfo> launch, string? sidecarDirectory = null,
        Func<ProcessStartInfo, CancellationToken, Process?>? start = null)
    {
        _open = launch;
        _start = start ?? ((info, _) => System.Diagnostics.Process.Start(info));
        var directory = new DirectoryInfo(Path.GetFullPath(sidecarDirectory ?? AppContext.BaseDirectory));
        // Managed app bundles keep their apphost in Resources behind a MacOS launcher.
        if (OperatingSystem.IsMacOS() && directory.Name == "Resources" && directory.Parent?.Name == "Contents" &&
            directory.Parent.Parent?.Extension == ".app")
            directory = new DirectoryInfo(Path.Combine(directory.Parent.FullName, "MacOS"));
        _sidecarDirectory = directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName;
    }

    public string Name => "shell";

    public void Initialize(PluginContext context)
    {
        foreach (var command in Commands)
        {
            var name = command;
            context.RegisterCommand(name, (invoke, args, token) => Execute(name, invoke, args, token));
            context.Permission(new("allow-" + name.Replace('_', '-'), [name], []));
            context.Permission(new("deny-" + name.Replace('_', '-'), [], [name]));
        }
        context.PermissionSet("default", "allow-open");
    }

    public void Dispose()
    {
        Child[] children;
        lock (_gate) { children = [.. _children.Values]; _children.Clear(); }
        foreach (var child in children) child.Dispose(kill: true);
    }

    public void OnDocumentChanged(AppHandle app, NetWiv.Runtime.RuntimeDocumentChange change)
    {
        Child[] stale;
        lock (_gate)
        {
            var entries = _children.Where(entry => entry.Value.WebviewLabel == change.WebviewLabel &&
                (change.Closed || entry.Value.DocumentId != change.DocumentId)).ToArray();
            stale = entries.Select(entry => entry.Value).ToArray();
            foreach (var entry in entries) _children.Remove(entry.Key);
        }
        foreach (var child in stale) child.Dispose(kill: true);
    }

    private async ValueTask<JsonElement> Execute(string command, InvokeContext context, JsonElement args, CancellationToken token)
    {
        switch (command)
        {
            case "execute":
            case "spawn": return await Start(command, context, args, token);
            case "kill":
            case "stdin_write":
            {
                var pid = args.GetProperty("pid").GetInt32();
                Child child;
                lock (_gate) child = _children.TryGetValue(pid, out var found) ? found : throw new InvokeException("Unknown shell process.");
                if (!context.IsTrusted && (child.WebviewLabel != context.WebviewLabel || child.DocumentId != context.DocumentId))
                    throw new InvokeException("Shell process belongs to another document.");
                if (command == "kill") { child.Kill(); return JsonValue.Null; }
                var value = args.GetProperty("buffer");
                var bytes = value.ValueKind switch
                {
                    JsonValueKind.String => System.Text.Encoding.UTF8.GetBytes(value.GetString()!),
                    JsonValueKind.Array => value.EnumerateArray().Select(item => item.GetByte()).ToArray(),
                    _ => throw new InvokeException("Shell stdin must be a string or byte array.")
                };
                await child.Process.StandardInput.BaseStream.WriteAsync(bytes, token);
                await child.Process.StandardInput.BaseStream.FlushAsync(token);
                return JsonValue.Null;
            }
            case "open":
                Open(args);
                return JsonValue.Null;
            default: throw new InvokeException("Unknown shell command.");
        }
    }

    private async Task<JsonElement> Start(string command, InvokeContext context, JsonElement args, CancellationToken token)
    {
        var program = Text(args, "program");
        var arguments = args.TryGetProperty("args", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(value => value.GetString() ?? throw new InvokeException("Shell argument must be a string.")).ToArray()
            : [];
        var options = args.TryGetProperty("options", out var configured) && configured.ValueKind == JsonValueKind.Object ? configured : default;
        var sidecar = options.ValueKind == JsonValueKind.Object && options.TryGetProperty("sidecar", out var bundled) && bundled.ValueKind == JsonValueKind.True;
        var executable = ResolveProgram(context, program, arguments, sidecar);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardInput = command == "spawn", RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (options.ValueKind == JsonValueKind.Object)
        {
            if (options.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String)
                info.WorkingDirectory = Path.GetFullPath(cwd.GetString()!);
            if (options.TryGetProperty("env", out var env))
            {
                if (env.ValueKind == JsonValueKind.Null) info.Environment.Clear();
                else if (env.ValueKind == JsonValueKind.Object)
                    foreach (var entry in env.EnumerateObject()) info.Environment[entry.Name] = entry.Value.GetString()!;
                else throw new InvokeException("Shell env must be an object or null.");
            }
        }
        var raw = options.ValueKind == JsonValueKind.Object && options.TryGetProperty("encoding", out var encoding) &&
            encoding.ValueKind == JsonValueKind.String && encoding.GetString() == "raw";
        if (options.ValueKind == JsonValueKind.Object && options.TryGetProperty("encoding", out encoding) &&
            encoding.ValueKind == JsonValueKind.String && encoding.GetString() is not ("raw" or "utf-8" or "utf8"))
            throw new InvokeException("Only UTF-8 and raw shell output encodings are supported.");
        var channel = command == "spawn" ? Channel.FromJson(context, args.GetProperty("onEvent")) : null;
        var process = _start(info, token) ?? throw new InvokeException("The shell process did not start.");
        if (command == "execute")
        {
            using (process)
            {
                var stdout = raw ? ReadBytes(process.StandardOutput.BaseStream, token) : ReadText(process.StandardOutput, token);
                var stderr = raw ? ReadBytes(process.StandardError.BaseStream, token) : ReadText(process.StandardError, token);
                try
                {
                    await process.WaitForExitAsync(token);
                    var outValue = await stdout; var errValue = await stderr;
                    return JsonValue.Parse("{\"code\":" + process.ExitCode + ",\"signal\":null,\"stdout\":" +
                        outValue.GetRawText() + ",\"stderr\":" + errValue.GetRawText() + "}");
                }
                finally
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { } // The process exited during cleanup.
                    await process.WaitForExitAsync(CancellationToken.None);
                    // Observe both readers before disposing their streams, preserving the original failure.
                    try { await Task.WhenAll(stdout, stderr); }
                    catch { }
                }
            }
        }
        var child = new Child(process, channel!, context.WebviewLabel, context.DocumentId);
        var pid = process.Id;
        try
        {
            lock (_gate)
            {
                // Document invalidation cancels invocations before removing their children.
                token.ThrowIfCancellationRequested();
                _children.Add(pid, child);
            }
        }
        catch
        {
            child.Dispose(kill: true);
            throw;
        }
        _ = Pump(child, raw);
        return JsonValue.From(pid);
    }

    private async Task Pump(Child child, bool raw)
    {
        var pid = child.Process.Id;
        try
        {
            var stdout = PumpStream(child, child.Process.StandardOutput, "Stdout", raw);
            var stderr = PumpStream(child, child.Process.StandardError, "Stderr", raw);
            await child.Process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            child.Send("Terminated", JsonValue.Parse("{\"code\":" + child.Process.ExitCode + ",\"signal\":null}"));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException)
        {
            child.Send("Error", JsonValue.From(error.Message));
        }
        finally
        {
            lock (_gate) _children.Remove(pid);
            child.Dispose(kill: false);
        }
    }

    private static async Task PumpStream(Child child, StreamReader reader, string eventName, bool raw)
    {
        if (!raw)
        {
            while (await reader.ReadLineAsync() is { } line) child.Send(eventName, JsonValue.From(line));
            return;
        }
        var buffer = new byte[8192];
        int count;
        while ((count = await reader.BaseStream.ReadAsync(buffer)) > 0)
            child.Send(eventName, ByteArray(buffer.AsSpan(0, count)));
    }

    private static async Task<JsonElement> ReadText(StreamReader reader, CancellationToken token) =>
        JsonValue.From(await reader.ReadToEndAsync(token));

    private static async Task<JsonElement> ReadBytes(Stream stream, CancellationToken token)
    {
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, token);
        return ByteArray(output.ToArray());
    }

    private static JsonElement ByteArray(ReadOnlySpan<byte> bytes)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        { writer.WriteStartArray(); foreach (var value in bytes) writer.WriteNumberValue(value); writer.WriteEndArray(); }
        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private string ResolveProgram(InvokeContext context, string name, string[] args, bool sidecar)
    {
        if (context.IsTrusted) return sidecar ? ResolveSidecar(name) : name;
        static bool Match(JsonElement entry, string program, string[] arguments, bool isSidecar)
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("name", out var label) || label.GetString() != program) return false;
            if ((entry.TryGetProperty("sidecar", out var configured) && configured.ValueKind == JsonValueKind.True) != isSidecar) return false;
            if (!entry.TryGetProperty("args", out var allowed) || allowed.ValueKind == JsonValueKind.Null) return arguments.Length == 0;
            if (allowed.ValueKind == JsonValueKind.True) return true;
            if (allowed.ValueKind != JsonValueKind.Array || allowed.GetArrayLength() != arguments.Length) return false;
            for (var index = 0; index < arguments.Length; index++)
            {
                var rule = allowed[index];
                if (rule.ValueKind == JsonValueKind.String && rule.GetString() == arguments[index]) continue;
                if (rule.ValueKind != JsonValueKind.Object || !rule.TryGetProperty("validator", out var validator) ||
                    validator.ValueKind != JsonValueKind.String ||
                    !Regex.IsMatch(arguments[index], "\\A(?:" + validator.GetString() + ")\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    return false;
            }
            return true;
        }
        if (context.Scope.Deny.Any(entry => Match(entry, name, args, sidecar))) throw new InvokeException("Shell command is denied by scope.");
        var configured = context.Scope.Allow.FirstOrDefault(entry => Match(entry, name, args, sidecar));
        if (configured.ValueKind != JsonValueKind.Object)
            throw new InvokeException("Shell command is not allowed by scope.");
        if (sidecar) return ResolveSidecar(name);
        if (!configured.TryGetProperty("cmd", out var cmd) || cmd.ValueKind != JsonValueKind.String)
            throw new InvokeException("Shell command is not allowed by scope.");
        return cmd.GetString()!;
    }

    private string ResolveSidecar(string name)
    {
        var segments = name.Replace('\\', '/').Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
            segment.IndexOfAny(['\0', ':', '<', '>', '"', '|', '?', '*']) >= 0))
            throw new InvokeException("Shell sidecar must be a relative bundled binary name.");
        var filename = segments[^1];
        if (OperatingSystem.IsWindows() && !filename.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) filename += ".exe";
        var path = Path.Combine(_sidecarDirectory, filename);
        if (!File.Exists(path)) throw new InvokeException("Shell sidecar was not found beside the application executable.");
        var resolved = new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path;
        if (!string.Equals(Path.GetDirectoryName(resolved), Path.TrimEndingDirectorySeparator(_sidecarDirectory),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvokeException("Shell sidecar must remain beside the application executable.");
        return resolved;
    }

    private void Open(JsonElement args)
    {
        var target = Text(args, "path");
        if (!Uri.TryCreate(target, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https" or "mailto" or "tel"))
            throw new InvokeException("The shell open target is not allowed.");
        if (args.TryGetProperty("with", out var app) && app.ValueKind == JsonValueKind.String)
            throw new InvokeException("Shell openWith is not implemented.");
        var info = OperatingSystem.IsWindows() ? new ProcessStartInfo(target) { UseShellExecute = true }
            : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "xdg-open") { UseShellExecute = false, ArgumentList = { target } };
        _open(info);
    }

    private static void StartOpen(ProcessStartInfo info)
    {
        using var process = System.Diagnostics.Process.Start(info) ?? throw new InvokeException("No application handled the shell target.");
    }

    private static string Text(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new InvokeException("Missing shell argument '" + name + "'.");

    private sealed class Child(System.Diagnostics.Process process, Channel channel, string webviewLabel, ulong documentId)
    {
        private readonly object _gate = new();
        private bool _disposed;
        public System.Diagnostics.Process Process { get; } = process;
        public string WebviewLabel { get; } = webviewLabel;
        public ulong DocumentId { get; } = documentId;

        public void Send(string kind, JsonElement payload)
        {
            lock (_gate)
            {
                if (_disposed) return;
                using var doc = JsonDocument.Parse("{\"event\":" + JsonValue.From(kind).GetRawText() + ",\"payload\":" + payload.GetRawText() + "}");
                try { channel.Send(doc.RootElement); }
                catch (ObjectDisposedException) { }
            }
        }

        public void Kill()
        {
            lock (_gate)
            {
                if (_disposed) return;
                try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }

        public void Dispose(bool kill)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (kill)
                {
                    try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                }
                try { channel.Dispose(); }
                catch (ObjectDisposedException) { }
                Process.Dispose();
            }
        }
    }
}
