using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NetWiv.BlazorHybrid.Aot;
using NetWiv.BlazorHybrid.Aot.Hosting;
using NetWiv.BlazorHybrid.Aot.Interop;
using NetWiv.BlazorHybrid.Aot.Tests.Interop;

namespace NetWiv.BlazorHybrid.Aot.Tests;

internal static class ProtocolProof
{
    internal static async Task<int> RunAsync()
    {
        await using var fixture = new ProtocolFixture();
        await fixture.AttachAsync();
        ProofGate.Check(CounterComponent.RenderedLabel == "compiled Razor", "Component parameter binding failed.");
        var reply = CounterComponent.JsReply!;
        ProofGate.Check(reply.Name == "reply" && reply.Count == 7 && reply.Items is [not null, null] &&
            reply.Items[0]!.Value == long.MinValue && reply.Optional?.Value == 42, "Nested generated DTO reply changed.");
        Console.WriteLine("PASS compiled Razor + official Renderer + generated nested DTO/array/null JS roundtrip");

        var handler = fixture.Batches.SelectMany(batch => batch.Events).Single(item => item.Name == "onclick").Handler;
        var arguments = "[{\"eventHandlerId\":" + handler.ToString(CultureInfo.InvariantCulture) +
            ",\"eventName\":\"click\",\"eventFieldInfo\":null},{\"detail\":1}]";
        var dispatched = await fixture.CallAsync("event", null, "DispatchEventAsync", 1, arguments);
        ProofGate.Check(dispatched[2].GetBoolean(), "The actual renderer event dispatch failed.");
        ProofGate.Check(CounterComponent.Count == 1 && fixture.Batches.Any(batch => batch.Text.Contains("1")),
            "The real outgoing render batches did not contain the updated counter.");
        Console.WriteLine("PASS serialized render-batch handler identity -> official event dispatch -> counter rerender");

        var input = JsonSerializer.Serialize(ProofData.Input(), ProofJsonContext.Default.ProbeDto);
        foreach (var (assembly, method, id, prefix, increment) in new[]
        {
            (ProofGate.Assembly, "StaticTask", 0L, "static-task:", 10),
            (ProofGate.Assembly, "StaticValueTask", 0L, "static-value-task:", 20),
            ((string?)null, "InstanceTask", fixture.ReceiverId, "instance:task:", 30),
            ((string?)null, "InstanceValueTask", fixture.ReceiverId, "instance:value-task:", 40),
        })
        {
            var completion = await fixture.CallAsync(method, assembly, method, id, "[" + input + "]");
            ProofGate.Check(completion[2].GetBoolean(), "A registered DTO invocation failed: " + method);
            using var value = JsonDocument.Parse(completion[3].GetString()!);
            var dto = JsonSerializer.Deserialize(value.RootElement, ProofJsonContext.Default.ProbeDto)!;
            ProofGate.Check(dto.Name == prefix + "input" && dto.Count == 3 + increment &&
                dto.Items is [not null, null, not null] && dto.Items[0]!.Value == long.MaxValue &&
                dto.Items[2]!.Value == -5 && dto.Optional is null, "A registered DTO invocation changed its nested values.");
            ProofGate.Check(value.RootElement.GetProperty("items")[0].GetProperty("value").ValueKind == JsonValueKind.String,
                "A large integer crossed the JS boundary as an imprecise numeric value.");
        }
        foreach (var (assembly, method, id, expected) in new[]
        {
            (ProofGate.Assembly, "StaticNumber", 0L, 12),
            ((string?)null, "InstanceNumber", fixture.ReceiverId, 7),
        })
        {
            var completion = await fixture.CallAsync(method, assembly, method, id, "[6]");
            ProofGate.Check(completion[2].GetBoolean() && completion[3].GetString() == expected.ToString(CultureInfo.InvariantCulture),
                "A numeric ValueTask result failed.");
        }
        Console.WriteLine("PASS static and instance JSInvokable Task<DTO>, ValueTask<DTO>, ValueTask<int>");

        foreach (var (assembly, id) in new[] { (ProofGate.Assembly, 0L), ((string?)null, fixture.ReceiverId) })
        {
            var completion = await fixture.CallAsync("unknown-" + id, assembly, "UnregisteredMethod", id, "[]");
            ProofGate.Check(!completion[2].GetBoolean(), "An unregistered invokable method executed.");
        }
        var sent = fixture.Sent;
        await UnsupportedAsync(() => fixture.Runtime.InvokeAsync<UnregisteredDto>("mustNotExecute", []).AsTask());
        await UnsupportedAsync(() => fixture.Runtime.InvokeAsync<string>("mustNotExecute", [new UnregisteredDto()]).AsTask());
        ProofGate.Check(fixture.Sent == sent, "An unregistered DTO executed JavaScript before being rejected.");
        Console.WriteLine("PASS unknown static/instance invokable and unregistered argument/result types fail closed");
        var disposed = await fixture.CallAsync("disposed", null, "InstanceNumber", fixture.DisposedReceiverId, "[1]");
        ProofGate.Check(!disposed[2].GetBoolean(), "A .NET-disposed callback executed.");
        var releasing = await fixture.CallAsync("release", null, "__Dispose", fixture.JsDisposedReceiverId, "null");
        ProofGate.Check(releasing[2].GetBoolean(), "JavaScript disposal failed.");
        var released = await fixture.CallAsync("released", null, "InstanceNumber", fixture.JsDisposedReceiverId, "[1]");
        ProofGate.Check(!released[2].GetBoolean(), "A JavaScript-disposed callback executed.");
        Console.WriteLine("PASS standard void/module transport and document-owned reference disposal");
        await fixture.DisposeAsync();
        ProofGate.Check(CounterComponent.CapturedScope?.Disposed == true, "Host disposal did not await its component's scoped async service.");
        Console.WriteLine("PASS host disposal awaits the component's injected asynchronous scoped service");
        await InteropFailureTests.RunAsync();
        Console.WriteLine("PASS cancellation, disconnect, transport-send failure and asynchronous invokable errors");
        await RendererBoundTests.RunAsync();
        if (OperatingSystem.IsWindows()) await RuntimeBoundaryTests.RunAsync();
        return 0;
    }

