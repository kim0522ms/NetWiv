using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;

internal static class BackendAuthorizationTests
{
    internal static async Task SelectedCapabilitiesAndApplicationDenies()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .RegisterCommand("first", (_, _, _) => ValueTask.FromResult(JsonValue.From("first")))
            .RegisterCommand("second", (_, _, _) => ValueTask.FromResult(JsonValue.From("second")))
            .AppCommandManifest("first", "second")
            .Capability(new Capability("first", ["allow-first"]) { Backend = true })
            .Capability(new Capability("second", ["allow-second"]) { Backend = true })
            .Capability(new Capability("frontend", ["allow-second"], Windows: ["main"]))
            .Build();

        var selectedIds = new[] { "first" };
        var selected = app.Handle.CreateInvokeAuthorization(selectedIds);
        selectedIds[0] = "second";
        Assert.Equal("first", selected.Capabilities.Single());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)selected.Capabilities)[0] = "second");
        Assert.Equal("first", (await app.Handle.InvokeAsync(selected, "first", JsonValue.EmptyObject)).GetString());
        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(selected, "second", JsonValue.EmptyObject).AsTask());

        var noGrant = app.Handle.CreateInvokeAuthorization();
        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(noGrant, "first", JsonValue.EmptyObject).AsTask());

        Assert.Throws<ArgumentException>(() => app.Handle.CreateInvokeAuthorization("unknown"));
        Assert.Throws<ArgumentException>(() => app.Handle.CreateInvokeAuthorization(""));
        Assert.Throws<ArgumentException>(() => app.Handle.CreateInvokeAuthorization("frontend"));

        using var deniedApp = new Builder().UseRuntime(new FakeRuntime())
            .RegisterCommand("first", (_, _, _) => ValueTask.FromResult(JsonValue.From("first")))
            .AppCommandManifest("first")
            .Capability(new Capability("first", ["allow-first"]) { Backend = true })
            .Capability(new Capability("deny", ["deny-first"]) { Backend = true })
            .Build();
        var denied = deniedApp.Handle.CreateInvokeAuthorization("first");
        await CaptureAsync<InvokeException>(() => deniedApp.Handle.InvokeAsync(denied, "first", JsonValue.EmptyObject).AsTask());

        using var noManifest = new Builder().UseRuntime(new FakeRuntime())
            .RegisterCommand("unlisted", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
            .Capability(new Capability("backend", ["core:resources:default"]) { Backend = true })
            .Build();
        var restricted = noManifest.Handle.CreateInvokeAuthorization("backend");
        await CaptureAsync<InvokeException>(() => noManifest.Handle.InvokeAsync(restricted, "unlisted", JsonValue.EmptyObject).AsTask());

        Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(new FakeRuntime())
            .Capability(new Capability("invalid", ["allow-first"], Windows: ["main"]) { Backend = true }).Build());

        var inline = EmbeddedConfiguration.ReadCapability(JsonValue.Parse(
            "{\"identifier\":\"inline-backend\",\"permissions\":[\"core:resources:default\"],\"backend\":true}"));
        Assert.True(inline.Backend, "Inline JSON capability parsing ignored backend: true.");
        Assert.Throws<FormatException>(() => EmbeddedConfiguration.ReadCapability(JsonValue.Parse(
            "{\"identifier\":\"invalid-backend\",\"permissions\":[\"core:resources:default\"],\"backend\":\"true\"}")));

        using var other = new Builder().UseRuntime(new FakeRuntime())
            .RegisterCommand("first", (_, _, _) => ValueTask.FromResult(JsonValue.From("other")))
            .AppCommandManifest("first")
            .Capability(new Capability("first", ["allow-first"]) { Backend = true })
            .Build();
        await CaptureAsync<ArgumentException>(() => other.Handle.InvokeAsync(selected, "first", JsonValue.EmptyObject).AsTask());
    }

    internal static async Task BackendContextAndNativeForwardingGuard()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .Capability(new Capability("backend", ["backend-probe:allow-inspect"]) { Backend = true })
            .Build();
        app.Run();
        var authorization = app.Handle.CreateInvokeAuthorization("backend");

        var details = await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|inspect", JsonValue.EmptyObject);
        Assert.Equal("false|true|||netwiv://backend/", details.GetString());
        var generated = await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|generated_inspect", JsonValue.EmptyObject);
        Assert.Equal(details.GetString(), generated.GetString());

        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(authorization, "plugin:backend-probe|platform", JsonValue.EmptyObject).AsTask());
        Assert.True(runtime.LastCommand is null, "A scoped invocation forwarded a native command.");

        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(authorization, "plugin:backend-probe|channel", JsonValue.Parse("{\"value\":\"__CHANNEL__:47\"}")).AsTask());
        Assert.True(runtime.LastCommand is null, "A scoped invocation forwarded a native command.");
        Assert.Equal(0, runtime.PendingScripts);

        var guest = await GuestIpc.Invoke(runtime, "plugin:backend-probe|inspect", "{}");
        Assert.Equal("error", guest.Headers!["Tauri-Response"]);
        Assert.Contains("not allowed", GuestIpc.Body(guest));
    }

    internal static async Task PermissionAndCapabilityScopesCombine()
    {
        var capabilityAllow = JsonValue.Parse("{\"path\":\"/capability\"}");
        var capabilityDeny = JsonValue.Parse("{\"path\":\"/capability-denied\"}");
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .Capability(new Capability("backend", ["backend-probe:allow-inspect", "backend-probe:scope"],
                PermissionScopes: [
                    new PermissionScopeExtension("backend-probe:allow-inspect", [capabilityAllow], [capabilityDeny]),
                    new PermissionScopeExtension("backend-probe:scope", [JsonValue.Parse("{\"path\":\"/scope-capability\"}")], [JsonValue.Parse("{\"path\":\"/scope-capability-denied\"}")])
                ]) { Backend = true })
            .Build();
        var authorization = app.Handle.CreateInvokeAuthorization("backend");

        var scopes = await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|scopes", JsonValue.EmptyObject);
        Assert.Contains("/permission", scopes.GetString()!);
        Assert.Contains("/capability", scopes.GetString()!);
        Assert.Contains("/scope-permission", scopes.GetString()!);
        Assert.Contains("/scope-capability", scopes.GetString()!);
        Assert.Contains("/permission-denied", scopes.GetString()!);
        Assert.Contains("/capability-denied", scopes.GetString()!);
        Assert.Contains("/scope-denied", scopes.GetString()!);
        Assert.Contains("/scope-capability-denied", scopes.GetString()!);
    }

    internal static async Task UnselectedBackendScopesContributeOnlyDenies()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .Capability(new Capability("selected", ["backend-probe:allow-inspect"], PermissionScopes: [
                new PermissionScopeExtension("backend-probe:allow-inspect", [JsonValue.Parse("{\"path\":\"/selected-safe\"}")])
            ]) { Backend = true })
            .Capability(new Capability("unselected", ["backend-probe:scope"], PermissionScopes: [
                new PermissionScopeExtension("backend-probe:scope", [JsonValue.Parse("{\"path\":\"/unselected-allow\"}")],
                    [JsonValue.Parse("{\"path\":\"/unselected-deny\"}"), JsonValue.Parse("{\"path\":\"/permission\"}")])
            ]) { Backend = true })
            .Build();
        var authorization = app.Handle.CreateInvokeAuthorization("selected");

        var scopes = (await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|scopes", JsonValue.EmptyObject)).GetString()!;
        Assert.Contains("/scope-denied", scopes);
        Assert.Contains("/unselected-deny", scopes);
        Assert.DoesNotContain("/scope-permission", scopes);
        Assert.DoesNotContain("/unselected-allow", scopes);
        Assert.True((await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|scope_access", JsonValue.Parse("{\"path\":\"/selected-safe\"}"))).GetBoolean(),
            "A selected allow scope could not authorize a matching target.");
        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(authorization, "plugin:backend-probe|scope_access", JsonValue.Parse("{\"path\":\"/permission\"}")).AsTask());
        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(authorization, "plugin:backend-probe|scope_access", JsonValue.Parse("{\"path\":\"/unselected-allow\"}")).AsTask());
    }

    internal static async Task AuthorizationTokensSnapshotMutableAclInputs()
    {
        var allowedCommands = new List<string> { "plugin:backend-probe|scopes", "plugin:backend-probe|mutate_scope", "plugin:backend-probe|deny_me" };
        var deniedCommands = new List<string> { "plugin:backend-probe|deny_me" };
        var permissionAllow = new List<JsonElement> { JsonValue.Parse("{\"path\":\"/source-allow\"}") };
        var permissionDeny = new List<JsonElement> { JsonValue.Parse("{\"path\":\"/source-deny\"}") };
        var selectedPermissions = new List<string> { "snapshot-permission" };
        var denyPermissions = new List<string> { "snapshot-deny" };
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .Permission(new PermissionDefinition("snapshot-permission", allowedCommands, [], permissionAllow, permissionDeny))
            .Permission(new PermissionDefinition("snapshot-deny", [], deniedCommands))
            .Capability(new Capability("snapshot", selectedPermissions) { Backend = true })
            .Capability(new Capability("global-deny", denyPermissions) { Backend = true })
            .Build();
        var authorization = app.Handle.CreateInvokeAuthorization("snapshot");

        var before = (await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|scopes", JsonValue.EmptyObject)).GetString()!;
        Assert.Contains("/source-allow", before);
        Assert.Contains("/source-deny", before);

        allowedCommands.Clear();
        deniedCommands.Clear();
        permissionAllow[0] = JsonValue.Parse("{\"path\":\"/mutated-allow\"}");
        permissionDeny[0] = JsonValue.Parse("{\"path\":\"/mutated-deny\"}");
        selectedPermissions[0] = "missing-after-snapshot";
        denyPermissions.Clear();

        var afterInputMutation = (await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|scopes", JsonValue.EmptyObject)).GetString()!;
        Assert.Contains("/source-allow", afterInputMutation);
        Assert.Contains("/source-deny", afterInputMutation);
        Assert.DoesNotContain("/mutated-allow", afterInputMutation);
        Assert.DoesNotContain("/mutated-deny", afterInputMutation);
        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(authorization, "plugin:backend-probe|deny_me", JsonValue.EmptyObject).AsTask());

        await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|mutate_scope", JsonValue.EmptyObject);
        var afterHandlerMutation = (await app.Handle.InvokeAsync(authorization, "plugin:backend-probe|scopes", JsonValue.EmptyObject)).GetString()!;
        Assert.Contains("/source-allow", afterHandlerMutation);
        Assert.Contains("/source-deny", afterHandlerMutation);
        Assert.DoesNotContain("/handler-mutated-allow", afterHandlerMutation);
        Assert.DoesNotContain("/handler-mutated-deny", afterHandlerMutation);
    }

    internal static async Task ScopedCleanupErrorsAndBinaryResponses()
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .Capability(new Capability("backend", ["backend-probe:allow-cleanup", "core:resources:default"]) { Backend = true })
            .Build();
        var authorization = app.Handle.CreateInvokeAuthorization("backend");

        var aggregate = await CaptureAsync<AggregateException>(() =>
            app.Handle.InvokeAsync(authorization, "plugin:backend-probe|fail-cleanup", JsonValue.EmptyObject).AsTask());
        Assert.True(ReferenceEquals(BackendAuthorizationPlugin.PrimaryFailure, aggregate.InnerExceptions[0]), "Cleanup replaced the command's original failure.");
        Assert.Equal(1, BackendAuthorizationPlugin.ThrowingCleanup!.DisposeCount);
        Assert.True(BackendAuthorizationPlugin.CleanupPeer!.Disposed, "Cleanup stopped after one resource disposal failed.");

        await CaptureAsync<InvokeException>(() =>
            app.Handle.InvokeAsync(authorization, "plugin:backend-probe|binary", JsonValue.EmptyObject).AsTask());
        var failedBinary = BackendAuthorizationPlugin.LastBinaryResource!;
        Assert.True(failedBinary.Disposed, "JSON-only invocation retained a resource after rejecting binary data.");

        var response = await app.Handle.InvokeResponseAsync(authorization, "plugin:backend-probe|binary", JsonValue.EmptyObject);
        Assert.True(response.IsBinary, "Response invocation did not preserve binary data.");
        Assert.True(response.Binary.Span.SequenceEqual(new byte[] { 1, 2, 3 }), "Response invocation changed the binary payload.");
        var retainedBinary = BackendAuthorizationPlugin.LastBinaryResource!;
        var retainedId = BackendAuthorizationPlugin.LastBinaryResourceId;
        Assert.True(!retainedBinary.Disposed, "Successful response invocation disposed its registered resource.");
        await app.Handle.InvokeAsync(authorization, "plugin:resources|close", JsonValue.Parse($"{{\"rid\":{retainedId}}}"));
        Assert.True(retainedBinary.Disposed, "A resource retained after a successful binary response could not be closed by its token.");
    }

    internal static async Task ScopedInvocationCancellationAndShutdown()
    {
        var runtime = new FakeRuntime();
        var app = new Builder().UseRuntime(runtime)
            .Capability(new Capability("backend", ["backend-probe:allow-wait"]) { Backend = true })
            .Build();
        var authorization = app.Handle.CreateInvokeAuthorization("backend");
        using var caller = new CancellationTokenSource();
        var callerInvocation = app.Handle.InvokeAsync(authorization, "plugin:backend-probe|wait", JsonValue.EmptyObject, caller.Token).AsTask();
        await BackendAuthorizationPlugin.WaitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        caller.Cancel();
        await CaptureAsync<OperationCanceledException>(() => callerInvocation);
        Assert.True(BackendAuthorizationPlugin.WaitToken.IsCancellationRequested, "Caller cancellation did not reach the scoped command token.");

        BackendAuthorizationPlugin.ResetWait();
        var shutdownInvocation = app.Handle.InvokeAsync(authorization, "plugin:backend-probe|wait", JsonValue.EmptyObject).AsTask();
        await BackendAuthorizationPlugin.WaitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        app.Dispose();
        await CaptureAsync<OperationCanceledException>(() => shutdownInvocation);
        Assert.True(BackendAuthorizationPlugin.LastContext!.InvocationCompleted, "Shutdown did not complete the scoped invocation context.");

        var late = new TrackedBackendResource();
        Assert.Throws<InvokeException>(() => BackendAuthorizationPlugin.LastContext.AddResource(late));
        Assert.True(late.Disposed, "Late resource admission after shutdown did not dispose the resource.");
    }

    internal static async Task ScopedResourcesRespectTokenAndFailureLifetime()
    {
        var runtime = new FakeRuntime();
        var app = new Builder().UseRuntime(runtime)
            .Capability(new Capability("backend", ["backend-probe:allow-inspect", "core:resources:default"]) { Backend = true })
            .Capability(new Capability("frontend", ["backend-probe:allow-inspect", "core:resources:default"], Windows: ["main"]))
            .Build();
        app.Run();
        var first = app.Handle.CreateInvokeAuthorization("backend");
        var second = app.Handle.CreateInvokeAuthorization("backend");

        var guestResourceResult = await GuestIpc.Invoke(runtime, "plugin:backend-probe|resource", "{}");
        Assert.Equal("ok", guestResourceResult.Headers!["Tauri-Response"]);
        var guestResourceId = uint.Parse(JsonValue.Parse(GuestIpc.Body(guestResourceResult)).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var guestResource = BackendAuthorizationPlugin.LastResource!;
        Assert.True(ReferenceEquals(guestResource, app.Handle.Resources.TryGet(guestResourceId)), "Guest resource creation did not register its resource.");
        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(first, "plugin:resources|close", JsonValue.Parse($"{{\"rid\":{guestResourceId}}}")).AsTask());

        var id = uint.Parse((await app.Handle.InvokeAsync(first, "plugin:backend-probe|resource", JsonValue.EmptyObject)).GetString()!);
        var owned = (TrackedBackendResource)app.Handle.Resources.TryGet(id)!;
        await CaptureAsync<InvokeException>(() => app.Handle.InvokeAsync(second, "plugin:resources|close", JsonValue.Parse($"{{\"rid\":{id}}}")).AsTask());
        var guestClose = await GuestIpc.Invoke(runtime, "plugin:resources|close", $"{{\"rid\":{id}}}");
        Assert.Equal("error", guestClose.Headers!["Tauri-Response"]);
        Assert.Contains("another document", GuestIpc.Body(guestClose));
        Assert.True(!owned.Disposed, "A different token or authorized guest close bypassed scoped resource ownership.");

        await app.Handle.InvokeAsync(first, "plugin:resources|close", JsonValue.Parse($"{{\"rid\":{id}}}"));
        Assert.True(owned.Disposed, "The resource owner could not close its resource.");

        await CaptureAsync<InvalidOperationException>(() => app.Handle.InvokeAsync(first, "plugin:backend-probe|fail-resource", JsonValue.EmptyObject).AsTask());
        var failed = BackendAuthorizationPlugin.NextResource;
        Assert.True(failed!.Disposed, "A failed backend invocation did not dispose its newly registered resource.");

        var lateContext = BackendAuthorizationPlugin.LastContext!;
        var late = new TrackedBackendResource();
        Assert.Throws<InvokeException>(() => lateContext.AddResource(late));
        Assert.True(late.Disposed, "A resource registered after invocation completion was not disposed.");

        var sharedId = uint.Parse((await app.Handle.InvokeAsync("plugin:backend-probe|shared-resource", JsonValue.EmptyObject)).GetString()!);
        var shared = (TrackedBackendResource)app.Handle.Resources.TryGet(sharedId)!;
        await app.Handle.InvokeAsync(first, "plugin:resources|close", JsonValue.Parse($"{{\"rid\":{sharedId}}}"));
        Assert.True(shared.Disposed, "An application-shared resource could not be closed by an authorized backend invocation.");

        var appOwnedId = uint.Parse((await app.Handle.InvokeAsync(first, "plugin:backend-probe|resource", JsonValue.EmptyObject)).GetString()!);
        var appOwned = (TrackedBackendResource)app.Handle.Resources.TryGet(appOwnedId)!;
        app.Dispose();
        Assert.True(appOwned.Disposed, "Application disposal did not dispose a scoped backend resource.");
    }

    private static async Task<T> CaptureAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

}

