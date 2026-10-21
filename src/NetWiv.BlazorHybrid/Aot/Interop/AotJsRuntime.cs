using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace NetWiv.BlazorHybrid.Aot.Interop;

// Typed platform transport for Microsoft's pinned WebView wire format.
// Each generic pending call closes its own reader and TCS at compile time.
// No Task<T> reflection factories or reflective JSON fallback are used.
internal sealed class AotJsRuntime : IJSRuntime, IDisposable, IAotReferenceOwner
{
    // Match the public IJSRuntime/IJSObjectReference generic contract in 8.0.32.
    private const DynamicallyAccessedMemberTypes JsonMembers = DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties;
    private readonly Action<string> _send;
    private readonly ConcurrentDictionary<long, PendingCall> _pending = new();
    private readonly BlazorAotInteropRegistry _registry;
    private readonly ConcurrentDictionary<(long Id, string Method), Func<JsonElement, ValueTask<string>>> _rawInstances = new();
    private readonly ConcurrentDictionary<long, IAotObjectReference> _references = new();
    private readonly object _lifetimeGate = new();
    private readonly object _sendGate = new();
    private readonly AsyncLocal<List<(int Id, byte[] Bytes)>?> _outgoingBytes = new();
    private readonly List<byte[]> _receivedBytes = [];
    private readonly CancellationTokenSource _retirement = new();
    private readonly CancellationToken _retirementToken;
    private int _inboundCalls;
    private long _nextCall;
    private long _nextReference = 1; // The renderer owns reference ID 1.
    private int _nextByteArray;
    private volatile bool _disconnected;

    internal AotJsRuntime(Action<string> send, IJsonTypeInfoResolver? resolver, BlazorAotInteropRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(registry);
        _send = send;
        _retirementToken = _retirement.Token;
        _registry = registry.Snapshot();
        ElementReferenceContext = new WebElementReferenceContext(this);
        JsonOptions = new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine([new AotReferenceResolver(this, _registry), .. resolver is null ? [] : new[] { resolver }, .. _registry.Resolvers, AotInteropJsonContext.Default])
        };
        JsonOptions.Converters.Add(new UnsupportedDotNetReferenceConverter());
        JsonOptions.Converters.Add(new AotByteArrayConverter(this));
        JsonOptions.Converters.Add(new AotElementReferenceConverter(ElementReferenceContext));
        foreach (var converter in _registry.ReferenceConverters.Values) JsonOptions.Converters.Add(converter(this));
    }

    internal JsonSerializerOptions JsonOptions { get; }
    internal ElementReferenceContext ElementReferenceContext { get; }

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<TValue>(0, identifier, cancellationToken, args);

    internal async Task InvokeVoidAsync(string identifier, params object?[] args) =>
        _ = await InvokeAsync<IJSVoidResult>(identifier, args);

#if NET10_0_OR_GREATER
    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, object?[]? args) =>
        InvokeConstructorAsync(identifier, CancellationToken.None, args);
    public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        InvokeAsync<IJSObjectReference>(0, identifier, cancellationToken, args, JSCallType.ConstructorCall);
    public ValueTask<TValue> GetValueAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier) =>
        GetValueAsync<TValue>(identifier, CancellationToken.None);
    public ValueTask<TValue> GetValueAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, CancellationToken cancellationToken) =>
        InvokeAsync<TValue>(0, identifier, cancellationToken, null, JSCallType.GetValue);
    public ValueTask SetValueAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, TValue value) =>
        SetValueAsync(identifier, value, CancellationToken.None);
    public async ValueTask SetValueAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, TValue value, CancellationToken cancellationToken) =>
        _ = await InvokeAsync<IJSVoidResult>(0, identifier, cancellationToken, [value], JSCallType.SetValue);
