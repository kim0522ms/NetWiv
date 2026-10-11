using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Runtime;
#if NETWIV_WRY
using NetWiv.Platform.Desktop;
#elif WINDOWS
using NetWiv.Platform.Windows;
#else
using NetWiv.Platform.Linux;
#endif

[assembly: CommandJsonContext(typeof(NetWiv.Conformance.ConformanceJsonContext))]

namespace NetWiv.Conformance;

internal static class Program
{
    private static readonly object ReportGate = new();
    private static readonly string SuiteDirectory = FindSuiteDirectory();
    private static readonly string DistributionDirectory = Path.Combine(SuiteDirectory, "dist");
    private static readonly string ReportPath = Environment.GetEnvironmentVariable("NETWIV_CONFORMANCE_RESULT")
        ?? Path.Combine(SuiteDirectory, "results", "netwiv.jsonl");

    [STAThread]
    private static int Main()
    {
        using var assets = new AssetFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath))!);
        File.Delete(ReportPath);

#if !NETWIV_EMBEDDED_CONFIG
        var capability = new Capability(
            "default",
            [
                "core:default",
                "core:webview:allow-create-webview-window", "core:webview:allow-create-webview",
                "core:webview:allow-webview-close", "core:webview:allow-reparent",
                "core:webview:allow-set-webview-background-color", "core:window:allow-create",
                "core:window:allow-destroy", "core:window:allow-set-background-color",
                "core:window:allow-set-badge-count", "core:window:allow-set-progress-bar",
                "core:window:allow-set-size", "core:window:allow-set-position",
                "core:window:allow-close", "core:window:allow-set-title", "core:window:allow-set-resizable",
                "core:app:allow-fetch-data-store-identifiers", "core:app:allow-remove-data-store",
                "core:app:allow-app-show", "core:app:allow-app-hide", "core:app:allow-set-dock-visibility",
                "literal-command-keys", "allow-echo-json", "allow-always-fails", "allow-echo-bytes", "allow-stream-numbers",
                "scope-read", "scope-write",
                "allow-custom-default-allowed", "allow-typed-request", "allow-conformance-record", "allow-conformance-finish", "allow-conformance-profile", "allow-conformance-cases"
            ],
            Windows: ["main", "conformance-child"],
            PermissionScopes: [new("scope-read", [JsonValue.From("extension")]), new("scope-write", [JsonValue.From("extension")])]);
#endif

        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var builder = new Builder()
#if NETWIV_WRY
            .UseRuntime(new DesktopRuntime())
#elif WINDOWS
            .UseRuntime(new WindowsDesktopRuntime())
#else
            .UseRuntime(new LinuxDesktopRuntime())
#endif
            .Metadata(new AppMetadata("NetWiv Conformance", "0.1.0", "dev.netwiv.conformance"))
            .Window("main", "Tauri App", "index.html", 800, 600)
#if NETWIV_EMBEDDED_CONFIG
            .UseEmbeddedConfig()
            .Plugin(new ConfigurationPlugin())
#else
            .Permission(new("literal-command-keys", ["*", "custom_*"], ["echo_*"]))
            .Permission(new("scope-read", ["scope_read"], [], [JsonValue.From("read")], [JsonValue.From("private")]))
            .Permission(new("scope-write", ["scope_write"], [], [JsonValue.From("write")]))
            .Capability(capability)
            .Capability(new("reparent-events", ["core:event:default"], Webviews: ["moving-child"]))
            .AppCommandManifest("echo_json", "always_fails", "echo_bytes", "stream_numbers", "custom_default_allowed",
                "typed_request", "restricted", "conformance_record", "conformance_finish", "conformance_profile", "conformance_cases")
