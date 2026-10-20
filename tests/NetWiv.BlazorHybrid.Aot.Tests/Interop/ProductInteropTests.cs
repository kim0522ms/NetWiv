using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using NetWiv.BlazorHybrid.Aot.Interop;

namespace NetWiv.BlazorHybrid.Aot.Tests.Interop;

internal static class ProductInteropTests
{
    internal static async Task RunAsync()
    {
        RegistryValidation();
        await VoidAndModuleAsync();
        await ReferenceLifetimeAsync();
        await ByteInterleavingAsync();
        await ByteAdmissionAsync();
        await InvalidCompletionAsync();
    }

    private static void RegistryValidation()
    {
        var registry = new BlazorAotInteropRegistry();
        Expect<ArgumentNullException>(() => registry.RegisterStatic<int>("app", "result", null!, ProductInteropJsonContext.Default.Int32));
        Expect<ArgumentNullException>(() => registry.RegisterStatic<int>("app", "result", () => ValueTask.FromResult(1), null!));
        Require(registry.StaticHandlers.Count == 0 && registry.Resolvers.Count == 0, "Invalid callback registration mutated the registry.");
        Expect<ArgumentNullException>(() => registry.RegisterInstance<Receiver, int>("result", _ => ValueTask.FromResult(1), null!));
        Expect<ArgumentException>(() => registry.RegisterInstanceVoid<Receiver>("__Dispose", _ => ValueTask.CompletedTask));
        Require(registry.InstanceHandlers.Count == 0 && registry.ReferenceConverters.Count == 0, "Invalid instance registration left a reference converter.");
        registry.RegisterStaticVoid("app", "ok", () => ValueTask.CompletedTask);
        var snapshot = registry.Snapshot();
        Expect<InvalidOperationException>(() => snapshot.RegisterStaticVoid("app", "later", () => ValueTask.CompletedTask));
        registry.RegisterStaticVoid("app", "later", () => ValueTask.CompletedTask);
        Require(snapshot.StaticHandlers.Count == 1, "A published callback registry changed with its source.");
    }

    private static async Task VoidAndModuleAsync()
    {
        var sent = new List<string>();
        using var runtime = new AotJsRuntime(sent.Add, null, new());
        IJSRuntime publicRuntime = runtime;
        var ordinary = publicRuntime.InvokeVoidAsync("voidFunction").AsTask();
        var call = Last(sent);
        Require(call[4].GetInt32() == 3, "Standard InvokeVoidAsync did not request a void result.");
        await CompleteAsync(runtime, call[1].GetInt64(), true, "null");
        await ordinary;
        var import = runtime.InvokeAsync<IJSObjectReference>("import", ["./module.js"]).AsTask();
        call = Last(sent);
        await CompleteAsync(runtime, call[1].GetInt64(), true, "{\"__jsObjectId\":71}");
        var module = await import;
        var moduleCall = module.InvokeVoidAsync("moduleVoid").AsTask();
        call = Last(sent);
        Require(call[4].GetInt32() == 3 && call[5].GetInt64() == 71, "A standard module void call lost its target or result type.");
        await CompleteAsync(runtime, call[1].GetInt64(), true, "null");
        await moduleCall;
        var disposal = module.DisposeAsync().AsTask();
        call = Last(sent);
        Require(call[2].GetString() == "DotNet.disposeJSObjectReferenceById" && call[4].GetInt32() == 3 && call[5].GetInt64() == 0,
            "Module disposal did not use the standard global void invocation.");
        await CompleteAsync(runtime, call[1].GetInt64(), true, "null");
        await disposal;
        var count = sent.Count;
        await module.DisposeAsync();
        Require(sent.Count == count, "Module disposal was not idempotent.");
        await ExpectAsync<ObjectDisposedException>(() => module.InvokeAsync<int>("afterDispose").AsTask());
    }

