using System.Text.Json;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NetWiv.Runtime;

namespace NetWiv;

public sealed class Builder
{
    private readonly List<WindowOptions> _windows = [];
    private readonly List<Action<StateStore>> _stateRegistrations = [];
    private readonly List<Action<CommandRegistry>> _commandRegistrations = [];
    private readonly List<IPlugin> _plugins = [];
    private readonly List<PermissionDefinition> _permissions = [];
    private readonly List<Capability> _capabilities = [];
    private readonly HashSet<string> _developmentOrigins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _appManifestCommands = new(StringComparer.Ordinal);
    private bool _hasAppCommandManifest;
    private bool _hasEmbeddedConfiguration;
    private byte[]? _encodedDefaultWindowIcon;
    private readonly List<Action<App>> _setup = [];
    private readonly List<Action<AppHandle, MenuEvent>> _menuEventHandlers = [];
    private readonly List<Action<AppHandle, TrayIconEvent>> _trayEventHandlers = [];
    private readonly List<Func<AppHandle, ReopenEvent, Task>> _reopenHandlers = [];
    private Func<AssetRequest, AssetResponse?> _assetResolver = static _ => null;
    private Func<IReadOnlyList<string>> _globalScriptHashes = static () => [];
    private IReadOnlyDictionary<string, string> _responseHeaders = new Dictionary<string, string>();
    private string? _contentSecurityPolicy;
    private IReadOnlySet<string> _disabledCspModifications = new HashSet<string>();
    private bool _freezePrototype;
    private IDesktopRuntime? _runtime;
    private Func<IServiceProvider, IDesktopRuntime>? _runtimeFactory;
    private AppMetadata _metadata = AppMetadata.Default;
    private ImageData? _defaultWindowIcon;
    private JsonElement _assetProtocolConfig;
    private IReadOnlyDictionary<string, JsonElement> _pluginConfigurations = new Dictionary<string, JsonElement>();
    private Assembly? _isolationAssembly;
    private IServiceProvider? _services;
    private ILoggerFactory? _loggerFactory;
    private readonly Dictionary<string, Func<UriSchemeContext, AssetRequest, Task<AssetResponse>>> _uriSchemeProtocols = new(StringComparer.Ordinal);