#endif
            .ResolveAssets(ResolveAsset)
            .RegisterUriSchemeProtocol("contract-sync", (context, request) => ResolveProtocol(context, request, 201))
            .RegisterAsynchronousUriSchemeProtocol("contract-async", async (context, request) =>
            {
                await Task.Delay(10);
                return ResolveProtocol(context, request, 202);
            })
            .RegisterCommand("asset_fixture", (_, _, _) => Value(JsonValue.From(assets.DirectoryPath)))
            .RegisterCommand("echo_json", static (_, arguments, _) =>
                Value(arguments.GetProperty("input").Clone()))
            .RegisterCommand("always_fails", static (_, _, _) =>
                throw new InvokeException("intentional conformance error"))
            .InvokeHandler("typed_request")
            .InvokeHandler(registry => registry.RegisterResponse("echo_bytes", static (_, arguments, _) =>
            {
                var input = arguments.GetProperty("bytes");
                var bytes = new byte[input.GetArrayLength()];
                var index = 0;
                foreach (var value in input.EnumerateArray()) bytes[index++] = value.GetByte();
                return ValueTask.FromResult(RuntimeInvokeResponse.FromBinary(bytes));
            }))
            .RegisterCommand("stream_numbers", static (context, arguments, _) =>
            {
                using var channel = Channel.FromJson(context, arguments.GetProperty("channel"));
                for (var value = 0; value < 3; value++) channel.Send(JsonValue.From(value));
                return Value(JsonValue.Null);
            })
            .RegisterCommand("custom_default_allowed", static (_, _, _) => Value(JsonValue.From("allowed")))
            .RegisterCommand("restricted", static (_, _, _) => Value(JsonValue.From("must be denied without its permission")))
            .RegisterCommand("scope_read", ScopeValue)
            .RegisterCommand("scope_write", ScopeValue)
            .RegisterCommand("conformance_cases", static (_, _, _) =>
                Value(JsonValue.Parse(Environment.GetEnvironmentVariable("NETWIV_CONFORMANCE_CASES") ?? "null")))
            .RegisterCommand("conformance_record", static (_, arguments, _) =>
            {
                lock (ReportGate) File.AppendAllText(ReportPath, arguments.GetProperty("result").GetRawText() + Environment.NewLine, Encoding.UTF8);
                return Value(JsonValue.Null);
            })
            .RegisterCommand("conformance_finish", static (context, _, _) =>
            {
                context.App.Exit(0);
                return Value(JsonValue.Null);
            })
            .RegisterCommand("conformance_profile", static (_, _, _) => Value(JsonValue.From(
#if DEBUG
                "debug"
#else
                "release"
#endif
            )));

        using var app = builder.Build();
        watchdog.Token.Register(() =>
        {
            lock (ReportGate)
            {
                File.AppendAllText(ReportPath, "{\"type\":\"harness-error\",\"error\":\"NetWiv host timed out\"}" + Environment.NewLine, Encoding.UTF8);
            }
            app.Handle.Exit(124);
        });
        app.Run();
        return 0;
    }

    private static AssetResponse ResolveProtocol(UriSchemeContext context, AssetRequest request, int status)
    {
        var headers = new Dictionary<string, string>
        {
            ["Access-Control-Allow-Origin"] = "*", ["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS",
            ["Access-Control-Allow-Headers"] = "x-request,content-type", ["Access-Control-Expose-Headers"] = "X-Webview,X-Method,X-Request",
            ["X-Webview"] = context.WebviewLabel, ["X-Method"] = request.Method,
            ["X-Request"] = request.Headers.TryGetValue("X-Request", out var value) ? value : ""
        };
        if (request.Method == "OPTIONS") return new(204, "text/plain", ReadOnlyMemory<byte>.Empty, headers);
        var file = request.Uri.AbsolutePath.TrimStart('/');
        if (file == "page") return new(200, "text/html", File.ReadAllBytes(Path.Combine(SuiteDirectory, "frontend/protocol.html")), headers);
        if (file is "core.js" or "event.js" or "external/tslib/tslib.es6.js")
            return new(200, "text/javascript", File.ReadAllBytes(Path.Combine(DistributionDirectory, "vendor/@tauri-apps/api", file)), headers);
        return new(status, "application/octet-stream", request.Body, headers);
    }

    private static ValueTask<JsonElement> ScopeValue(InvokeContext context, JsonElement arguments, CancellationToken cancellationToken) =>
        Value(JsonValue.Parse("{\"allow\":[" + string.Join(',', context.Scope.Allow.Select(value => value.GetRawText())) +
            "],\"deny\":[" + string.Join(',', context.Scope.Deny.Select(value => value.GetRawText())) + "]}"));

    private static AssetResponse? ResolveAsset(AssetRequest request)
    {
        var relative = Uri.UnescapeDataString(request.Uri.AbsolutePath.TrimStart('/'));
        if (string.IsNullOrEmpty(relative)) relative = "index.html";
        var fullPath = Path.GetFullPath(Path.Combine(DistributionDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(Path.GetFullPath(DistributionDirectory) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(fullPath)) return null;
        var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            _ => "application/octet-stream"
        };
        return new AssetResponse(200, contentType, File.ReadAllBytes(fullPath));
    }

    private static string FindSuiteDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "conformance");
            if (Directory.Exists(Path.Combine(candidate, "frontend"))) return candidate;
            current = current.Parent;
        }
        var configured = Environment.GetEnvironmentVariable("NETWIV_CONFORMANCE_ROOT");
        return configured is not null ? Path.GetFullPath(configured) : throw new DirectoryNotFoundException("Set NETWIV_CONFORMANCE_ROOT to tests/conformance.");
    }

    private static ValueTask<JsonElement> Value(JsonElement value) => ValueTask.FromResult(value);
}

public sealed record TypedRequest(string Message, uint Count);

internal sealed class ConfigurationPlugin : IPlugin
{
    public string Name => "contract";
    public void Initialize(PluginContext context)
    {
        var config = context.ReadConfig(ConformanceJsonContext.Default.TypedRequest)
            ?? throw new InvalidOperationException("Missing contract plugin configuration.");
        context.Permission(new("allow-read-config", "read_config"));
        context.RegisterCommand("read_config", (_, _, _) => ValueTask.FromResult(
            JsonSerializer.SerializeToElement(config, ConformanceJsonContext.Default.TypedRequest)));
    }
}

public static class ConformanceCommands
{
    [Command("typed_request")]
    public static TypedRequest TypedRequest(TypedRequest request) => request;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(TypedRequest))]
public partial class ConformanceJsonContext : JsonSerializerContext;
