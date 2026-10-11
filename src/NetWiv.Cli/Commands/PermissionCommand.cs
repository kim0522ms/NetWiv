using NetWiv.Build.Execution;
using System.Text.Json;
using System.Text.Json.Nodes;
using NetWiv.Build.Configuration;

namespace NetWiv.Cli;

internal static class PermissionCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, string workingDirectory, CancellationToken token)
    {
        var arguments = new CommandArguments(args);
        if (arguments.TakeFlag("--help") || args.Length == 0) { await output.WriteLineAsync("Usage: netwiv permission <new|add|rm|ls> [OPTIONS]"); return 0; }
        return arguments.TakePositional() switch
        {
            "new" => await New(arguments, output, workingDirectory, token),
            "add" => await Add(arguments, output, workingDirectory, token),
            "rm" => await Remove(arguments, output, workingDirectory, token),
            "ls" => await List(arguments, output, workingDirectory),
            var command => throw new ArgumentException($"Unknown permission command '{command}'.")
        };
    }

    private static async Task<int> New(CommandArguments args, TextWriter output, string cwd, CancellationToken token)
    {
        var identifier = args.TakePositional() ?? throw new ArgumentException("Permission identifier is required."); ValidateId(identifier);
        var description = args.TakeOption("--description") ?? identifier;
        var allow = Split(args.TakeOption("--allow", "-a")); var deny = Split(args.TakeOption("--deny", "-d"));
        var format = args.TakeOption("--format") ?? "json"; var custom = args.TakeOption("--out", "-o"); args.EnsureEmpty();
        if (format is not "json" and not "toml") throw new ArgumentException("Permission format must be json or toml.");
        var config = ConfigLoader.Load(cwd, Platform()); var path = custom is null ? Path.Combine(config.ConfigDirectory, "permissions", identifier + "." + format) : Path.GetFullPath(custom, cwd);
        if (File.Exists(path)) throw new IOException($"Permission file already exists: {path}"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var document = new JsonObject { ["identifier"] = identifier, ["description"] = description, ["commands"] = new JsonObject { ["allow"] = new JsonArray(allow.Select(value => JsonValue.Create(value)).ToArray()), ["deny"] = new JsonArray(deny.Select(value => JsonValue.Create(value)).ToArray()) } };
        var text = format == "json" ? document.ToJsonString(Indented) : ToToml(identifier, description, allow, deny);
        await File.WriteAllTextAsync(path, text + Environment.NewLine, token); await output.WriteLineAsync(path); return 0;
    }

    private static async Task<int> Add(CommandArguments args, TextWriter output, string cwd, CancellationToken token)
    {
        var identifier = args.TakePositional() ?? throw new ArgumentException("Permission identifier is required.");
        var capability = args.TakePositional() ?? "default"; args.EnsureEmpty();
        var config = ConfigLoader.Load(cwd, Platform()); var path = CapabilityPath(config, capability);
        var root = ReadObject(path); var permissions = root["permissions"] as JsonArray ?? []; if (root["permissions"] is null) root["permissions"] = permissions;
        if (!permissions.Any(node => node is JsonValue value && value.TryGetValue<string>(out var item) && item == identifier)) permissions.Add((JsonNode)JsonValue.Create(identifier)!);
        await WriteCapability(path, root, token); await output.WriteLineAsync($"Added {identifier} to {capability}"); return 0;
    }

    private static async Task<int> Remove(CommandArguments args, TextWriter output, string cwd, CancellationToken token)
    {
        var identifier = args.TakePositional() ?? throw new ArgumentException("Permission identifier is required."); args.EnsureEmpty();
        var config = ConfigLoader.Load(cwd, Platform()); var permissions = Path.Combine(config.ConfigDirectory, "permissions");
        var matches = Directory.Exists(permissions) ? Directory.EnumerateFiles(permissions).Where(path => Path.GetFileNameWithoutExtension(path) == identifier || (identifier.EndsWith(":*", StringComparison.Ordinal) && Path.GetFileNameWithoutExtension(path).StartsWith(identifier[..^1], StringComparison.Ordinal))).ToArray() : [];
        foreach (var match in matches) File.Delete(match);
        var capabilities = Path.Combine(config.ConfigDirectory, "capabilities");
        if (Directory.Exists(capabilities)) foreach (var path in Directory.EnumerateFiles(capabilities).Where(IsConfigFile))
        {
            var root = ReadObject(path); if (root["permissions"] is not JsonArray array) continue;
            var remove = array.Where(node => node is JsonValue value && value.TryGetValue<string>(out var id) && (id == identifier || (identifier.EndsWith(":*", StringComparison.Ordinal) && id.StartsWith(identifier[..^1], StringComparison.Ordinal)))).ToArray();
            foreach (var node in remove) array.Remove(node); if (remove.Length > 0) await WriteCapability(path, root, token);
        }
        await output.WriteLineAsync($"Removed {matches.Length} permission file(s) and capability references."); return 0;
    }

    private static async Task<int> List(CommandArguments args, TextWriter output, string cwd)
    {
        var plugin = args.TakePositional(); var filter = args.TakeOption("--filter", "-f"); args.EnsureEmpty();
        var config = ConfigLoader.Load(cwd, Platform()); var directory = Path.Combine(config.ConfigDirectory, "permissions");
        if (!Directory.Exists(directory)) return 0;
        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(file); if (plugin is not null && !id.StartsWith(plugin + ":", StringComparison.Ordinal)) continue; if (filter is not null && !id.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            await output.WriteLineAsync(id);
        }
        return 0;
    }

    internal static string CapabilityPath(ConfigDocument config, string identifier)
    {
        ValidateId(identifier); var directory = Path.Combine(config.ConfigDirectory, "capabilities");
        var candidates = new[] { ".json", ".json5", ".toml" }.Select(extension => Path.Combine(directory, identifier + extension)).Where(File.Exists).ToArray();
        if (candidates.Length > 1) throw new NetWivConfigException($"Multiple capability files found for '{identifier}': {string.Join(", ", candidates)}");
        if (candidates.Length == 0) throw new FileNotFoundException($"Capability not found in {directory}: {identifier}.json, {identifier}.json5, or {identifier}.toml");
        return candidates[0];
    }
    internal static JsonObject ReadObject(string path) => ConfigLoader.ParseFile(path) as JsonObject ?? throw new NetWivConfigException($"Expected object in {path}.");
    internal static Task WriteJson(string path, JsonObject root, CancellationToken token) => File.WriteAllTextAsync(path, root.ToJsonString(Indented) + Environment.NewLine, token);
    private static Task WriteCapability(string path, JsonObject root, CancellationToken token)
    {
        if (!path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) return WriteJson(path, root, token);
        var permissions = root["permissions"] as JsonArray ?? [];
        var text = File.ReadAllText(path);
        var rendered = ToTomlValue(permissions);
        var firstTable = System.Text.RegularExpressions.Regex.Match(text, "(?m)^[ \\t]*\\[");
        var topLevelLength = firstTable.Success ? firstTable.Index : text.Length;
        var match = System.Text.RegularExpressions.Regex.Match(text[..topLevelLength], "(?m)^[ \\t]*permissions[ \\t]*=[ \\t]*");
        if (!match.Success)
        {
            var prefix = text[..topLevelLength];
            if (prefix.Length > 0 && prefix[^1] != '\n') prefix += Environment.NewLine;
            return File.WriteAllTextAsync(path, prefix + "permissions = " + rendered + Environment.NewLine + text[topLevelLength..], token);
        }
        var start = match.Index + match.Length;
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        if (start >= text.Length || text[start] != '[') throw new NetWivConfigException($"TOML capability permissions must be an array in {path}.");
        var end = FindArrayEnd(text, start, path);
        return File.WriteAllTextAsync(path, text[..start] + rendered + text[(end + 1)..], token);
    }

    private static int FindArrayEnd(string text, int start, string path)
    {
        var depth = 0; var basic = false; var literal = false; var escaped = false; var comment = false;
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (comment) { if (character is '\r' or '\n') comment = false; continue; }
            if (basic)
            {
                if (escaped) { escaped = false; continue; }
                if (character == '\\') { escaped = true; continue; }
                if (character == '"') basic = false;
                continue;
            }
            if (literal) { if (character == '\'') literal = false; continue; }
            if (character == '#') { comment = true; continue; }
            if (character == '"') { basic = true; continue; }
            if (character == '\'') { literal = true; continue; }
            if (character == '[') depth++;
            else if (character == ']' && --depth == 0) return index;
        }
        throw new NetWivConfigException($"Unterminated TOML capability permissions array in {path}.");
    }

    private static string ToTomlValue(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => Quote(text),
        JsonValue value when value.TryGetValue<bool>(out var boolean) => boolean ? "true" : "false",
        JsonValue value => value.ToJsonString(),
        JsonArray array => "[" + string.Join(", ", array.Select(ToTomlValue)) + "]",
        JsonObject table => "{ " + string.Join(", ", table.Select(property => QuoteKey(property.Key) + " = " + ToTomlValue(property.Value))) + " }",
        _ => throw new NetWivConfigException("TOML capability permissions cannot contain null values.")
    };

    private static string QuoteKey(string value) => value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-') ? value : Quote(value);
    private static bool IsConfigFile(string path) => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".json5", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase);
    internal static void ValidateId(string value) { if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['/', '\\']) >= 0 || value.Contains("..", StringComparison.Ordinal)) throw new ArgumentException($"Invalid identifier '{value}'."); }
    private static string[] Split(string? value) => value?.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    private static string ToToml(string id, string description, string[] allow, string[] deny) => $"identifier = {Quote(id)}\ndescription = {Quote(description)}\n\n[commands]\nallow = [{string.Join(", ", allow.Select(Quote))}]\ndeny = [{string.Join(", ", deny.Select(Quote))}]\n";
    private static string Quote(string value) => JsonValue.Create(value)!.ToJsonString();
    private static string Platform() => BuildTarget.Resolve(null).Platform;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