    private static async Task UnsupportedAsync(Func<Task> operation)
    {
        try { await operation(); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("An unregistered type did not fail closed.");
    }
}

internal sealed class ProtocolFixture : IAsyncDisposable
{
    private readonly System.Threading.Channels.Channel<string> _outgoing = System.Threading.Channels.Channel.CreateUnbounded<string>();
    private readonly ServiceProvider _services;
    private readonly AotWebViewSession _host;
    private readonly BlazorDispatcher _dispatcher = new();
    private readonly Uri _origin = new("http://netwiv-blazor.localhost/");
    private readonly Dictionary<string, JsonElement> _completed = [];
    private int _sent;
    private bool _disposed;
    internal int Sent => Volatile.Read(ref _sent);
    internal AotJsRuntime Runtime => _host.JSRuntime;
    internal long ReceiverId { get; private set; }
    internal long DisposedReceiverId { get; private set; }
    internal long JsDisposedReceiverId { get; private set; }
    private bool _ready;
    internal List<BatchReader> Batches { get; } = [];

    internal ProtocolFixture()
    {
        var options = new BlazorAotOptions();
        ProofGate.Configure(options);
        var web = new BlazorWebViewOptions();
        web.RootComponents.Add<CounterComponent>("#app", new Dictionary<string, object?> { [nameof(CounterComponent.Label)] = "compiled Razor" });
        _services = new ServiceCollection().AddScoped<ProofScope>().AddNetWivBlazorHybridAot(ProofGate.Configure).BuildServiceProvider();
        _host = new(_services, _dispatcher, _origin, web, options, Send, error => throw new InvalidOperationException("The product session failed.", error));
    }

    internal async Task AttachAsync()
    {
        await _host.InitializeAsync();
        var attaching = Receive("__bwv:[\"AttachPage\",\"http://netwiv-blazor.localhost/\",\"http://netwiv-blazor.localhost/\"]");
        await PumpUntilAsync(() => _ready && Batches.Count >= 2, attaching);
    }

    internal async Task<JsonElement> CallAsync(string call, string? assembly, string method, long objectId, string arguments)
    {
        var operation = Receive(Message("BeginInvokeDotNet", writer =>
        {
            writer.WriteStringValue(call);
            writer.WriteStringValue(assembly);
            writer.WriteStringValue(method);
            writer.WriteNumberValue(objectId);
            writer.WriteStringValue(arguments);
        }));
        await PumpUntilAsync(() => _completed.ContainsKey(call), operation);
        return _completed[call];
    }

    private void Send(string message)
    {
        Interlocked.Increment(ref _sent);
        _outgoing.Writer.TryWrite(message);
    }

