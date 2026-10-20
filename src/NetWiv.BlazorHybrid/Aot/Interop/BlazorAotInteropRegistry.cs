using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NetWiv.BlazorHybrid.Aot.Interop;

namespace NetWiv.BlazorHybrid.Aot;

/// <summary>Explicit, statically typed JavaScript-to-.NET callback contracts for NativeAOT.</summary>
public sealed class BlazorAotInteropRegistry
{
    private bool _readOnly;

    internal delegate ValueTask<string> StaticHandler(JsonElement arguments, JsonSerializerOptions options);
    internal delegate ValueTask<string> InstanceHandler(object target, JsonElement arguments, JsonSerializerOptions options);
    internal Dictionary<(string Assembly, string Method), StaticHandler> StaticHandlers { get; } = [];
    internal Dictionary<(Type Target, string Method), InstanceHandler> InstanceHandlers { get; } = [];
    internal Dictionary<Type, Func<AotJsRuntime, JsonConverter>> ReferenceConverters { get; } = [];
    internal Dictionary<Type, Func<AotJsRuntime, JsonSerializerOptions, JsonTypeInfo>> ReferenceMetadata { get; } = [];
    internal List<IJsonTypeInfoResolver> Resolvers { get; } = [];

    public void RegisterStatic<TResult>(string assembly, string method, Func<ValueTask<TResult>> invoke, JsonTypeInfo<TResult> resultInfo) =>
        AddStatic(assembly, method, invoke, async (args, options) =>
        { Count(args, 0); return Write(await invoke(), options, resultInfo); }, resultInfo);

    public void RegisterStatic<T1, TResult>(string assembly, string method, Func<T1, ValueTask<TResult>> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<TResult> resultInfo) =>
        AddStatic(assembly, method, invoke, async (args, options) =>
        { Count(args, 1); return Write(await invoke(Read(args[0], options, argument1)), options, resultInfo); }, argument1, resultInfo);

    public void RegisterStatic<T1, T2, TResult>(string assembly, string method, Func<T1, T2, ValueTask<TResult>> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<T2> argument2, JsonTypeInfo<TResult> resultInfo) =>
        AddStatic(assembly, method, invoke, async (args, options) =>
        { Count(args, 2); return Write(await invoke(Read(args[0], options, argument1), Read(args[1], options, argument2)), options, resultInfo); }, argument1, argument2, resultInfo);

    public void RegisterStatic<T1, T2, T3, TResult>(string assembly, string method, Func<T1, T2, T3, ValueTask<TResult>> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<T2> argument2, JsonTypeInfo<T3> argument3, JsonTypeInfo<TResult> resultInfo) =>
        AddStatic(assembly, method, invoke, async (args, options) =>
        { Count(args, 3); return Write(await invoke(Read(args[0], options, argument1), Read(args[1], options, argument2), Read(args[2], options, argument3)), options, resultInfo); }, argument1, argument2, argument3, resultInfo);

    public void RegisterInstance<TInstance, TResult>(string method, Func<TInstance, ValueTask<TResult>> invoke,
        JsonTypeInfo<TResult> resultInfo) where TInstance : class =>
        AddInstance<TInstance>(method, invoke, async (target, args, options) =>
        { Count(args, 0); return Write(await invoke((TInstance)target), options, resultInfo); }, resultInfo);

    public void RegisterInstance<TInstance, T1, TResult>(string method, Func<TInstance, T1, ValueTask<TResult>> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<TResult> resultInfo) where TInstance : class =>
        AddInstance<TInstance>(method, invoke, async (target, args, options) =>
        { Count(args, 1); return Write(await invoke((TInstance)target, Read(args[0], options, argument1)), options, resultInfo); }, argument1, resultInfo);

    public void RegisterInstance<TInstance, T1, T2, TResult>(string method, Func<TInstance, T1, T2, ValueTask<TResult>> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<T2> argument2, JsonTypeInfo<TResult> resultInfo) where TInstance : class =>
        AddInstance<TInstance>(method, invoke, async (target, args, options) =>
        { Count(args, 2); return Write(await invoke((TInstance)target, Read(args[0], options, argument1), Read(args[1], options, argument2)), options, resultInfo); }, argument1, argument2, resultInfo);

    public void RegisterInstance<TInstance, T1, T2, T3, TResult>(string method, Func<TInstance, T1, T2, T3, ValueTask<TResult>> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<T2> argument2, JsonTypeInfo<T3> argument3, JsonTypeInfo<TResult> resultInfo) where TInstance : class =>
        AddInstance<TInstance>(method, invoke, async (target, args, options) =>
        { Count(args, 3); return Write(await invoke((TInstance)target, Read(args[0], options, argument1), Read(args[1], options, argument2), Read(args[2], options, argument3)), options, resultInfo); }, argument1, argument2, argument3, resultInfo);

    public void RegisterStaticVoid(string assembly, string method, Func<ValueTask> invoke) =>
        AddStatic(assembly, method, invoke, async (args, _) =>
        { Count(args, 0); await invoke(); return "null"; });

    public void RegisterStaticVoid<T1>(string assembly, string method, Func<T1, ValueTask> invoke, JsonTypeInfo<T1> argument1) =>
        AddStatic(assembly, method, invoke, async (args, options) =>
        { Count(args, 1); await invoke(Read(args[0], options, argument1)); return "null"; }, argument1);

    public void RegisterStaticVoid<T1, T2>(string assembly, string method, Func<T1, T2, ValueTask> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<T2> argument2) =>
        AddStatic(assembly, method, invoke, async (args, options) =>
        { Count(args, 2); await invoke(Read(args[0], options, argument1), Read(args[1], options, argument2)); return "null"; }, argument1, argument2);

