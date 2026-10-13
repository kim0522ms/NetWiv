using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetWiv;

public sealed class GreetingOptions
{
    public string Prefix { get; set; } = "Hello";
}

public sealed record GreetingResult(string Message, string WorkspaceId, bool SharedDependency);
public sealed record EquipmentStatus(string State, string ReceiverId, string WorkspaceId, bool SharedDependency, bool LifecycleInitialized);
public sealed record AuditSnapshot(int WorkspacesCreated, int ReceiversCreated, int ResetCalls, int LifecycleInitialized, int MaxConcurrentStatuses, string[] DisposedWorkspaces, string[] DisposedReceivers);

// The singleton holds observations, never scoped services. It is caller-owned.
public sealed class InvocationAudit
{
    private readonly object _gate = new();
    private int _activeStatuses, _maxConcurrentStatuses;
    public int WorkspacesCreated, ReceiversCreated, ResetCalls, LifecycleInitialized, LifecycleDisposed;
    public bool SmokePassed;
    public ConcurrentQueue<string> DisposedWorkspaces { get; } = new();
    public ConcurrentQueue<string> DisposedReceivers { get; } = new();
    public void EnterStatus()
    {
        lock (_gate) _maxConcurrentStatuses = Math.Max(_maxConcurrentStatuses, ++_activeStatuses);
    }
    public void ExitStatus() { lock (_gate) _activeStatuses--; }
    public AuditSnapshot Snapshot() => new(WorkspacesCreated, ReceiversCreated, ResetCalls, LifecycleInitialized, _maxConcurrentStatuses,
        DisposedWorkspaces.ToArray(), DisposedReceivers.ToArray());
}

public sealed class InvocationWorkspace : IDisposable
{
    private readonly InvocationAudit _audit;
    public string Id { get; } = Guid.NewGuid().ToString();
    public InvocationWorkspace(InvocationAudit audit)
    {
        _audit = audit;
        Interlocked.Increment(ref audit.WorkspacesCreated);
    }
    public void Dispose() => _audit.DisposedWorkspaces.Enqueue(Id);
}

public sealed class GreetingCommands(InvocationWorkspace workspace, IOptions<GreetingOptions> options)
{
    [Command]
    public GreetingResult Greet(string name, [FromServices] InvocationWorkspace methodWorkspace)
        => new($"{options.Value.Prefix}, {name}", workspace.Id, ReferenceEquals(workspace, methodWorkspace));
}

public sealed class EquipmentPlugin : IPlugin, IAsyncDisposable
{
    private readonly InvocationAudit _audit;
    private readonly InvocationWorkspace? _workspace;
    private readonly ILogger<EquipmentPlugin>? _logger;
    private readonly string _receiverId = Guid.NewGuid().ToString();
    private bool _initialized;

    public EquipmentPlugin(InvocationWorkspace workspace, InvocationAudit audit, ILogger<EquipmentPlugin> logger)
    {
        _workspace = workspace;
        _audit = audit;
        _logger = logger;
        Interlocked.Increment(ref audit.ReceiversCreated);
    }

    // This explicit lifecycle object registers handlers; DI creates separate command receivers.
    private EquipmentPlugin(InvocationAudit audit) => _audit = audit;
    public static EquipmentPlugin CreateLifecycle(InvocationAudit audit) => new(audit);
    public string Name => "equipment";

    public void Initialize(PluginContext context)
    {
        _initialized = true;
        Interlocked.Increment(ref _audit.LifecycleInitialized);
        context.RegisterGeneratedCommands<EquipmentPlugin>("get_status", "reset");
        context.Permission(new("allow-status", "get_status"));
        context.Permission(new("allow-reset", "reset"));
        context.Permission(new("deny-reset", [], ["reset"]));
    }

    [Command]
    public async Task<EquipmentStatus> GetStatus([FromServices] InvocationWorkspace methodWorkspace, CancellationToken cancellationToken)
    {
        _audit.EnterStatus();
        try
        {
            // Stand-in for asynchronous application I/O; no hardware SDK compatibility is claimed.
            await Task.Delay(30, cancellationToken);
            _logger!.LogInformation("Equipment status in invocation {WorkspaceId}", _workspace!.Id);
            return new("ready", _receiverId, _workspace.Id, ReferenceEquals(_workspace, methodWorkspace), _initialized);
        }
        finally { _audit.ExitStatus(); }
    }

    [Command]
    public string Reset()
    {
        Interlocked.Increment(ref _audit.ResetCalls);
        return "reset";
    }

    // Exported to demonstrate that generating a binding does not register or authorize a command.
    [Command]
    public string Unselected() => "unselected";

    public void Dispose() => Interlocked.Increment(ref _audit.LifecycleDisposed);
    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        _audit.DisposedReceivers.Enqueue(_receiverId);
    }
}

public static class SmokeCommands
{
    [Command]
    public static AuditSnapshot AuditSnapshot([FromServices] InvocationAudit audit) => audit.Snapshot();

    [Command]
    public static void SmokeDone(string result, [FromServices] InvocationAudit audit, [FromServices] IHostApplicationLifetime lifetime)
    {
        audit.SmokePassed = result == "PASS";
        if (!audit.SmokePassed) Console.Error.WriteLine("FAIL: " + result);
        lifetime.StopApplication();
    }
}