    public Builder UseRuntime(IDesktopRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _runtimeFactory = null;
        return this;
    }
    /// <summary>Creates the runtime at build time using the configured root service provider.</summary>
    /// <remarks>The factory must not resolve AppHandle, which is available only after construction completes.</remarks>
    public Builder UseRuntime(Func<IServiceProvider, IDesktopRuntime> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _runtimeFactory = factory;
        _runtime = null;
        return this;
    }
    /// <summary>
    /// Supplies the root service provider for instance command receivers and <c>[FromServices]</c> command parameters.
    /// Each command invocation runs in its own scope when the provider supports scopes.
    /// The caller keeps ownership of the provider and disposes it after the app.
    /// </summary>
    public Builder UseServiceProvider(IServiceProvider services) { ArgumentNullException.ThrowIfNull(services); _services = services; return this; }
    /// <summary>Supplies the logger factory for NetWiv diagnostics. Defaults to the provider's factory, then to a no-op factory.</summary>
    public Builder UseLoggerFactory(ILoggerFactory loggerFactory) { ArgumentNullException.ThrowIfNull(loggerFactory); _loggerFactory = loggerFactory; return this; }
    public Builder Metadata(AppMetadata metadata) { _metadata = metadata; return this; }
    public Builder DefaultWindowIcon(ImageData icon) { ArgumentNullException.ThrowIfNull(icon); _defaultWindowIcon = icon; return this; }
    public Builder Window(WindowOptions options) { _windows.Add(options); return this; }
    public Builder Window(string label, string title, string url, double width = 800, double height = 600, JsonElement extraConfig = default) =>
        Window(new WindowOptions(label, title, url, width, height, extraConfig));
    public Builder Manage<T>(T state) where T : notnull
    {
        _stateRegistrations.Add(store =>
        {
            if (!store.Manage(state)) throw new InvalidOperationException($"State '{typeof(T)}' is already managed.");
        });
        return this;
    }
    public Builder RegisterCommand(string name, CommandHandler handler) { _commandRegistrations.Add(registry => registry.Register(name, handler)); return this; }
    public Builder InvokeHandler(Action<CommandRegistry> registration) { _commandRegistrations.Add(registration); return this; }
    public Builder InvokeHandler(params string[] generatedCommandNames)
    {
        ArgumentNullException.ThrowIfNull(generatedCommandNames);
        _commandRegistrations.Add(registry => GeneratedCommandRegistry.ApplyTo(registry, generatedCommandNames));
        return this;
    }
    public Builder RegisterGeneratedCommands() { _commandRegistrations.Add(GeneratedCommandRegistry.ApplyAllTo); return this; }
    public Builder Plugin(IPlugin plugin) { _plugins.Add(plugin); return this; }
    public Builder Permission(PermissionDefinition permission) { _permissions.Add(permission); return this; }
    public Builder Capability(Capability capability) { _capabilities.Add(capability); return this; }
    public Builder AppCommandManifest(params string[] commands)
    {
        _hasAppCommandManifest = true;
        foreach (var command in commands)
        {
            if (!_appManifestCommands.Add(command)) continue;
            var permissionName = command.Replace('_', '-');
            _permissions.Add(new($"allow-{permissionName}", [command], []));
            _permissions.Add(new($"deny-{permissionName}", [], [command]));
        }
        return this;
    }
    public Builder DevelopmentOrigin(string origin) { _developmentOrigins.Add(NormalizeDevelopmentOrigin(origin)); return this; }
    public Builder Setup(Action<App> setup) { _setup.Add(setup); return this; }
    public Builder OnMenuEvent(Action<AppHandle, MenuEvent> handler) { ArgumentNullException.ThrowIfNull(handler); _menuEventHandlers.Add(handler); return this; }
    public Builder OnTrayIconEvent(Action<AppHandle, TrayIconEvent> handler) { ArgumentNullException.ThrowIfNull(handler); _trayEventHandlers.Add(handler); return this; }
    /// <inheritdoc cref="AppHandle.OnReopen(Func{AppHandle, ReopenEvent, Task})"/>
    public Builder OnReopen(Func<AppHandle, ReopenEvent, Task> handler) { ArgumentNullException.ThrowIfNull(handler); _reopenHandlers.Add(handler); return this; }
    /// <inheritdoc cref="AppHandle.OnReopen(Func{AppHandle, ReopenEvent, Task})"/>
    public Builder OnReopen(Action<AppHandle, ReopenEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return OnReopen((app, value) => { handler(app, value); return Task.CompletedTask; });
    }
    public Builder ResolveAssets(Func<AssetRequest, AssetResponse?> resolver) { _assetResolver = resolver; _globalScriptHashes = static () => []; return this; }
    public Builder RegisterUriSchemeProtocol(string uriScheme, Func<UriSchemeContext, AssetRequest, AssetResponse> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RegisterAsynchronousUriSchemeProtocol(uriScheme, (context, request) => Task.FromResult(handler(context, request)));
    }
    public Builder RegisterAsynchronousUriSchemeProtocol(string uriScheme, Func<UriSchemeContext, AssetRequest, Task<AssetResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrEmpty(uriScheme) || !Uri.CheckSchemeName(uriScheme) || uriScheme != uriScheme.ToLowerInvariant())
            throw new ArgumentException("A URI protocol requires a lowercase URI scheme name.", nameof(uriScheme));
        _uriSchemeProtocols[uriScheme] = handler;
        return this;
    }
    public Builder UseEmbeddedAssets(Assembly? assembly = null, string resourcePrefix = "netwiv.assets.")
    {
        var assets = new EmbeddedAssets(assembly ?? Assembly.GetEntryAssembly() ??
            throw new InvalidOperationException("The entry assembly is unavailable."), resourcePrefix);
        _assetResolver = assets.Resolve;
        _globalScriptHashes = () => assets.ScriptHashes;
        return this;
    }
    public Builder UseEmbeddedConfig(EmbeddedConfigMode mode = EmbeddedConfigMode.Production, Assembly? assembly = null, string resourceName = "netwiv.config.json")
    {
        assembly ??= Assembly.GetEntryAssembly() ?? throw new InvalidOperationException("The entry assembly is unavailable.");
        var config = EmbeddedConfiguration.Load(assembly, resourceName, _metadata, mode);
        _encodedDefaultWindowIcon = null;
        using (var icon = assembly.GetManifestResourceStream("netwiv.default-icon"))
        {
            if (icon is not null)
            {
                using var bytes = new MemoryStream();
                icon.CopyTo(bytes);
                _encodedDefaultWindowIcon = bytes.ToArray();
            }
        }
        _metadata = config.Metadata;
        _hasEmbeddedConfiguration = true;
        if (config.HasWindowList)
        {
            _windows.Clear();
            _windows.AddRange(config.Windows);
        }
        _capabilities.AddRange(config.Capabilities);
        _permissions.AddRange(config.Acl.Permissions);
        _hasAppCommandManifest |= config.Acl.HasAppManifest;
        _responseHeaders = config.Headers;
        _contentSecurityPolicy = config.ContentSecurityPolicy;
        _disabledCspModifications = config.DisabledCspModifications;
        _freezePrototype = config.FreezePrototype;
        _assetProtocolConfig = config.AssetProtocol;
        _pluginConfigurations = config.Plugins;
        _isolationAssembly = config.Isolation ? assembly : null;
        if (mode == EmbeddedConfigMode.Development && config.DevelopmentUrl is not null)
        {
            DevelopmentOrigin(config.DevelopmentUrl);
            ApplyDevelopmentUrl(config.DevelopmentUrl);
        }
        return this;
    }

    public App Build()
    {
        var runtime = _runtimeFactory is null
            ? _runtime ?? throw new InvalidOperationException("A desktop runtime must be configured with UseRuntime.")
            : _runtimeFactory(_services ?? throw new InvalidOperationException("A runtime factory requires a service provider. Configure UseServiceProvider or use NetWiv.Hosting."))
                ?? throw new InvalidOperationException("The runtime factory returned null.");
        var windows = _windows.Count == 0 && !_hasEmbeddedConfiguration
            ? [new WindowOptions("main", _metadata.Name, "index.html", 800, 600, default)]
            : _windows.ToArray();
        EnsureUniqueLabels(windows);
        var fileAssets = FileAssetProtocol.Create(_assetProtocolConfig, _metadata.Identifier);

        var state = new StateStore();
        var initializedPlugins = new List<IPlugin>();
        IsolationPattern? isolation = null;
        try
        {
            if (_isolationAssembly is not null) isolation = new IsolationPattern(_isolationAssembly);
            foreach (var registration in _stateRegistrations) registration(state);
            var commands = new CommandRegistry();
            foreach (var registration in _commandRegistrations) registration(commands);
            var plugins = GeneratedPluginRegistry.CreateAll().Concat(_plugins).ToArray();
            var permissions = _permissions.ToList();
            foreach (var plugin in plugins)
            {
                initializedPlugins.Add(plugin);
                var context = new PluginContext(plugin.Name, commands, permissions,
                    _pluginConfigurations.TryGetValue(plugin.Name, out var pluginConfig) ? pluginConfig : JsonValue.Null, plugin.GetType());
                plugin.Initialize(context);
                context.Complete();
            }

            var origins = new HashSet<string>(_developmentOrigins, StringComparer.OrdinalIgnoreCase)
            {
                "tauri://localhost",
                "http://tauri.localhost",
                "https://tauri.localhost"
            };
            var responsePolicy = CreateAssetResponsePolicy(isolation);
            var app = new App(runtime, _metadata, windows, state, commands, permissions, _capabilities, origins,
                _hasAppCommandManifest, CreateAssetResolver(fileAssets, isolation, responsePolicy), plugins, _freezePrototype, _setup.ToArray(),
                new Dictionary<string, Func<UriSchemeContext, AssetRequest, Task<AssetResponse>>>(_uriSchemeProtocols, StringComparer.Ordinal));
            app.Handle.DefaultWindowIcon = _defaultWindowIcon;
            app.Handle.EncodedDefaultWindowIcon = _defaultWindowIcon is null ? _encodedDefaultWindowIcon : null;
            app.Handle.ConfigureServices(_services, _loggerFactory);
            var entryAssembly = Assembly.GetEntryAssembly();
            var debugMetadata = entryAssembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "NetWiv.DebugAssertions")?.Value;
            app.Handle.DebugAssertions = debugMetadata is not null ? debugMetadata == "true"
                : entryAssembly?.GetCustomAttribute<System.Diagnostics.DebuggableAttribute>()?.IsJITOptimizerDisabled == true;
            app.Handle.AssetProtocolEnabled = fileAssets is not null;
            app.Handle.FileAssetScope = fileAssets?.Scope;
            app.Handle.Isolation = isolation;
            app.Handle.AssetResponsePolicy = responsePolicy;
            foreach (var handler in _menuEventHandlers) app.OnMenuEvent(handler);
            foreach (var handler in _trayEventHandlers) app.OnTrayIconEvent(handler);
            foreach (var handler in _reopenHandlers) app.OnReopen(handler);
            return app;
        }
        catch (Exception buildError)
        {
            isolation?.Dispose();
            List<Exception> errors = [buildError];
            foreach (var plugin in initializedPlugins)
            {
                try { plugin.Dispose(); }
                catch (Exception exception) { errors.Add(exception); }
            }
            try { state.Dispose(); }
            catch (Exception exception) { errors.Add(exception); }
            if (errors.Count > 1) throw new AggregateException("Application construction and cleanup failed.", errors);
            throw;
        }
    }

    public void Run()
    {
        using var app = Build();
        app.Run();
    }

    private static string NormalizeDevelopmentOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            throw new ArgumentException("Development origins must be absolute origins.", nameof(origin));
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !uri.IsLoopback)
            throw new ArgumentException("Development origins must be loopback HTTP(S) origins. Use a remote capability for other URLs.", nameof(origin));
        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static void EnsureUniqueLabels(IReadOnlyList<WindowOptions> windows)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var webviewLabels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var window in windows)
        {
            WindowBuilder.ValidateLabel(window.Label);
            if (!labels.Add(window.Label)) throw new InvalidOperationException($"Window label '{window.Label}' is duplicated.");
            if (window.Webviews is null)
            {
                if (!webviewLabels.Add(window.Label)) throw new InvalidOperationException($"Webview label '{window.Label}' is duplicated.");
                continue;
            }
            foreach (var webview in window.Webviews)
            {
                WindowBuilder.ValidateLabel(webview.Label);
                if (!webviewLabels.Add(webview.Label)) throw new InvalidOperationException($"Webview label '{webview.Label}' is duplicated.");
            }
        }
    }

    private Func<AssetRequest, AssetResponse?> CreateAssetResolver(FileAssetProtocol? fileAssets, IsolationPattern? isolation,
        Func<AssetRequest, AssetResponse, AssetResponse> responsePolicy)
    {
        var resolve = _assetResolver;
        var configuredHeaders = _responseHeaders;
        var disabledCspModifications = _disabledCspModifications;
        return request =>
        {
            if (isolation?.IsIsolationUri(request.Uri) == true) return isolation.Resolve(request, disabledCspModifications, configuredHeaders);
            if (FileAssetProtocol.IsAssetUri(request.Uri)) return fileAssets?.Resolve(request);
            var response = resolve(request);
            return response is null ? null : responsePolicy(request, response);
        };
    }

    private Func<AssetRequest, AssetResponse, AssetResponse> CreateAssetResponsePolicy(IsolationPattern? isolation)
    {
        var configuredHeaders = _responseHeaders;
        var configuredCsp = _contentSecurityPolicy;
        var disabledCspModifications = _disabledCspModifications;
        var globalScriptHashes = _globalScriptHashes;
        return (request, response) =>
        {
            if (configuredHeaders.Count == 0 && configuredCsp is null) return response;
            var headers = response.Headers is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase);
            foreach (var header in configuredHeaders) headers[header.Key] = header.Value;
            response = response with { Headers = headers };
            var csp = configuredCsp;
            if (csp is not null && isolation is not null)
            {
                var directives = ContentSecurityPolicy.Parse(csp);
                if (!directives.TryGetValue("default-src", out var sources)) directives.Add("default-src", sources = []);
                sources.Add(isolation.OriginSource(request.Uri));
                csp = ContentSecurityPolicy.Serialize(directives);
            }
            return csp is null ? response : ContentSecurityPolicy.Apply(csp, response, disabledCspModifications, globalScriptHashes(),
                isolation is null ? [] : [HtmlAssets.Hash(IsolationPattern.IframeStyle)]);
        };
    }

    private void ApplyDevelopmentUrl(string developmentUrl)
    {
        var baseUri = new Uri(developmentUrl, UriKind.Absolute);
        for (var index = 0; index < _windows.Count; index++)
        {
            var window = _windows[index];
            if (Uri.TryCreate(window.Url, UriKind.Absolute, out _)) continue;
            _windows[index] = window with { Url = new Uri(baseUri, window.Url).AbsoluteUri };
        }
    }
}
