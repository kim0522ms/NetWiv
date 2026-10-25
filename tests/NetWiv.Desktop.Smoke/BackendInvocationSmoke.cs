using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Platform.Desktop;

[assembly: CommandJsonContext(typeof(BackendInvocationSmokeJson))]

[JsonSerializable(typeof(string))]
internal partial class BackendInvocationSmokeJson : JsonSerializerContext;

internal static class BackendInvocationSmoke
{
    internal static int Run()
    {
        Exception? failure = null;
        var timedOut = 0;
        var probeCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new DesktopRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .Metadata(new("Backend invocation smoke", "1.0", "dev.netwiv.backend-invocation-smoke"))
            .Capability(new Capability("backend-invocation-smoke", ["backend-invocation-smoke:allow", "core:resources:default"]) { Backend = true })
            .Capability(new Capability("backend-deny", ["backend-invocation-smoke:deny"]) { Backend = true })
            .Window("main", "Backend invocation smoke", "about:blank", 480, 320)
            .Setup(context =>
            {
                Program.VerifyBackend(runtime);
                _ = Probe(context.Handle);
            })
            .Build();
        using var timeout = new Timer(_ =>
        {
            Interlocked.Exchange(ref timedOut, 1);
            app.Handle.Exit(124);
        }, null, TimeSpan.FromSeconds(25), Timeout.InfiniteTimeSpan);
        app.Run();
        timeout.Dispose();
        probeCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        app.Dispose();
        if (Volatile.Read(ref timedOut) != 0) throw new TimeoutException("Backend invocation smoke did not finish within 25 seconds.");
        if (failure is not null) throw failure;
        if (BackendInvocationSmokePlugin.Resource is { Disposed: false })
            throw new InvalidOperationException("Application shutdown did not dispose the backend resource.");
        Console.WriteLine("PASS native backend invocation authorization, command context, and resource ownership");
        return 0;

        async Task Probe(AppHandle handle)
        {
            try
            {
                var authorization = handle.CreateInvokeAuthorization("backend-invocation-smoke");
                var context = await handle.InvokeAsync(authorization, "plugin:backend-invocation-smoke|inspect", JsonValue.EmptyObject);
                Require(context.GetString() == "true|false|||netwiv://backend/", "Scoped command context did not identify a backend caller without guest labels.");
                var trustedContext = await handle.InvokeAsync("plugin:backend-invocation-smoke|inspect", JsonValue.EmptyObject);
                Require(trustedContext.GetString() == "true|true|native|native|netwiv://native/", "The trusted overload lost its trusted native context.");

                await ExpectDenied(handle.InvokeAsync(authorization, "plugin:backend-invocation-smoke|denied", JsonValue.EmptyObject).AsTask());
                await ExpectDenied(handle.InvokeAsync(authorization, "plugin:backend-invocation-smoke|unselected", JsonValue.EmptyObject).AsTask());
                await ExpectDenied(handle.InvokeAsync("plugin:backend-invocation-smoke|denied", JsonValue.EmptyObject).AsTask(), allowed: true);

                await ExpectDenied(handle.InvokeAsync(authorization, "plugin:backend-invocation-smoke|forward", JsonValue.EmptyObject).AsTask());
                await ExpectDenied(handle.InvokeAsync(authorization, "plugin:backend-invocation-smoke|channel", JsonValue.Parse("{\"channel\":\"__CHANNEL__:47\"}")).AsTask());

                var resourceId = uint.Parse((await handle.InvokeAsync(authorization, "plugin:backend-invocation-smoke|own_resource", JsonValue.EmptyObject)).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var resource = BackendInvocationSmokePlugin.Resource!;
                Require(ReferenceEquals(resource, handle.Resources.TryGet(resourceId)), "Scoped backend resource was not registered in the app resource table.");
                var otherAuthorization = handle.CreateInvokeAuthorization("backend-invocation-smoke");
                await ExpectDenied(handle.InvokeAsync(otherAuthorization, "plugin:resources|close", JsonValue.Parse($"{{\"rid\":{resourceId}}}")).AsTask());
                Require(!resource.Disposed, "A different authorization token closed a scoped backend resource.");
                await handle.InvokeAsync(authorization, "plugin:resources|close", JsonValue.Parse($"{{\"rid\":{resourceId}}}"));
                Require(resource.Disposed, "The owning authorization token could not close its resource.");

                var appResourceId = uint.Parse((await handle.InvokeAsync(authorization, "plugin:backend-invocation-smoke|own_resource", JsonValue.EmptyObject)).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var appResource = BackendInvocationSmokePlugin.Resource!;
                Require(handle.Resources.TryGet(appResourceId) is not null, "Backend resource was not retained for the application lifetime.");
                Require(!appResource.Disposed, "Application-owned backend resource was disposed before application shutdown.");
                handle.Exit(0);
            }
            catch (Exception error)
            {
                failure = error;
                handle.Exit(1);
            }
            finally { probeCompleted.TrySetResult(); }
        }
    }

    private static async Task ExpectDenied(Task<JsonElement> invocation, bool allowed = false)
    {
        try
        {
            var result = await invocation;
            if (!allowed) throw new InvalidOperationException($"Expected invocation denial, received {result.GetRawText()}.");
        }
        catch (InvokeException) when (!allowed) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

[Plugin]
internal sealed class BackendInvocationSmokePlugin : IPlugin
{
    internal static BackendInvocationSmokeResource? Resource { get; private set; }
    public string Name => "backend-invocation-smoke";

    public void Initialize(PluginContext context)
    {
        context.RegisterGeneratedCommands<BackendInvocationSmokePlugin>("inspect", "denied", "unselected", "forward", "channel", "own_resource");
        context.Permission(new PermissionDefinition("allow", ["inspect", "denied", "forward", "channel", "own_resource"]));
        context.Permission(new PermissionDefinition("deny", [], ["denied"]));
    }

    [Command]
    public static string Inspect(InvokeContext context) =>
        $"{context.IsBackend.ToString().ToLowerInvariant()}|{context.IsTrusted.ToString().ToLowerInvariant()}|{context.WindowLabel}|{context.WebviewLabel}|{context.Source}";

    [Command]
    public static string Denied() => "A backend deny must override this allow.";

    [Command]
    public static string Unselected() => "This command is not granted by the selected capability.";

    [Command]
    public static async ValueTask<JsonElement> Forward(InvokeContext context)
    {
        _ = await context.InvokePlatformAsync("plugin:app|name", JsonValue.EmptyObject);
        return JsonValue.Null;
    }

    [Command]
    public static void Channel(Channel<string> channel) => channel.Send("backend channel");

    [Command]
    public static string OwnResource(InvokeContext context)
    {
        Resource = new BackendInvocationSmokeResource();
        return context.AddResource(Resource).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

internal sealed class BackendInvocationSmokeResource : IDisposable
{
    internal bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}