    private async Task PumpUntilAsync(Func<bool> completed, Task operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!completed())
        {
            if (operation.IsFaulted) await operation;
            var message = await _outgoing.Reader.ReadAsync(timeout.Token);
            using var document = JsonDocument.Parse(message.AsMemory("__bwv:".Length));
            var values = document.RootElement;
            switch (values[0].GetString())
            {
                case "BeginInvokeJS":
                    await CompleteJsAsync(values);
                    break;
                case "RenderBatch":
                    var batch = new BatchReader(Convert.FromBase64String(values[2].GetString()!));
                    batch.Read();
                    Batches.Add(batch);
                    await Receive(Message("OnRenderCompleted", writer =>
                    {
                        writer.WriteNumberValue(values[1].GetInt64());
                        writer.WriteNullValue();
                    }));
                    break;
                case "EndInvokeDotNet":
                    _completed.Add(values[1].GetString()!, values.Clone());
                    break;
                case "AttachToDocument":
                    ProofGate.Check(values[2].GetString() == "#app", "The wrong root selector was attached.");
                    break;
                default:
                    throw new InvalidOperationException("Unexpected outgoing proof message.");
            }
        }
        await operation.WaitAsync(timeout.Token);
    }

    private async Task CompleteJsAsync(JsonElement values)
    {
        var handle = values[1].GetInt64();
        var identifier = values[2].GetString();
        using var arguments = JsonDocument.Parse(values[3].GetString()!);
        JsonElement result;
        if (identifier == "Blazor._internal.attachWebRendererInterop")
        {
            ProofGate.Check(arguments.RootElement[0].GetInt32() == 3 &&
                arguments.RootElement[1].GetProperty("__dotNetObject").GetInt64() == 1, "Renderer bootstrap identity changed.");
            result = JsonSerializer.SerializeToElement<object?>(null, ProofJsonContext.Default.Object);
        }
        else if (identifier == "proof.dto")
        {
            var dto = arguments.RootElement[0];
            ProofGate.Check(dto.GetProperty("name").GetString() == "input" && dto.GetProperty("items")[1].ValueKind == JsonValueKind.Null &&
                dto.GetProperty("optional").ValueKind == JsonValueKind.Null &&
                dto.GetProperty("items")[0].GetProperty("value").GetString() == long.MaxValue.ToString(CultureInfo.InvariantCulture),
                "Generated outgoing nested DTO/array/null/quoted integer changed.");
            result = JsonSerializer.SerializeToElement(ProofData.Reply(), ProofJsonContext.Default.ProbeDto);
        }
        else if (identifier == "import")
        {
            ProofGate.Check(values[4].GetInt32() == 1, "Module import did not request an object reference.");
            using var module = JsonDocument.Parse("{\"__jsObjectId\":71}");
            result = module.RootElement.Clone();
        }
        else
        {
            ProofGate.Check(identifier is "proof.voidCall" or "setFlag" or "proof.setInstances" or "proof.ready" or "DotNet.disposeJSObjectReferenceById", "Unexpected JS invocation: " + identifier);
            ProofGate.Check(values[4].GetInt32() == 3, "Standard void interop did not use the void wire result.");
            if (identifier == "setFlag") ProofGate.Check(values[5].GetInt64() == 71, "The module target was lost.");
            if (identifier == "proof.setInstances")
            {
                ReceiverId = arguments.RootElement[0].GetProperty("__dotNetObject").GetInt64();
                DisposedReceiverId = arguments.RootElement[1].GetProperty("__dotNetObject").GetInt64();
                JsDisposedReceiverId = arguments.RootElement[2].GetProperty("__dotNetObject").GetInt64();
            }
            if (identifier == "proof.ready") _ready = true;
            using var empty = JsonDocument.Parse("null");
            result = empty.RootElement.Clone();
        }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(handle);
            writer.WriteBooleanValue(true);
            result.WriteTo(writer);
            writer.WriteEndArray();
        }
        var completion = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        await Receive(Message("EndInvokeJS", writer =>
        {
            writer.WriteNumberValue(handle);
            writer.WriteBooleanValue(true);
            writer.WriteStringValue(completion);
        }));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _host.DisposeAsync();
        await _services.DisposeAsync();
    }

    private Task Receive(string message) => _host.ReceiveAsync(_origin, message);

    internal static string Message(string type, Action<Utf8JsonWriter> arguments)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(type);
            arguments(writer);
            writer.WriteEndArray();
        }
        return "__bwv:" + System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