    private static async Task ReferenceLifetimeAsync()
    {
        var registry = new BlazorAotInteropRegistry();
        registry.RegisterInstanceVoid<Receiver, int, int, int>("sum", (receiver, a, b, c) =>
        { receiver.Sum = a + b + c; return ValueTask.CompletedTask; },
            ProductInteropJsonContext.Default.Int32, ProductInteropJsonContext.Default.Int32, ProductInteropJsonContext.Default.Int32);
        var sent = new List<string>();
        using var runtime = new AotJsRuntime(sent.Add, null, registry);
        using var other = new AotJsRuntime(_ => { }, null, registry);
        var receiver = new Receiver();
        using var reference = BlazorAotObjectReference.Create(receiver);
        var bind = ((IJSRuntime)runtime).InvokeVoidAsync("capture", reference).AsTask();
        var call = Last(sent);
        using var args = JsonDocument.Parse(call[3].GetString()!);
        var id = args.RootElement[0].GetProperty("__dotNetObject").GetInt64();
        await CompleteAsync(runtime, call[1].GetInt64(), true, "null");
        await bind;
        Expect<InvalidOperationException>(() => other.BindReference(reference));
        await runtime.InvokeDotNetAsync("sum", null, "sum", id, "[1,2,3]");
        Require(receiver.Sum == 6 && Last(sent)[2].GetBoolean(), "Typed multi-argument instance void callback failed.");
        await runtime.InvokeDotNetAsync("arity", null, "sum", id, "[1,2]");
        Require(!Last(sent)[2].GetBoolean() && receiver.Sum == 6, "Wrong callback arity invoked the application delegate.");
        reference.Dispose();
        await runtime.InvokeDotNetAsync("disposed", null, "sum", id, "[1,2,3]");
        Require(!Last(sent)[2].GetBoolean(), ".NET disposal left the callback callable.");
        foreach (var disposalArgs in new string?[] { null, "null", "[]" })
        {
            using var jsDisposed = BlazorAotObjectReference.Create(new Receiver());
            var jsId = runtime.BindReference(jsDisposed);
            await runtime.InvokeDotNetAsync("dispose", null, "__Dispose", jsId, disposalArgs);
            Require(Last(sent)[2].GetBoolean() && Last(sent)[3].GetString() == "null", "JavaScript reference disposal rejected the standard null arguments.");
            Expect<ObjectDisposedException>(() => _ = jsDisposed.Value);
        }
        using var disconnected = BlazorAotObjectReference.Create(new Receiver());
        runtime.BindReference(disconnected);
        runtime.Disconnect();
        Expect<ObjectDisposedException>(() => other.BindReference(disconnected));
        using var standard = DotNetObjectReference.Create(new Receiver());
        await ExpectAsync<NotSupportedException>(() => ((IJSRuntime)other).InvokeVoidAsync("capture", standard).AsTask());
    }

