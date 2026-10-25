using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NetWiv.Runtime;

namespace NetWiv;

public sealed partial class AppHandle
{
    /// <summary>Snapshots selected backend capability grants and all backend capability denies for this application.</summary>
    public InvokeAuthorization CreateInvokeAuthorization(params string[] capabilities)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (capabilities.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Capability identifiers must not be blank.", nameof(capabilities));
        return _authorizer.CreateBackendAuthorization(this, capabilities);
    }

    /// <summary>Invokes a registered managed command under an application-issued backend capability grant.</summary>
    public async ValueTask<JsonElement> InvokeAsync(InvokeAuthorization authorization, string command, JsonElement args, CancellationToken cancellationToken = default)
    {
        var response = await InvokeBackendCoreAsync(authorization, command, args, cancellationToken, jsonOnly: true);
        return response.Json;
    }

    /// <summary>Invokes a registered managed command, preserving a binary or JSON response.</summary>
    public ValueTask<RuntimeInvokeResponse> InvokeResponseAsync(InvokeAuthorization authorization, string command, JsonElement args, CancellationToken cancellationToken = default) =>
        InvokeBackendCoreAsync(authorization, command, args, cancellationToken, jsonOnly: false);

    private async ValueTask<RuntimeInvokeResponse> InvokeBackendCoreAsync(InvokeAuthorization authorization, string command, JsonElement args, CancellationToken cancellationToken, bool jsonOnly)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(authorization);
        if (!ReferenceEquals(authorization.App, this)) throw new ArgumentException("The authorization belongs to another application.", nameof(authorization));
        var grant = authorization.Authorize(command);
        if (!grant.Allowed) throw new InvokeException($"Command {command} not allowed by backend ACL");
        if (!_commands.TryGet(command, out var handler)) throw new InvokeException($"Command {command} is not a registered managed command.");

        var lifetime = BeginInvocation("", 0, true, cancellationToken);
        InvokeContext? context = null;
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var scope = _scopeFactory?.CreateAsyncScope();
            try
            {
                context = new(this, "", "", new Uri("netwiv://backend"), false, grant.Scope,
                    request: new Request(InvokeBody.FromJson(args)), services: scope?.ServiceProvider ?? _services)
                { Authorization = authorization, InvocationResources = [] };
                var result = await handler!(context, args, lifetime.Token);
                if (jsonOnly && result.IsBinary) throw new InvokeException("The command returned binary data; use InvokeResponseAsync.");
                return result;
            }
            finally
            {
                if (scope is { } active) await active.DisposeAsync();
            }
        }
        catch (Exception failure)
        {
            if (context is not null && CompleteBackendInvocation(context, failed: true) is { } cleanupErrors)
                throw new AggregateException("The command failed and resource cleanup also failed.", [failure, .. cleanupErrors]);
            throw;
        }
        finally
        {
            if (context is not null) CompleteBackendInvocation(context, failed: false);
            EndInvocation(lifetime);
        }
    }

    private List<Exception>? CompleteBackendInvocation(InvokeContext context, bool failed)
    {
        uint[] resources;
        lock (_invocationGate)
        {
            if (context.InvocationCompleted) return null;
            context.InvocationCompleted = true;
            resources = failed ? context.InvocationResources!.ToArray() : [];
        }
        List<Exception>? errors = null;
        foreach (var resource in resources)
        {
            try { Resources.Close(resource, context.ResourceOwner, 0); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
        return errors;
    }

    private static void RequireGuestOrTrustedBackend(InvokeContext context)
    {
        if (context.Authorization is not null)
            throw new InvokeException("Scoped backend invocations cannot forward native commands without a WebView identity.");
    }
}
