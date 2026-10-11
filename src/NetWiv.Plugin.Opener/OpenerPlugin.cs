using System.Diagnostics;
using System.Text.Json;

namespace NetWiv.Plugin.Opener;

/// <summary>Scoped external URL and path opening for the official opener guest.</summary>
public sealed class OpenerPlugin : IPlugin
{
    private readonly Action<ProcessStartInfo> _launch;

    public OpenerPlugin() : this(Start) { }
    internal OpenerPlugin(Action<ProcessStartInfo> launch) => _launch = launch;

    public string Name => "opener";

    public void Initialize(PluginContext context)
    {
        foreach (var command in new[] { "open_url", "open_path", "reveal_item_in_dir" })
        {
            var name = command;
            context.RegisterCommand(name, (invoke, args, token) => Execute(name, invoke, args, token));
            context.Permission(new("allow-" + name.Replace('_', '-'), [name], []));
            context.Permission(new("deny-" + name.Replace('_', '-'), [], [name]));
        }
        context.Permission(new("allow-default-urls", [], [],
            [JsonValue.Parse("""{"url":"mailto:*"}"""), JsonValue.Parse("""{"url":"tel:*"}"""),
                JsonValue.Parse("""{"url":"https://*"}"""), JsonValue.Parse("""{"url":"http://*"}""")]));
        context.PermissionSet("default", "allow-open-url", "allow-reveal-item-in-dir", "allow-default-urls");
    }

    private ValueTask<JsonElement> Execute(string command, InvokeContext context, JsonElement args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (command == "open_url")
        {
            var url = Text(args, "url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) throw new InvokeException("The URL is invalid.");
            if (!context.IsTrusted && !Allowed(context.Scope.Allow, context.Scope.Deny, "url", url))
                throw new InvokeException("URL is not allowed by opener scope.");
            Launch(parsed.AbsoluteUri, Program(args));
        }
        else if (command == "open_path")
        {
            var path = Path.GetFullPath(Text(args, "path"));
            if (!context.IsTrusted && !AllowedPath(context, path)) throw new InvokeException("Path is not allowed by opener scope.");
            if (!File.Exists(path) && !Directory.Exists(path)) throw new InvokeException("The path does not exist.");
            Launch(path, Program(args));
        }
        else if (command == "reveal_item_in_dir")
        {
            var paths = args.GetProperty("paths");
            if (paths.ValueKind != JsonValueKind.Array || paths.GetArrayLength() == 0) throw new InvokeException("Reveal requires paths.");
            var macReveal = OperatingSystem.IsMacOS() ? new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false } : null;
            macReveal?.ArgumentList.Add("-R");
            foreach (var item in paths.EnumerateArray())
            {
                var path = Path.GetFullPath(item.GetString() ?? throw new InvokeException("Reveal path must be a string."));
                if (macReveal is not null)
                {
                    if (!File.Exists(path) && !Directory.Exists(path)) throw new InvokeException("The path does not exist.");
                    macReveal.ArgumentList.Add(path);
                    continue;
                }
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (folder is null || !Directory.Exists(folder)) throw new InvokeException("The parent directory does not exist.");
                if (OperatingSystem.IsWindows())
                {
                    var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                    info.ArgumentList.Add("/select,"); info.ArgumentList.Add(path);
                    _launch(info);
                }
                else Launch(folder, null);
            }
            // Validate the entire batch before Finder can change its selection.
            if (macReveal is not null) _launch(macReveal);
        }
        else throw new InvokeException("Unknown opener command.");
        return ValueTask.FromResult(JsonValue.Null);
    }

    private static string? Program(JsonElement args) => args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty("with", out var value) && value.ValueKind == JsonValueKind.String
            ? throw new InvokeException("Opening with a named application requires a NetWiv opener policy and is not implemented.")
            : null;

    private static bool AllowedPath(InvokeContext context, string path)
    {
        var allow = context.Scope.Allow.Where(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("path", out _))
            .Select(entry => entry.GetProperty("path").GetString()!).ToArray();
        var deny = context.Scope.Deny.Where(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("path", out _))
            .Select(entry => entry.GetProperty("path").GetString()!).ToArray();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("allow"); writer.WriteStartArray(); foreach (var pattern in allow) writer.WriteStringValue(pattern); writer.WriteEndArray();
            writer.WritePropertyName("deny"); writer.WriteStartArray(); foreach (var pattern in deny) writer.WriteStringValue(pattern); writer.WriteEndArray();
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return new FileScope(document.RootElement, context.App.Metadata.Identifier).IsAllowed(path);
    }

    private static bool Allowed(IReadOnlyList<JsonElement> allow, IReadOnlyList<JsonElement> deny, string field, string value)
    {
        bool Matches(JsonElement entry)
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty(field, out var pattern) || pattern.ValueKind != JsonValueKind.String) return false;
            return new UrlScopePattern(pattern.GetString()!).Matches(new Uri(value));
        }
        return !deny.Any(Matches) && allow.Any(Matches);
    }

    private static string Text(JsonElement args, string name) => args.ValueKind == JsonValueKind.Object &&
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw new InvokeException("Missing opener argument '" + name + "'.");

    private void Launch(string target, string? program)
    {
        ProcessStartInfo info;
        if (program is not null)
        {
            info = new(program) { UseShellExecute = false };
            info.ArgumentList.Add(target);
        }
        else if (OperatingSystem.IsWindows()) info = new(target) { UseShellExecute = true };
        else { info = new(OperatingSystem.IsMacOS() ? "/usr/bin/open" : "xdg-open") { UseShellExecute = false }; info.ArgumentList.Add(target); }
        _launch(info);
    }

    private static void Start(ProcessStartInfo info)
    {
        try { using var process = System.Diagnostics.Process.Start(info) ?? throw new InvokeException("No application handled the request."); }
        catch (System.ComponentModel.Win32Exception error) { throw new InvokeException(error.Message, error); }
    }
}