    private static async Task ByteInterleavingAsync()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<byte[]>();
        var registry = new BlazorAotInteropRegistry();
        registry.RegisterStaticVoid<BytePayload>("app", "hold", async value =>
        { observed.Add(value.Data); await release.Task; }, ProductInteropJsonContext.Default.BytePayload);
        registry.RegisterStaticVoid<BytePayload>("app", "next", value =>
        { observed.Add(value.Data); return ValueTask.CompletedTask; }, ProductInteropJsonContext.Default.BytePayload);
        var sent = new List<string>();
        using var runtime = new AotJsRuntime(sent.Add, ProductInteropJsonContext.Default, registry);
        await ReceiveAsync(runtime, "[\"ReceiveByteArrayFromJS\",0,\"AQI=\"]");
        var first = runtime.InvokeDotNetAsync("first", "app", "hold", 0, "[{\"data\":{\"__byte[]\":0}}]");
        Require(!first.IsCompleted && observed.Count == 1, "The first callback was not suspended after typed byte-array deserialization.");
        await ReceiveAsync(runtime, "[\"ReceiveByteArrayFromJS\",0,\"AwQ=\"]");
        release.SetResult();
        await first;
        await runtime.InvokeDotNetAsync("second", "app", "next", 0, "[{\"data\":{\"__byte[]\":0}}]");
        Require(Last(sent)[2].GetBoolean() && observed.Count == 2 && observed[0].SequenceEqual(new byte[] { 1, 2 }) && observed[1].SequenceEqual(new byte[] { 3, 4 }),
            "An older asynchronous callback cleared a newer callback's byte-array packet.");
        sent.Clear();
        var outbound = runtime.InvokeAsync<int>("bytes", [new BytePayload { Data = [5, 6] }]).AsTask();
        Require(sent.Count == 2 && Parse(sent[0])[0].GetString() == "SendByteArrayToJS", "Nested outgoing bytes did not use the standard byte-array channel.");
        var call = Last(sent);
        using var args = JsonDocument.Parse(call[3].GetString()!);
        Require(args.RootElement[0].GetProperty("data").GetProperty("__byte[]").GetInt32() == Parse(sent[0])[1].GetInt32(), "Nested byte-array metadata lost its wire ID.");
        await CompleteAsync(runtime, call[1].GetInt64(), true, "7");
        Require(await outbound == 7, "The byte-array invocation did not finish.");
    }

    private static async Task InvalidCompletionAsync()
    {
        var sent = new List<string>();
        using var runtime = new AotJsRuntime(sent.Add, null, new());
        await ExpectAsync<NotSupportedException>(() => runtime.InvokeAsync<UnknownResult>("mustNotRun", []).AsTask());
        Require(sent.Count == 0, "Unknown result metadata allowed JavaScript execution.");
        var pending = runtime.InvokeAsync<int>("result", []).AsTask();
        var id = Last(sent)[1].GetInt64();
        await ExpectAsync<JsonException>(() => CompleteAsync(runtime, id, false, "{}"));
        Require(!pending.IsCompleted, "A malformed failure removed the real pending invocation.");
        await CompleteAsync(runtime, id, true, "42");
        Require(await pending == 42, "A valid completion could not recover after a rejected malformed completion.");
        await CompleteAsync(runtime, id, true, "99");
        Require(await pending == 42, "A duplicate completion changed a finished result.");
    }

    private static async Task ByteAdmissionAsync()
    {
        var sent = new List<string>();
        using var runtime = new AotJsRuntime(sent.Add, ProductInteropJsonContext.Default, new());
        await ExpectAsync<NotSupportedException>(() => runtime.InvokeAsync<int>("invalidLaterArgument", [new byte[] { 1 }, new UnknownResult()]).AsTask());
        Require(sent.Count == 0, "Argument serialization failure leaked an earlier byte-array packet.");

        var pending = new Task<int>[4096];
        for (var index = 0; index < pending.Length; index++)
            pending[index] = runtime.InvokeAsync<int>("fill", []).AsTask();
        sent.Clear();
        await ExpectAsync<InvalidOperationException>(() => runtime.InvokeAsync<int>("overLimit", [new byte[] { 2 }]).AsTask());
        Require(sent.Count == 0, "A call rejected at the pending-call limit leaked a byte-array packet.");
        runtime.Disconnect();
        await ExpectAsync<JSDisconnectedException>(() => Task.WhenAll(pending));

        var registry = new BlazorAotInteropRegistry();
        registry.RegisterStatic<ByteResult>("app", "badResult", async () =>
        {
            await Task.Yield();
            return new ByteResult { Data = [3], Unsupported = new UnknownResult() };
        }, ProductInteropJsonContext.Default.ByteResult);
        registry.RegisterStatic<BytePayload>("app", "goodResult", async () =>
        {
            await Task.Yield();
            return new BytePayload { Data = [4] };
        }, ProductInteropJsonContext.Default.BytePayload);
        sent.Clear();
        using var receiver = new AotJsRuntime(sent.Add, ProductInteropJsonContext.Default, registry);
        await receiver.InvokeDotNetAsync("bad", "app", "badResult", 0, "[]");
        Require(sent.Count == 1 && Last(sent)[0].GetString() == "EndInvokeDotNet" && !Last(sent)[2].GetBoolean(),
            "Failed callback result serialization leaked an earlier byte-array packet.");
        sent.Clear();
        await receiver.InvokeDotNetAsync("good", "app", "goodResult", 0, "[]");
        Require(sent.Count == 2 && Parse(sent[0])[0].GetString() == "SendByteArrayToJS" && Last(sent)[2].GetBoolean(),
            "A generated asynchronous callback result did not flush its staged byte-array packet.");

        sent.Clear();
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
            (object)receiver.InvokeAsync<int>("concurrent", [new byte[] { (byte)index }]).AsTask())));
        Require(sent.Count == 32, "Concurrent byte-array invocations lost protocol frames.");
        for (var index = 0; index < sent.Count; index += 2)
        {
            var bytes = Parse(sent[index]);
            var call = Parse(sent[index + 1]);
            using var args = JsonDocument.Parse(call[3].GetString()!);
            Require(bytes[0].GetString() == "SendByteArrayToJS" && call[0].GetString() == "BeginInvokeJS" &&
                args.RootElement[0].GetProperty("__byte[]").GetInt32() == bytes[1].GetInt32(),
                "Concurrent invocation byte-array groups interleaved.");
            await CompleteAsync(receiver, call[1].GetInt64(), true, "1");
        }
        Require((await Task.WhenAll(concurrent.Cast<Task<int>>())).All(value => value == 1), "Concurrent byte-array invocations did not finish.");
    }

    private static Task CompleteAsync(AotJsRuntime runtime, long id, bool success, string result)
    {
        var inner = $"[{id},{(success ? "true" : "false")},{result}]";
        var encoded = JsonSerializer.Serialize(inner, ProductInteropJsonContext.Default.String);
        return ReceiveAsync(runtime, $"[\"EndInvokeJS\",{id},{(success ? "true" : "false")},{encoded}]");
    }

    private static async Task ReceiveAsync(AotJsRuntime runtime, string message)
    {
        using var json = JsonDocument.Parse(message);
        await runtime.ReceiveAsync(json.RootElement);
    }

    private static JsonElement Last(List<string> sent) => Parse(sent[^1]);
    private static JsonElement Parse(string frame)
    {
        using var json = JsonDocument.Parse(frame.AsMemory("__bwv:".Length));
        return json.RootElement.Clone();
    }

    private static void Expect<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static async Task ExpectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Receiver { internal int Sum; }
    private sealed class UnknownResult;
}

internal sealed class BytePayload
{
    public byte[] Data { get; set; } = [];
}

internal sealed class ByteResult
{
    public byte[] Data { get; set; } = [];
    public object? Unsupported { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BytePayload))]
[JsonSerializable(typeof(ByteResult))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
internal partial class ProductInteropJsonContext : JsonSerializerContext;