#endif

    private ValueTask<TValue> InvokeAsync<TValue>(long target, string identifier, CancellationToken cancellationToken, object?[]? args
#if NET10_0_OR_GREATER
        , JSCallType callType = JSCallType.FunctionCall
#endif
        )
    {
        if (_disconnected) throw Disconnected();
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        cancellationToken.ThrowIfCancellationRequested();
        Func<JsonElement, TValue> read;
        var resultType = JSCallResultType.Default;
        if (typeof(TValue) == typeof(IJSVoidResult))
        {
            resultType = JSCallResultType.JSVoidResult;
            read = _ => default!;
        }
        else if (typeof(TValue) == typeof(IJSObjectReference))
        {
            resultType = JSCallResultType.JSObjectReference;
            read = value => (TValue)(object)new ObjectReference(this, value.GetProperty("__jsObjectId").GetInt64());
        }
        else if (typeof(TValue) == typeof(IJSStreamReference))
            throw new NotSupportedException("NativeAOT JavaScript stream references are not supported.");
        else
        {
            // Resolve before sending: an unsupported result cannot execute JS.
            var info = JsonOptions.GetTypeInfo(typeof(TValue)) as JsonTypeInfo<TValue>
                ?? throw new NotSupportedException($"No generated JSON metadata is registered for '{typeof(TValue)}'.");
            read = value => JsonSerializer.Deserialize(value, info)!;
        }
        var argsInfo = JsonOptions.GetTypeInfo(typeof(object[]));
        if (args is not null)
            foreach (var argument in args)
                if (argument is not null && argument.GetType().IsGenericType &&
                    argument.GetType().GetGenericTypeDefinition() == typeof(DotNetObjectReference<>))
                    throw new NotSupportedException("NativeAOT callbacks require BlazorAotObjectReference<T>, not DotNetObjectReference<T>.");
        var outgoing = new List<(int Id, byte[] Bytes)>();
        var previous = _outgoingBytes.Value;
        string? argsJson;
        _outgoingBytes.Value = outgoing;
        try { argsJson = args is null ? null : JsonSerializer.Serialize(args, argsInfo); }
        finally { _outgoingBytes.Value = previous; }
        var id = Interlocked.Increment(ref _nextCall);
        var call = new PendingCall<TValue>(read);
        lock (_lifetimeGate)
        {
            if (_disconnected) throw Disconnected();
            if (_pending.Count >= 4096) throw new InvalidOperationException("The WebView document exceeded 4096 pending JavaScript calls.");
            if (!_pending.TryAdd(id, call)) throw new InvalidOperationException("Duplicate JS call ID.");
        }
        call.Register(cancellationToken, () =>
        {
            if (_pending.TryRemove(id, out var pending)) pending.Cancel(cancellationToken);
        });
        try
        {
            lock (_sendGate)
            {
                if (_disconnected)
                {
                    if (_pending.TryRemove(id, out var pending)) pending.Fail(Disconnected());
                }
                else if (_pending.ContainsKey(id))
                {
                    FlushBytes(outgoing);
                    _send(Frame("BeginInvokeJS", writer =>
                    {
                        writer.WriteNumberValue(id);
                        writer.WriteStringValue(identifier);
                        writer.WriteStringValue(argsJson);
                        writer.WriteNumberValue((int)resultType);
                        writer.WriteNumberValue(target);
#if NET10_0_OR_GREATER
                        writer.WriteNumberValue((int)callType);
#endif
                    }));
                }
            }
        }
        catch (Exception error)
        {
            if (_pending.TryRemove(id, out var pending)) pending.Fail(error);
        }
        return new(call.Task);
    }

    internal void RegisterInstanceRaw(long id, string method, Func<JsonElement, ValueTask<string>> invoke)
    {
        if (id != 1) throw new ArgumentOutOfRangeException(nameof(id), "Only the reserved renderer reference accepts raw callbacks.");
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(invoke);
        if (!_rawInstances.TryAdd((id, method), invoke)) throw new ArgumentException("The renderer callback is already registered.", nameof(method));
    }

    internal async Task ReceiveAsync(JsonElement envelope)
    {
        if (_disconnected) return;
        switch (envelope[0].GetString())
        {
            case "EndInvokeJS":
                using (var document = JsonDocument.Parse(envelope[3].GetString()!))
                {
                    var values = document.RootElement;
                    if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 3)
                        throw new JsonException("Invalid JavaScript completion.");
                    var id = values[0].GetInt64();
                    var success = values[1].GetBoolean();
                    if (!success && values[2].ValueKind != JsonValueKind.String)
                        throw new JsonException("A failed JavaScript completion requires an error string.");
                    try
                    {
                        if (_pending.TryRemove(id, out var call))
                        {
                            if (success) call.Complete(values[2]);
                            else call.Fail(new JSException(values[2].GetString()!));
                        }
                    }
                    finally { lock (_receivedBytes) _receivedBytes.Clear(); }
                }
                break;
            case "ReceiveByteArrayFromJS":
                ReceiveBytes(envelope[1].GetInt32(), envelope[2].GetBytesFromBase64());
                break;
            case "BeginInvokeDotNet":
                await InvokeDotNetAsync(envelope[1].GetString(), envelope[2].GetString(), envelope[3].GetString()!,
                    envelope[4].GetInt64(), envelope[5].GetString());
                break;
            default:
                throw new NotSupportedException("The NativeAOT interop runtime does not handle this message type.");
        }
    }

    internal async Task InvokeDotNetAsync(string? callId, string? assembly, string method, long objectId, string? arguments)
    {
        lock (_lifetimeGate)
        {
            if (_disconnected) throw Disconnected();
            if (_inboundCalls >= 4096) throw new InvalidOperationException("The WebView document exceeded 4096 pending .NET callbacks.");
            _inboundCalls++;
        }
        bool success;
        string result;
        var outgoing = new List<(int Id, byte[] Bytes)>();
        var previous = _outgoingBytes.Value;
        _outgoingBytes.Value = outgoing;
        Task<string>? operation = null;
        try
        {
            operation = InvokeCallbackCoreAsync(assembly, method, objectId, arguments);
            result = await operation.WaitAsync(_retirementToken);
            success = true;
        }
        catch (OperationCanceledException) when (_retirementToken.IsCancellationRequested)
        {
            if (operation is not null)
                _ = operation.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }
        catch (Exception error)
        {
            result = error.Message;
            success = false;
        }
        finally
        {
            _outgoingBytes.Value = previous;
            lock (_lifetimeGate) _inboundCalls--;
        }
        if (callId is not null)
            lock (_sendGate)
            {
                if (_disconnected) return;
                if (success) FlushBytes(outgoing);
                _send(Frame("EndInvokeDotNet", writer =>
                {
                    writer.WriteStringValue(callId);
                    writer.WriteBooleanValue(success);
                    writer.WriteStringValue(result);
                }));
            }
    }

    private async Task<string> InvokeCallbackCoreAsync(string? assembly, string method, long objectId, string? arguments)
    {
        using var args = ParseCallbackArguments(arguments);
        ValueTask<string> invocation;
        try
        {
            if (objectId == 0)
            {
                if (assembly is null || !_registry.StaticHandlers.TryGetValue((assembly, method), out var invoke))
                    throw new NotSupportedException("The static JS-invokable method is not registered.");
                invocation = invoke(args.RootElement, JsonOptions);
            }
            else
            {
                if (assembly is not null) throw new NotSupportedException("Instance callbacks cannot specify an assembly.");
                if (_rawInstances.TryGetValue((objectId, method), out var raw)) invocation = raw(args.RootElement);
                else if (_references.TryGetValue(objectId, out var reference))
                {
                    if (method == "__Dispose")
                    {
                        if (args.RootElement.ValueKind != JsonValueKind.Null &&
                            (args.RootElement.ValueKind != JsonValueKind.Array || args.RootElement.GetArrayLength() != 0))
                            throw new JsonException("Object disposal does not accept arguments.");
                        reference.Dispose();
                        invocation = ValueTask.FromResult("null");
                    }
                    else if (_registry.InstanceHandlers.TryGetValue((reference.TargetType, method), out var invoke))
                        invocation = invoke(reference.Value, args.RootElement, JsonOptions);
                    else throw new NotSupportedException("The instance JS-invokable method is not registered.");
                }
                else throw new NotSupportedException("The callback reference was disposed or belongs to another document.");
            }
        }
        finally { lock (_receivedBytes) _receivedBytes.Clear(); }
        // The callback owns its argument document until its real completion,
        // even when document retirement detaches the transport's wait.
        return await invocation;
    }

    private JsonDocument ParseCallbackArguments(string? arguments)
    {
        try { return JsonDocument.Parse(arguments ?? "null"); }
        catch
        {
            lock (_receivedBytes) _receivedBytes.Clear();
            throw;
        }
    }

    internal void Disconnect()
    {
        lock (_sendGate)
            lock (_lifetimeGate)
            {
                if (_disconnected) return;
                _disconnected = true;
            }
        _retirement.Cancel();
        _retirement.Dispose();
        foreach (var (id, _) in _pending)
            if (_pending.TryRemove(id, out var call)) call.Fail(Disconnected());
        foreach (var reference in _references.Values) reference.Dispose();
        _rawInstances.Clear();
        lock (_receivedBytes) _receivedBytes.Clear();
    }

    public void Dispose() => Disconnect();
    private static JSDisconnectedException Disconnected() => new("The WebView document is disconnected.");

    long IAotReferenceOwner.Track(IAotObjectReference reference)
    {
        lock (_lifetimeGate)
        {
            if (_disconnected) throw Disconnected();
            if (!_registry.ReferenceConverters.ContainsKey(reference.GetType()))
                throw new NotSupportedException("Register callback methods for this NativeAOT object-reference type before using it.");
            if (_references.Count >= 4096) throw new InvalidOperationException("The WebView document exceeded 4096 callback references.");
            var id = Interlocked.Increment(ref _nextReference);
            if (!_references.TryAdd(id, reference)) throw new InvalidOperationException("Duplicate callback reference ID.");
            return id;
        }
    }

    void IAotReferenceOwner.Release(long id) => _references.TryRemove(id, out _);
    internal long BindReference(IAotObjectReference reference) => reference.Bind(this);
    internal IAotObjectReference GetReference(long id) => _references.TryGetValue(id, out var reference)
        ? reference : throw new JsonException("The callback reference was disposed or belongs to another document.");

    internal int SendBytes(byte[] bytes)
    {
        if (_disconnected) throw Disconnected();
        var outgoing = _outgoingBytes.Value
            ?? throw new InvalidOperationException("JavaScript byte arrays must be serialized as part of an interop invocation.");
        var id = Interlocked.Increment(ref _nextByteArray);
        outgoing.Add((id, (byte[])bytes.Clone()));
        return id;
    }

    // The caller holds _sendGate so each packet group stays adjacent to the
    // invocation that consumes it. Failed serialization/admission sends nothing.
    private void FlushBytes(List<(int Id, byte[] Bytes)> outgoing)
    {
        foreach (var (id, bytes) in outgoing)
            _send(Frame("SendByteArrayToJS", writer =>
            { writer.WriteNumberValue(id); writer.WriteBase64StringValue(bytes); }));
    }

    private void ReceiveBytes(int id, byte[] bytes)
    {
        lock (_receivedBytes)
        {
            if (id == 0) _receivedBytes.Clear();
            if (id != _receivedBytes.Count || id >= 4096) throw new JsonException("Invalid JavaScript byte-array sequence.");
            _receivedBytes.Add(bytes);
        }
    }

    internal byte[] ReadBytes(int id)
    {
        lock (_receivedBytes)
            return id >= 0 && id < _receivedBytes.Count ? _receivedBytes[id] : throw new JsonException("The JavaScript byte array was not received.");
    }

    private static string Frame(string type, Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(type);
            write(writer);
            writer.WriteEndArray();
        }
        return "__bwv:" + Encoding.UTF8.GetString(buffer.ToArray());
    }

    private abstract class PendingCall
    {
        private readonly object _registrationGate = new();
        private CancellationTokenRegistration _registration;
        private bool _finished;

        internal void Register(CancellationToken token, Action cancel)
        {
            // Register may invoke cancel synchronously before returning its
            // registration. Completion must hand that late ownership back.
            var registration = token.Register(cancel);
            lock (_registrationGate)
            {
                if (!_finished)
                {
                    _registration = registration;
                    return;
                }
            }
            registration.Dispose();
        }

        private void ReleaseRegistration()
        {
            CancellationTokenRegistration registration;
            lock (_registrationGate)
            {
                _finished = true;
                registration = _registration;
                _registration = default;
            }
            // Disposal may wait for cancel, which also takes the gate.
            registration.Dispose();
        }

        internal void Complete(JsonElement value)
        {
            ReleaseRegistration();
            try { SetResult(value); }
            catch (Exception error) { SetException(error); }
        }
        internal void Fail(Exception error) { ReleaseRegistration(); SetException(error); }
        internal void Cancel(CancellationToken token) { ReleaseRegistration(); SetCanceled(token); }
        protected abstract void SetResult(JsonElement value);
        protected abstract void SetException(Exception error);
        protected abstract void SetCanceled(CancellationToken token);
    }

    private sealed class PendingCall<TValue>(Func<JsonElement, TValue> read) : PendingCall
    {
        private readonly TaskCompletionSource<TValue> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<TValue> Task => _completion.Task;
        protected override void SetResult(JsonElement value) => _completion.TrySetResult(read(value));
        protected override void SetException(Exception error) => _completion.TrySetException(error);
        protected override void SetCanceled(CancellationToken token) => _completion.TrySetCanceled(token);
    }

    private sealed class ObjectReference(AotJsRuntime runtime, long id) : IJSObjectReference
    {
        private bool _disposed;
        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return runtime.InvokeAsync<TValue>(id, identifier, cancellationToken, args);
        }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _ = await runtime.InvokeAsync<IJSVoidResult>(0, "DotNet.disposeJSObjectReferenceById", CancellationToken.None, [id]);
        }
#if NET10_0_OR_GREATER
        public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, object?[]? args) =>
            InvokeConstructorAsync(identifier, CancellationToken.None, args);
        public ValueTask<IJSObjectReference> InvokeConstructorAsync(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return runtime.InvokeAsync<IJSObjectReference>(id, identifier, cancellationToken, args, JSCallType.ConstructorCall);
        }
        public ValueTask<TValue> GetValueAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier) =>
            GetValueAsync<TValue>(identifier, CancellationToken.None);
        public ValueTask<TValue> GetValueAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return runtime.InvokeAsync<TValue>(id, identifier, cancellationToken, null, JSCallType.GetValue);
        }
        public ValueTask SetValueAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, TValue value) =>
            SetValueAsync(identifier, value, CancellationToken.None);
        public async ValueTask SetValueAsync<[DynamicallyAccessedMembers(JsonMembers)] TValue>(string identifier, TValue value, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _ = await runtime.InvokeAsync<IJSVoidResult>(id, identifier, cancellationToken, [value], JSCallType.SetValue);
        }
#endif
    }
}
