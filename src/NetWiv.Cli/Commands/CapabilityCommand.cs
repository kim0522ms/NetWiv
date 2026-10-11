using System.Text.Json;
using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

internal static class CapabilityCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help") || args.Length == 0) { await output.WriteLineAsync("Usage: netwiv capability new [IDENTIFIER] [--description TEXT] [--windows LABELS] [--format json|toml] [-o FILE]"); return 0; }
        if (arguments.TakePositional() != "new") throw new ArgumentException("Only `capability new` is defined by the Tauri v2 desktop CLI.");
        var identifier = arguments.TakePositional() ?? "default"; PermissionCommand.ValidateId(identifier);
        var description = arguments.TakeOption("--description") ?? $"Capability for {identifier}";
        var windows = arguments.TakeOption("--windows")?.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? ["main"];
        var outputPath = arguments.TakeOption("--out", "-o"); var format = arguments.TakeOption("--format") ?? "json"; arguments.EnsureEmpty();
        if (format is not "json" and not "toml") throw new ArgumentException("Capability format must be json or toml.");
        var config = ConfigLoader.Load(workingDirectory, OperatingSystem.IsWindows() ? "windows" : "linux");
        var path = outputPath is null ? Path.Combine(config.ConfigDirectory, "capabilities", identifier + "." + format) : Path.GetFullPath(outputPath, workingDirectory);
        if (File.Exists(path)) throw new IOException($"Capability already exists: {path}"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var root = new JsonObject { ["identifier"] = identifier, ["description"] = description, ["windows"] = new JsonArray(windows.Select(value => JsonValue.Create(value)).ToArray()), ["permissions"] = new JsonArray("core:default") };
        var text = format == "json"
            ? root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            : $"identifier = {Quote(identifier)}\ndescription = {Quote(description)}\nwindows = [{string.Join(", ", windows.Select(Quote))}]\npermissions = [\"core:default\"]";
        await File.WriteAllTextAsync(path, text + Environment.NewLine, token); await output.WriteLineAsync(path); return 0;
    }

    private static string Quote(string value) => JsonValue.Create(value)!.ToJsonString();
}
