using NetWiv.Build.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetWiv.Cli;

internal static class CommandConfig
{
    internal static string? BlazorMode(string? value) => value switch
    {
        null => null,
        _ when value.Equals("Hybrid", StringComparison.OrdinalIgnoreCase) => "Hybrid",
        _ when value.Equals("WebAssembly", StringComparison.OrdinalIgnoreCase) => "WebAssembly",
        _ => throw new ArgumentException("--blazor-mode must be Hybrid or WebAssembly.")
    };

    public static (ConfigDocument Config, Build.Execution.BuildTarget Target) Load(CommandArguments arguments, string workingDirectory)
    {
        var overlays = arguments.TakeOptions("--config", "-c");
        var targetName = arguments.TakeOption("--target", "-t");
        var target = Build.Execution.BuildTarget.Resolve(targetName);
        var config = ConfigLoader.Load(workingDirectory, target.Platform, overlays);
        return (config, target);
    }

    public static string Project(ConfigDocument config)
    {
        var configured = config.String("build", "dotnetProject");
        if (configured is null) return Build.Execution.ProjectLocator.FindApplicationProject(config.ConfigDirectory);
        var path = Path.GetFullPath(configured, config.ConfigDirectory);
        if (!File.Exists(path)) throw new FileNotFoundException($"Configured build.dotnetProject does not exist: {path}");
        return path;
    }

    public static IReadOnlyList<string> Features(ConfigDocument config, string? commandLine)
    {
        var values = new List<string>(config.Strings("build", "features"));
        if (!string.IsNullOrWhiteSpace(commandLine))
            values.AddRange(commandLine.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var feature in values)
            if (!feature.All(character => char.IsLetterOrDigit(character) || character is '_' or '-'))
                throw new ArgumentException($"Invalid feature name '{feature}'.");
        return values.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static string? IsolationDirectory(ConfigDocument config) => config.String("app", "security", "pattern", "use") == "isolation"
        ? Path.GetFullPath(config.String("app", "security", "pattern", "options", "dir") ??
            throw new NetWivConfigException("app.security.pattern.options.dir is required for isolation."), config.ConfigDirectory) : null;

    public static string WriteEffectiveConfig(ConfigDocument config, Build.Execution.BuildTarget target, bool debug, string? developmentUrl = null)
    {
        var root = (JsonObject)config.Root.DeepClone();
        if (developmentUrl is not null)
        {
            if (root["build"] is not JsonObject build) root["build"] = build = new JsonObject();
            build["devUrl"] = developmentUrl;
        }
        var capabilitySources = ExpandCapabilities(root, config.ConfigDirectory, config.BasePath);
        EffectivePermissions.Apply(root, config.ConfigDirectory, target.Platform, capabilitySources);
        var directory = Path.Combine(config.ConfigDirectory, "obj", "netwiv", target.RequestedTarget, debug ? "debug" : "release");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "effective.conf.json");
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        return path;
    }

    private static IReadOnlyList<string> ExpandCapabilities(JsonObject root, string configDirectory, string configSource)
    {
        if (root["app"] is not JsonObject app) root["app"] = app = new JsonObject();
        if (app["security"] is not JsonObject security) app["security"] = security = new JsonObject();
        var configured = security["capabilities"] as JsonArray;
        var expanded = new JsonArray();
        var sources = new List<string>();
        if (configured is null)
        {
            var directory = Path.Combine(configDirectory, "capabilities");
            if (Directory.Exists(directory))
                foreach (var path in Directory.EnumerateFiles(directory).Where(IsConfigFile).OrderBy(path => path, StringComparer.Ordinal))
                    AddCapability(expanded, sources, ConfigLoader.ParseFile(path), path);
        }
        else
        {
            foreach (var entry in configured)
            {
                if (entry is not JsonValue value || !value.TryGetValue<string>(out var selector))
                {
                    expanded.Add(entry?.DeepClone());
                    sources.Add(configSource);
                    continue;
                }
                var matches = FindCapabilityFiles(configDirectory, selector).ToArray();
                if (matches.Length == 0) throw new NetWivConfigException($"Capability selector '{selector}' did not match a JSON, JSON5, or TOML file.");
                foreach (var path in matches) AddCapability(expanded, sources, ConfigLoader.ParseFile(path), path);
            }
        }
        security["capabilities"] = expanded;
        return sources;
    }

    private static IEnumerable<string> FindCapabilityFiles(string configDirectory, string selector)
    {
        var directory = Path.Combine(configDirectory, "capabilities");
        if (!Directory.Exists(directory)) yield break;
        var pattern = Path.GetFileName(selector);
        if (Path.GetExtension(pattern).Length == 0) pattern += ".*";
        foreach (var path in Directory.EnumerateFiles(directory).Where(IsConfigFile).OrderBy(path => path, StringComparer.Ordinal))
            if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path), ignoreCase: OperatingSystem.IsWindows()))
                yield return path;
    }

    private static bool IsConfigFile(string path) => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".json5", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".toml", StringComparison.OrdinalIgnoreCase);

    private static void AddCapability(JsonArray destination, List<string> sources, JsonNode node, string source)
    {
        if (node is JsonObject named && named.ContainsKey("capabilities"))
        {
            if (named["capabilities"] is not JsonArray list) throw new NetWivConfigException($"Named capabilities must be an array: {source}");
            AddCapability(destination, sources, list, source);
            return;
        }
        if (node is JsonArray array)
        {
            foreach (var item in array) { destination.Add(item?.DeepClone()); sources.Add(source); }
            return;
        }
        if (node is JsonObject capability) { destination.Add((JsonNode)capability); sources.Add(source); return; }
        throw new NetWivConfigException($"Capability file must contain an object or array: {source}");
    }
}
