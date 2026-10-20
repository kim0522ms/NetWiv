using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.JSInterop;
using NetWiv.BlazorHybrid.Aot;
using NetWiv.BlazorHybrid.Aot.Interop;

namespace NetWiv.BlazorHybrid.Aot.Tests.Interop;

internal static class InteropFailureTests
{
    internal static async Task RunAsync()
    {
        await CancellationHandoffAsync();
        await ProductInteropTests.RunAsync();
        var sent = new List<string>();
        using var runtime = new AotJsRuntime(sent.Add, ProofJsonContext.Default, new());
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await ExpectAsync<OperationCanceledException>(() =>
                runtime.InvokeAsync<int>("mustNotRun", canceled.Token, []).AsTask());
            Require(sent.Count == 0, "A canceled invocation sent JavaScript.");
        }

        using (var cancellation = new CancellationTokenSource())
        {
            var pending = runtime.InvokeAsync<int>("cancelPending", cancellation.Token, []).AsTask();
            Require(sent.Count == 1 && !pending.IsCompleted, "The pending invocation was not admitted.");
            cancellation.Cancel();
            await ExpectAsync<TaskCanceledException>(() => pending);
            Require(pending.IsCanceled && sent.Count == 1, "Cancellation did not terminate the pending call locally.");
        }

        var disconnected = runtime.InvokeAsync<int>("disconnectPending", []).AsTask();
        Require(sent.Count == 2 && !disconnected.IsCompleted, "The disconnect scenario did not have a pending call.");
        runtime.Disconnect();
        await ExpectAsync<JSDisconnectedException>(() => disconnected);
        await ExpectAsync<JSDisconnectedException>(() => runtime.InvokeAsync<int>("afterDisconnect", []).AsTask());
        Require(sent.Count == 2, "A disconnected runtime sent JavaScript.");
        runtime.Disconnect();

        using (var failingSender = new AotJsRuntime(_ => throw new InvalidOperationException("Expected transport failure."),
            ProofJsonContext.Default, new()))
        {
            var failed = failingSender.InvokeAsync<int>("sendFailure", []).AsTask();
            var error = await ExpectAsync<InvalidOperationException>(() => failed);
            Require(error.Message == "Expected transport failure.", "The transport failure was replaced or lost.");
            failingSender.Disconnect();
            Require(failed.IsFaulted, "Disconnect changed a previously failed invocation.");
        }

        var replies = new List<string>();
        var registry = new BlazorAotInteropRegistry();
        registry.RegisterStatic<int, int>("proof", "Throws", ThrowAsync,
            ProofJsonContext.Default.Int32, ProofJsonContext.Default.Int32);
        using var receiver = new AotJsRuntime(replies.Add, ProofJsonContext.Default, registry);
        using var invocation = JsonDocument.Parse("[\"BeginInvokeDotNet\",\"error-call\",\"proof\",\"Throws\",0,\"[7]\"]");
        await receiver.ReceiveAsync(invocation.RootElement);
        Require(replies.Count == 1 && replies[0].StartsWith("__bwv:", StringComparison.Ordinal),
            "An asynchronous invokable failure did not produce one protocol reply.");
        using var reply = JsonDocument.Parse(replies[0].AsMemory("__bwv:".Length));
        var values = reply.RootElement;
        Require(values.GetArrayLength() == 4 && values[0].GetString() == "EndInvokeDotNet" &&
            values[1].GetString() == "error-call" && !values[2].GetBoolean() &&
            values[3].GetString() == "Expected asynchronous invokable failure.",
            "An asynchronous invokable exception was not returned as a failed completion.");
    }

    private static async Task CancellationHandoffAsync()
    {
        for (var iteration = 0; iteration < 64; iteration++)
        {
            using var cancellation = new CancellationTokenSource();
            var resolver = new CancelDuringResolution(cancellation, asynchronously: iteration % 2 != 0);
            var sends = 0;
            using var runtime = new AotJsRuntime(_ => Interlocked.Increment(ref sends), resolver, new());
            // Metadata is requested after the initial cancellation check, but
            // before token registration: even iterations force synchronous
            // cancellation during Register's ownership handoff.
            var pending = runtime.InvokeAsync<int>("cancelDuringInvocation", cancellation.Token, [new byte[] { 1 }]).AsTask();
            if (iteration % 2 != 0) runtime.Disconnect();
            await resolver.Cancellation.WaitAsync(TimeSpan.FromSeconds(2));
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(2));
                throw new InvalidOperationException("A cancellation/disconnect race completed successfully.");
            }
            catch (OperationCanceledException) { }
            catch (JSDisconnectedException) when (iteration % 2 != 0) { }
            runtime.Disconnect();
            Require(sends <= 2 && (iteration % 2 != 0 || sends == 0),
                "Cancellation during registration sent an unexpected invocation.");
        }
    }

    private sealed class CancelDuringResolution(CancellationTokenSource cancellation, bool asynchronously) : IJsonTypeInfoResolver
    {
        private bool _triggered;
        internal Task Cancellation { get; private set; } = Task.CompletedTask;

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == typeof(int) && !_triggered)
            {
                _triggered = true;
                if (asynchronously) Cancellation = Task.Run(cancellation.Cancel);
                else cancellation.Cancel();
            }
            return ((IJsonTypeInfoResolver)ProofJsonContext.Default).GetTypeInfo(type, options);
        }
    }

    private static async ValueTask<int> ThrowAsync(int value)
    {
        await Task.Yield();
        throw new InvalidOperationException("Expected asynchronous invokable failure.");
    }

    private static async Task<TException> ExpectAsync<TException>(Func<Task> invoke) where TException : Exception
    {
        try { await invoke(); }
        catch (TException error) { return error; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
