using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetWiv.Build.Configuration;

public static class ConfigLoader
{
    private static readonly string[] NewNames = ["netwiv.conf.json", "netwiv.conf.json5", "NetWiv.toml"];
    private static readonly string[] LegacyNames = ["tauri.conf.json", "tauri.conf.json5", "Tauri.toml"];

    public static ConfigDocument Load(string startDirectory, string platform, IReadOnlyList<string>? overlays = null, int maxDepth = 3)
    {
        var basePath = Discover(startDirectory, maxDepth);
        var root = ParseFile(basePath) as JsonObject ?? throw new NetWivConfigException($"Configuration root must be an object: {basePath}");
        var sources = new List<string> { basePath };
        var directory = Path.GetDirectoryName(basePath)!;
        var platformCandidates = PlatformCandidates(basePath, platform).Where(File.Exists).ToArray();
        if (platformCandidates.Length > 1)
            throw new NetWivConfigException($"Multiple platform configuration files found: {string.Join(", ", platformCandidates)}");
        if (platformCandidates.Length == 1)
        {
            root = RequireObject(JsonMergePatch.Apply(root, ParseFile(platformCandidates[0])), platformCandidates[0]);
            sources.Add(platformCandidates[0]);
        }

        JsonNode? cliPatch = new JsonObject();
        foreach (var overlay in overlays ?? [])
        {
            JsonNode patch;
            string source;
            if (File.Exists(overlay))
            {
                source = Path.GetFullPath(overlay);
                patch = ParseFile(source);
            }
            else
            {
                source = "<inline-config>";
                try { patch = JsonNode.Parse(overlay) ?? throw new JsonException("empty JSON"); }
                catch (JsonException exception) { throw new NetWivConfigException($"--config is neither an existing file nor valid JSON: {exception.Message}"); }
            }
            cliPatch = JsonMergePatch.Compose(cliPatch, patch);
            sources.Add(source);
        }

        if (overlays is { Count: > 0 })
            root = RequireObject(JsonMergePatch.Apply(root, cliPatch), sources[^1]);

        Validate(root, basePath);
        if (root["version"] is JsonValue configuredVersion)
        {
            try
            {
                root["version"] = ApplicationVersion.Resolve(configuredVersion.GetValue<string>(), directory, out var versionFile);
                if (versionFile is not null) sources.Add(versionFile);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
            { throw new NetWivConfigException(exception.Message); }
        }
        var leaf = Path.GetFileName(directory);
        var projectDirectory = leaf is "src-netwiv" or "src-tauri"
            ? Directory.GetParent(directory)?.FullName ?? directory
            : directory;
        return new ConfigDocument(basePath, directory, projectDirectory, root, sources)
        { Overlays = overlays?.ToArray() ?? [], CliPatch = overlays is { Count: > 0 } ? cliPatch : null };
    }

    public static string Discover(string startDirectory, int maxDepth = 3)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        for (var depth = 0; directory is not null && depth <= maxDepth; depth++, directory = directory.Parent)
        {
            var candidates = CandidatePaths(directory.FullName).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length > 1)
                throw new NetWivConfigException($"Ambiguous base configuration. Keep exactly one of: {string.Join(", ", candidates)}");
            if (candidates.Length == 1)
                return Path.GetFullPath(candidates[0]);
        }
        throw new NetWivConfigException($"No NetWiv configuration found within {maxDepth} parent directories of '{Path.GetFullPath(startDirectory)}'.");
    }

    public static JsonNode ParseFile(string path)
    {
        var text = File.ReadAllText(path);
        try
        {
            if (path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)) return TomlLite.Parse(text);
            if (path.EndsWith(".json5", StringComparison.OrdinalIgnoreCase)) return Json5.Parse(text);
            return JsonNode.Parse(text)
                ?? throw new NetWivConfigException($"Empty configuration: {path}");
        }
        catch (Exception exception) when (exception is JsonException or Tomlyn.TomlException)
        {
            throw new NetWivConfigException($"Invalid configuration '{path}': {exception.Message}");
        }
    }

    private static IEnumerable<string> CandidatePaths(string directory)
    {
        foreach (var name in NewNames) { yield return Path.Combine(directory, name); yield return Path.Combine(directory, "src-netwiv", name); }
        foreach (var name in LegacyNames) { yield return Path.Combine(directory, name); yield return Path.Combine(directory, "src-tauri", name); }
    }

    private static IEnumerable<string> PlatformCandidates(string basePath, string platform)
    {
        var directory = Path.GetDirectoryName(basePath)!;
        var legacy = Path.GetFileName(basePath).StartsWith("tauri", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(basePath) == "Tauri.toml";
        var prefix = legacy ? "tauri" : "netwiv";
        var tomlPrefix = legacy ? "Tauri" : "NetWiv";
        yield return Path.Combine(directory, $"{prefix}.{platform}.conf.json");
        yield return Path.Combine(directory, $"{prefix}.{platform}.conf.json5");
        yield return Path.Combine(directory, $"{tomlPrefix}.{platform}.toml");
    }

    private static JsonObject RequireObject(JsonNode? node, string source) => node as JsonObject
        ?? throw new NetWivConfigException($"Configuration merge produced a non-object root at {source}.");

    private static void Validate(JsonObject root, string source)
    {
        var rootKeys = new HashSet<string>(["$schema", "productName", "mainBinaryName", "version", "identifier", "app", "build", "bundle", "plugins"], StringComparer.Ordinal);
        foreach (var property in root)
            if (!rootKeys.Contains(property.Key)) throw new NetWivConfigException($"Unknown top-level configuration property '{property.Key}' in {source}.");
        RequireString(root, "productName", source);
        RequireString(root, "mainBinaryName", source);
        RequireString(root, "version", source);
        if (root["identifier"] is not null)
        {
            if (root["identifier"] is not JsonValue identifier || !identifier.TryGetValue<string>(out var identifierText))
                throw new NetWivConfigException($"'identifier' must be a string in {source}.");
            if (string.IsNullOrWhiteSpace(identifierText))
                throw new NetWivConfigException($"'identifier' cannot be empty in {source}.");
            if (identifierText.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-'))
                throw new NetWivConfigException($"'identifier' may contain only ASCII letters, digits, hyphens, and periods in {source}.");
        }
        if (root["build"] is not null and not JsonObject)
            throw new NetWivConfigException($"'build' must be an object in {source}.");
        if (root["bundle"] is not null and not JsonObject)
            throw new NetWivConfigException($"'bundle' must be an object in {source}.");
        if (root["app"] is not null and not JsonObject)
            throw new NetWivConfigException($"'app' must be an object in {source}.");
        if (root.ContainsKey("plugins") && root["plugins"] is not JsonObject)
            throw new NetWivConfigException($"'plugins' must be an object in {source}.");

        if (root["app"] is JsonObject app)
        {
            if (app["windows"] is not null and not JsonArray) throw new NetWivConfigException($"'app.windows' must be an array in {source}.");
            if (app["windows"] is JsonArray windows)
                foreach (var window in windows)
                {
                    if (window is not JsonObject item) throw new NetWivConfigException($"Each 'app.windows' entry must be an object in {source}.");
                    RequireString(item, "label", source); RequireString(item, "title", source); RequireString(item, "url", source);
                    RequireBoolean(item, "visible", source); RequireNumber(item, "width", source); RequireNumber(item, "height", source);
                }
            if (app["security"] is not null and not JsonObject) throw new NetWivConfigException($"'app.security' must be an object in {source}.");
            if (app["security"] is JsonObject security)
            {
                RequireBoolean(security, "freezePrototype", source);
                if (security["capabilities"] is not null and not JsonArray) throw new NetWivConfigException($"'app.security.capabilities' must be an array in {source}.");
                if (security.ContainsKey("assetProtocol")) ValidateAssetProtocol(security["assetProtocol"], source);
            }
        }

        if (root["build"] is JsonObject build)
        {
            foreach (var hook in new[] { "beforeBuildCommand", "beforeBundleCommand", "beforeDevCommand" })
                ValidateHook(build[hook], hook, source);
            RequireString(build, "devUrl", source); RequireString(build, "frontendDist", source); RequireString(build, "dotnetProject", source);
            if (build["runner"] is { } runner) _ = RunnerConfig.FromJson(runner);
            RequireStringArray(build, "features", source);
            RequireStringArray(build, "additionalWatchFolders", source);
        }

        if (root["bundle"] is JsonObject bundle)
        {
            _ = WindowsInstallerOptions.FromJson(root);
            RequireString(bundle, "category", source);
            if (bundle["category"] is JsonValue category) _ = Packaging.AppCategory.Parse(category.GetValue<string>());
            if (bundle["resources"] is JsonArray resources) RequireStringArray(bundle, "resources", source);
            else if (bundle["resources"] is JsonObject resourceMap)
            {
                foreach (var resource in resourceMap)
                    if (resource.Value is not JsonValue value || !value.TryGetValue<string>(out _))
                        throw new NetWivConfigException($"'bundle.resources' destinations must be strings in {source}.");
            }
            else if (bundle["resources"] is not null)
                throw new NetWivConfigException($"'bundle.resources' must be an array of paths or an object of mappings in {source}.");
            RequireBoolean(bundle, "active", source); RequireString(bundle, "license", source); RequireString(bundle, "shortDescription", source); RequireString(bundle, "publisher", source);
            if (bundle["targets"] is JsonArray targets)
            {
                foreach (var targetNode in targets)
                    if (targetNode is not JsonValue value || !value.TryGetValue<string>(out var item) || !KnownBundleTarget(item)) throw new NetWivConfigException($"Invalid bundle target in {source}.");
            }
            else if (bundle["targets"] is not null)
            {
                if (bundle["targets"] is not JsonValue targetValue || !targetValue.TryGetValue<string>(out var target))
                    throw new NetWivConfigException($"'bundle.targets' must be a string or array in {source}.");
                if (target != "all" && !KnownBundleTarget(target)) throw new NetWivConfigException($"Unknown bundle target '{target}' in {source}.");
            }
        }
    }

    private static void ValidateHook(JsonNode? node, string name, string source)
    {
        if (node is null || node is JsonValue text && text.TryGetValue<string>(out _)) return;
        if (node is not JsonObject hook || hook["script"] is not JsonValue script || !script.TryGetValue<string>(out _))
            throw new NetWivConfigException($"'build.{name}' must be a string or an object with a string 'script' in {source}.");
        RequireString(hook, "cwd", source);
        if (name == "beforeDevCommand" && hook.ContainsKey("wait") &&
            (hook["wait"] is not JsonValue wait || !wait.TryGetValue<bool>(out _)))
            throw new NetWivConfigException($"'build.beforeDevCommand.wait' must be a boolean in {source}.");
    }

    private static bool KnownBundleTarget(string value) => value.ToLowerInvariant() is "all" or "deb" or "rpm" or "appimage" or "msi" or "nsis" or "dmg" or "app" or "updater";
    private static void ValidateAssetProtocol(JsonNode? node, string source)
    {
        if (node is not JsonObject config) throw new NetWivConfigException($"'app.security.assetProtocol' must be an object in {source}.");
        if (config.Any(static property => property.Key is not ("enable" or "scope")))
            throw new NetWivConfigException($"Unknown assetProtocol configuration key in {source}.");
        if (config.ContainsKey("enable") && config["enable"] is null) throw new NetWivConfigException($"'assetProtocol.enable' must be a boolean in {source}.");
        RequireBoolean(config, "enable", source);
        if (!config.ContainsKey("scope")) return;
        if (config["scope"] is JsonArray)
        {
            RequireStringArray(config, "scope", source);
            return;
        }
        if (config["scope"] is not JsonObject scope) throw new NetWivConfigException($"'assetProtocol.scope' must be an array or object in {source}.");
        foreach (var key in new[] { "allow", "deny" })
        {
            if (scope.ContainsKey(key) && scope[key] is null) throw new NetWivConfigException($"'assetProtocol.scope.{key}' must be a string array in {source}.");
            RequireStringArray(scope, key, source);
        }
        if (scope.ContainsKey("requireLiteralLeadingDot") && scope.ContainsKey("require-literal-leading-dot"))
            throw new NetWivConfigException($"Duplicate requireLiteralLeadingDot field in {source}.");
        RequireBoolean(scope, "requireLiteralLeadingDot", source);
        RequireBoolean(scope, "require-literal-leading-dot", source);
    }
    private static void RequireString(JsonObject owner, string name, string source)
    {
        if (owner[name] is not null && (owner[name] is not JsonValue value || !value.TryGetValue<string>(out _))) throw new NetWivConfigException($"'{name}' must be a string in {source}.");
    }
    private static void RequireBoolean(JsonObject owner, string name, string source)
    {
        if (owner[name] is not null && (owner[name] is not JsonValue value || !value.TryGetValue<bool>(out _))) throw new NetWivConfigException($"'{name}' must be a boolean in {source}.");
    }
    private static void RequireNumber(JsonObject owner, string name, string source)
    {
        if (owner[name] is not null && (owner[name] is not JsonValue value || (!value.TryGetValue<double>(out _) && !value.TryGetValue<long>(out _)))) throw new NetWivConfigException($"'{name}' must be a number in {source}.");
    }
    private static void RequireStringArray(JsonObject owner, string name, string source)
    {
        if (owner[name] is null) return;
        if (owner[name] is not JsonArray values || values.Any(value => value is not JsonValue item || !item.TryGetValue<string>(out _)))
            throw new NetWivConfigException($"'build.{name}' must be an array of strings in {source}.");
    }
}
