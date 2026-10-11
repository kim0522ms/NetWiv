using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NetWiv.Hosting;

/// <summary>
/// A NetWiv desktop app running inside the .NET Generic Host.
/// </summary>
/// <remarks>
/// <see cref="Run"/> starts hosted services, runs the native event loop on the calling thread and stops the
/// host after the loop returns. Call it from the process's original main thread, as with <see cref="NetWiv.App.Run"/>.
/// There is intentionally no <c>RunAsync</c>: an asynchronous continuation cannot own the native UI loop.
/// </remarks>
public sealed partial class NetWivApplication : IDisposable, IAsyncDisposable
{
    private readonly IHost _host;
    private readonly App _app;
    private readonly TimeSpan _shutdownTimeout;
    private int _started;
    private bool _disposed;

    internal NetWivApplication(IHost host, App app)
    {
        _host = host;
        _app = app;
        Lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Environment = host.Services.GetRequiredService<IHostEnvironment>();
        Configuration = host.Services.GetRequiredService<IConfiguration>();
        Logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(Environment.ApplicationName);
        _shutdownTimeout = host.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout;
    }

    public static NetWivApplicationBuilder CreateBuilder() => CreateBuilder(new HostApplicationBuilderSettings());

    public static NetWivApplicationBuilder CreateBuilder(string[] args) => CreateBuilder(new HostApplicationBuilderSettings { Args = args });

    /// <summary>Creates a builder. An unset content root defaults to the application directory, not the working directory.</summary>
    public static NetWivApplicationBuilder CreateBuilder(HostApplicationBuilderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new NetWivApplicationBuilder(settings);
    }

    public IServiceProvider Services => _host.Services;
    public IConfiguration Configuration { get; }
    public IHostEnvironment Environment { get; }
    public IHostApplicationLifetime Lifetime { get; }
    public ILogger Logger { get; }
    public App App => _app;
    public AppHandle Handle => _app.Handle;

    /// <summary>
    /// Starts the host, runs the desktop event loop and stops the host. <see cref="IHostApplicationLifetime.StopApplication"/>
    /// exits the event loop. After the loop returns, commands still in flight are cancelled and awaited for up to
    /// <see cref="HostOptions.ShutdownTimeout"/> before hosted services stop. A restart requested through
    /// <see cref="AppHandle.Restart"/> relaunches only after that, once both the app and the service provider have been disposed.
    /// </summary>
    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("A NetWiv application can run only once.");
        try { _host.StartAsync().GetAwaiter().GetResult(); }
        // StopApplication during startup cancels the remaining start; the event loop is then skipped.
        catch (OperationCanceledException) when (Lifetime.ApplicationStopping.IsCancellationRequested) { }
        ProcessStartInfo? restart;
        try
        {
            using (Lifetime.ApplicationStopping.Register(RequestExit))
                restart = Lifetime.ApplicationStopping.IsCancellationRequested ? null : _app.RunEventLoop();
        }
        catch
        {
            // Stop hosted services without replacing the event-loop failure.
            try
            {
                DrainInvocationsAsync(closeAdmission: false).GetAwaiter().GetResult();
                _host.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception stopError) { HostingLog.StopFailed(Logger, stopError); }
            throw;
        }
        // Like a server draining requests, let commands finish before their hosted services and singletons go away.
        // Admission stays open so hosted services can still invoke commands while they stop; Dispose closes it.
        DrainInvocationsAsync(closeAdmission: false).GetAwaiter().GetResult();
        _host.StopAsync().GetAwaiter().GetResult();
        if (restart is not null)
        {
            Dispose();
            using var process = Process.Start(restart) ?? throw new InvokeException("The application could not restart.");
        }
    }

    private void RequestExit()
    {
        try { _app.Handle.Exit(); }
        // The loop has not started yet, or has already stopped. The Setup hook rechecks a pending stop.
        catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Stops accepting commands, cancels those in flight and waits for them for up to
    /// <see cref="HostOptions.ShutdownTimeout"/>, then disposes the app and its plugins, then the service provider.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            DrainInvocationsAsync(closeAdmission: true).GetAwaiter().GetResult();
            _app.Dispose();
        }
        finally { _host.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await DrainInvocationsAsync(closeAdmission: true).ConfigureAwait(false);
            _app.Dispose();
        }
        finally
        {
            if (_host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync().ConfigureAwait(false);
            else _host.Dispose();
        }
    }

    private async Task DrainInvocationsAsync(bool closeAdmission)
    {
        // Closing admission and snapshotting happen atomically, so the wait below covers every admitted command.
        _app.Handle.CancelInvocations(closeAdmission);
        using var timeout = new CancellationTokenSource(_shutdownTimeout);
        // Like the host after its shutdown timeout, continue rather than hang on a command that ignores cancellation.
        if (!await _app.Handle.WaitForInvocationsAsync(timeout.Token).ConfigureAwait(false))
            HostingLog.InvocationsTimedOut(Logger, _shutdownTimeout);
    }

    private static partial class HostingLog
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Hosted services failed to stop after the desktop event loop failed.")]
        internal static partial void StopFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
            Message = "Commands were still running after the shutdown timeout of {Timeout}; continuing shutdown without them.")]
        internal static partial void InvocationsTimedOut(ILogger logger, TimeSpan timeout);
    }
}