[Plugin]
internal sealed class BackendAuthorizationPlugin : IPlugin
{
    internal static InvokeContext? LastContext { get; private set; }
    internal static TrackedBackendResource? NextResource { get; private set; }
    internal static TrackedBackendResource? LastResource { get; private set; }
    internal static TrackedBackendResource? LastBinaryResource { get; private set; }
    internal static uint LastBinaryResourceId { get; private set; }
    internal static ThrowingBackendResource? ThrowingCleanup { get; private set; }
    internal static TrackedBackendResource? CleanupPeer { get; private set; }
    internal static InvalidOperationException? PrimaryFailure { get; private set; }
    private static TaskCompletionSource _waitEntered = NewSignal();
    internal static TaskCompletionSource WaitEntered => _waitEntered;
    internal static CancellationToken WaitToken { get; private set; }

    public string Name => "backend-probe";

    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<BackendAuthorizationPlugin>("generated_inspect");
        context.RegisterCommand("inspect", (invoke, _, _) =>
        {
            LastContext = invoke;
            var details = $"{invoke.IsTrusted.ToString().ToLowerInvariant()}|{invoke.IsBackend.ToString().ToLowerInvariant()}|{invoke.WindowLabel}|{invoke.WebviewLabel}|{invoke.Source}";
            return ValueTask.FromResult(JsonValue.From(details));
        });
        context.RegisterCommand("channel", (invoke, arguments, _) =>
        {
            Channel.FromJson(invoke, arguments.GetProperty("value")).Send(JsonValue.EmptyObject);
            return ValueTask.FromResult(JsonValue.Null);
        });
        context.RegisterCommand("platform", async (invoke, _, token) =>
        {
            await invoke.InvokePlatformAsync("plugin:backend-probe|inspect", JsonValue.EmptyObject, token);
            return JsonValue.Null;
        });
        context.RegisterCommand("resource", (invoke, _, _) =>
        {
            var resource = new TrackedBackendResource();
            LastResource = resource;
            return ValueTask.FromResult(JsonValue.From(invoke.AddResource(resource).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        });
        context.RegisterCommand("fail-resource", (invoke, _, _) =>
        {
            NextResource = new TrackedBackendResource();
            LastContext = invoke;
            _ = invoke.AddResource(NextResource);
            return ValueTask.FromException<JsonElement>(new InvalidOperationException("expected command failure"));
        });
        context.RegisterCommand("shared-resource", (invoke, _, _) =>
        {
            var resource = new TrackedBackendResource();
            return ValueTask.FromResult(JsonValue.From(invoke.App.Resources.Add(resource).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        });
        context.RegisterCommand("scopes", (invoke, _, _) => ValueTask.FromResult(JsonValue.From(
            string.Join('|', invoke.Scope.Allow.Select(item => item.GetRawText())) + "|" +
            string.Join('|', invoke.Scope.Deny.Select(item => item.GetRawText())))));
        context.RegisterCommand("scope_access", (invoke, arguments, _) =>
        {
            var path = arguments.GetProperty("path").GetString();
            if (invoke.Scope.Deny.Any(item => item.GetProperty("path").GetString() == path) ||
                !invoke.Scope.Allow.Any(item => item.GetProperty("path").GetString() == path))
                throw new InvokeException("The synthetic target is outside the command scope.");
            return ValueTask.FromResult(JsonValue.From(true));
        });
        context.RegisterCommand("mutate_scope", (invoke, _, _) =>
        {
            ((JsonElement[])invoke.Scope.Allow)[0] = JsonValue.Parse("{\"path\":\"/handler-mutated-allow\"}");
            ((JsonElement[])invoke.Scope.Deny)[0] = JsonValue.Parse("{\"path\":\"/handler-mutated-deny\"}");
            return ValueTask.FromResult(JsonValue.Null);
        });
        context.RegisterCommand("deny_me", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)));
        context.RegisterCommand("fail-cleanup", (invoke, _, _) =>
        {
            ThrowingCleanup = new ThrowingBackendResource();
            CleanupPeer = new TrackedBackendResource();
            _ = invoke.AddResource(ThrowingCleanup);
            _ = invoke.AddResource(CleanupPeer);
            PrimaryFailure = new InvalidOperationException("primary command failure");
            return ValueTask.FromException<JsonElement>(PrimaryFailure);
        });
        context.RegisterResponseCommand("binary", (invoke, _, _) =>
        {
            LastBinaryResource = new TrackedBackendResource();
            LastBinaryResourceId = invoke.AddResource(LastBinaryResource);
            return ValueTask.FromResult(RuntimeInvokeResponse.FromBinary(new byte[] { 1, 2, 3 }));
        });
        context.RegisterCommand("wait", async (invoke, _, token) =>
        {
            LastContext = invoke;
            WaitToken = token;
            WaitEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonValue.Null;
        });
        context.Permission(new PermissionDefinition("allow-inspect", ["inspect", "generated_inspect", "channel", "platform", "resource", "fail-resource", "scopes", "scope_access"], [],
            [JsonValue.Parse("{\"path\":\"/permission\"}")], [JsonValue.Parse("{\"path\":\"/permission-denied\"}")]));
        context.Permission(new PermissionDefinition("scope", [], [],
            [JsonValue.Parse("{\"path\":\"/scope-permission\"}")], [JsonValue.Parse("{\"path\":\"/scope-denied\"}")]));
        context.Permission(new PermissionDefinition("allow-shared-resource", ["shared-resource"]));
        context.Permission(new PermissionDefinition("allow-cleanup", ["fail-cleanup", "binary"]));
        context.Permission(new PermissionDefinition("allow-snapshot", ["scopes", "mutate_scope"]));
        context.Permission(new PermissionDefinition("allow-wait", ["wait"]));
    }

    [Command("generated_inspect")]
    public static string GeneratedInspect(InvokeContext context, Request request) =>
        $"{context.IsTrusted.ToString().ToLowerInvariant()}|{context.IsBackend.ToString().ToLowerInvariant()}|{context.WindowLabel}|{context.WebviewLabel}|{context.Source}";

    internal static void ResetWait()
    {
        _waitEntered = NewSignal();
        WaitToken = default;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class TrackedBackendResource : IDisposable
{
    internal bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

internal sealed class ThrowingBackendResource : IDisposable
{
    internal int DisposeCount { get; private set; }
    public void Dispose()
    {
        DisposeCount++;
        throw new InvalidOperationException("cleanup failure");
    }
}