    public void RegisterStaticVoid<T1, T2, T3>(string assembly, string method, Func<T1, T2, T3, ValueTask> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<T2> argument2, JsonTypeInfo<T3> argument3) =>
        AddStatic(assembly, method, invoke, async (args, options) =>
        { Count(args, 3); await invoke(Read(args[0], options, argument1), Read(args[1], options, argument2), Read(args[2], options, argument3)); return "null"; }, argument1, argument2, argument3);

    public void RegisterInstanceVoid<TInstance>(string method, Func<TInstance, ValueTask> invoke) where TInstance : class =>
        AddInstance<TInstance>(method, invoke, async (target, args, _) =>
        { Count(args, 0); await invoke((TInstance)target); return "null"; });

    public void RegisterInstanceVoid<TInstance, T1>(string method, Func<TInstance, T1, ValueTask> invoke,
        JsonTypeInfo<T1> argument1) where TInstance : class =>
        AddInstance<TInstance>(method, invoke, async (target, args, options) =>
        { Count(args, 1); await invoke((TInstance)target, Read(args[0], options, argument1)); return "null"; }, argument1);

    public void RegisterInstanceVoid<TInstance, T1, T2>(string method, Func<TInstance, T1, T2, ValueTask> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<T2> argument2) where TInstance : class =>
        AddInstance<TInstance>(method, invoke, async (target, args, options) =>
        { Count(args, 2); await invoke((TInstance)target, Read(args[0], options, argument1), Read(args[1], options, argument2)); return "null"; }, argument1, argument2);

    public void RegisterInstanceVoid<TInstance, T1, T2, T3>(string method, Func<TInstance, T1, T2, T3, ValueTask> invoke,
        JsonTypeInfo<T1> argument1, JsonTypeInfo<T2> argument2, JsonTypeInfo<T3> argument3) where TInstance : class =>
        AddInstance<TInstance>(method, invoke, async (target, args, options) =>
        { Count(args, 3); await invoke((TInstance)target, Read(args[0], options, argument1), Read(args[1], options, argument2), Read(args[2], options, argument3)); return "null"; }, argument1, argument2, argument3);

    private void AddStatic(string assembly, string method, Delegate callback, StaticHandler invoke, params JsonTypeInfo[] metadata)
    {
        Validate(callback, metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        StaticHandlers.Add((assembly, method), invoke);
        Remember(metadata);
    }

    private void AddInstance<TInstance>(string method, Delegate callback, InstanceHandler invoke, params JsonTypeInfo[] metadata) where TInstance : class
    {
        Validate(callback, metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (method == "__Dispose") throw new ArgumentException("The callback name '__Dispose' is reserved for reference disposal.", nameof(method));
        InstanceHandlers.Add((typeof(TInstance), method), invoke);
        ReferenceConverters.TryAdd(typeof(BlazorAotObjectReference<TInstance>), runtime => new AotObjectReferenceConverter<TInstance>(runtime));
        ReferenceMetadata.TryAdd(typeof(BlazorAotObjectReference<TInstance>), (runtime, options) =>
            JsonMetadataServices.CreateValueInfo<BlazorAotObjectReference<TInstance>>(options, new AotObjectReferenceConverter<TInstance>(runtime)));
        Remember(metadata);
    }

    private void Validate(Delegate callback, JsonTypeInfo[] metadata)
    {
        if (_readOnly) throw new InvalidOperationException("A NativeAOT interop registry snapshot is read-only.");
        ArgumentNullException.ThrowIfNull(callback);
        foreach (var info in metadata) ArgumentNullException.ThrowIfNull(info);
    }

    private void Remember(JsonTypeInfo[] metadata)
    {
        foreach (var info in metadata)
            if (info.Options.TypeInfoResolver is { } resolver && !Resolvers.Contains(resolver)) Resolvers.Add(resolver);
    }

    private static T Read<T>(JsonElement value, JsonSerializerOptions options, JsonTypeInfo<T> declared) =>
        JsonSerializer.Deserialize(value, Resolve(options, declared))!;
    private static string Write<T>(T value, JsonSerializerOptions options, JsonTypeInfo<T> declared) =>
        JsonSerializer.Serialize(value, Resolve(options, declared));
    private static JsonTypeInfo<T> Resolve<T>(JsonSerializerOptions options, JsonTypeInfo<T> declared) =>
        options.GetTypeInfo(declared.Type) as JsonTypeInfo<T>
        ?? throw new NotSupportedException($"Generated JSON metadata for '{declared.Type}' is unavailable.");
    private static void Count(JsonElement arguments, int count)
    {
        if (arguments.ValueKind != JsonValueKind.Array || arguments.GetArrayLength() != count)
            throw new JsonException($"The registered callback requires {count} arguments.");
    }

    internal BlazorAotInteropRegistry Snapshot()
    {
        var snapshot = new BlazorAotInteropRegistry();
        foreach (var entry in StaticHandlers) snapshot.StaticHandlers.Add(entry.Key, entry.Value);
        foreach (var entry in InstanceHandlers) snapshot.InstanceHandlers.Add(entry.Key, entry.Value);
        foreach (var entry in ReferenceConverters) snapshot.ReferenceConverters.Add(entry.Key, entry.Value);
        foreach (var entry in ReferenceMetadata) snapshot.ReferenceMetadata.Add(entry.Key, entry.Value);
        snapshot.Resolvers.AddRange(Resolvers);
        snapshot._readOnly = true;
        return snapshot;
    }
}
