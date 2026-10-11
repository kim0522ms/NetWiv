using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetWiv.Hosting;

/// <summary>
/// Builds a NetWiv desktop application on the .NET Generic Host. Services, configuration, logging and
/// hosted services follow the usual <see cref="IHostApplicationBuilder"/> conventions; desktop settings
/// such as the runtime, windows, commands and plugins are configured through <see cref="Desktop"/>.
/// </summary>
public sealed class NetWivApplicationBuilder : IHostApplicationBuilder
{
    private readonly HostApplicationBuilder _host;
    private bool _built;

    internal NetWivApplicationBuilder(HostApplicationBuilderSettings settings)
    {
        settings.ContentRootPath ??= DefaultContentRoot();
        _host = Host.CreateApplicationBuilder(settings);
    }

    /// <summary>The NetWiv desktop configuration: runtime, windows, commands, plugins and security.</summary>
    public Builder Desktop { get; } = new();
    public ConfigurationManager Configuration => _host.Configuration;
    public IHostEnvironment Environment => _host.Environment;
    public ILoggingBuilder Logging => _host.Logging;
    public IMetricsBuilder Metrics => _host.Metrics;
    public IServiceCollection Services => _host.Services;
    public IDictionary<object, object> Properties => ((IHostApplicationBuilder)_host).Properties;
    IConfigurationManager IHostApplicationBuilder.Configuration => Configuration;

    public void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory, Action<TContainerBuilder>? configure = null)
        where TContainerBuilder : notnull =>
        _host.ConfigureContainer(factory, configure);

    /// <summary>Builds the service provider and the NetWiv app. <see cref="AppHandle"/> becomes resolvable as a singleton.</summary>
    public NetWivApplication Build()
    {
        if (_built) throw new InvalidOperationException("The NetWiv application has already been built.");
        _built = true;
        var handle = new HandleHolder();
        Services.AddSingleton(_ => handle.Value);
        var host = _host.Build();
        try
        {
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var app = Desktop
                .UseServiceProvider(host.Services)
                .UseLoggerFactory(host.Services.GetRequiredService<ILoggerFactory>())
                // A stop requested before the event loop accepted work could not exit it; honor it once ready.
                .Setup(started => { if (lifetime.ApplicationStopping.IsCancellationRequested) started.Handle.Exit(); })
                .Build();
            handle.Value = app.Handle;
            return new NetWivApplication(host, app);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    // Matches the bundle layout: the macOS bundler moves the executable to Contents/MacOS and
    // stages every other publish file, including appsettings.json, in Contents/Resources.
    private static string DefaultContentRoot()
    {
        var directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (OperatingSystem.IsMacOS() && Path.GetFileName(directory) == "MacOS" &&
            Path.GetFileName(Path.GetDirectoryName(directory)) == "Contents" &&
            Path.GetDirectoryName(Path.GetDirectoryName(directory))?.EndsWith(".app", StringComparison.OrdinalIgnoreCase) == true)
            return Path.GetFullPath(Path.Combine(directory, "..", "Resources"));
        return directory;
    }

    private sealed class HandleHolder
    {
        private AppHandle? _value;

        public AppHandle Value
        {
            get => _value ?? throw new InvalidOperationException("AppHandle is unavailable until NetWivApplicationBuilder.Build completes.");
            set => _value = value;
        }
    }
}
