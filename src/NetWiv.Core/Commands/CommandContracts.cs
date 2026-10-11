using System.Text.Json;

namespace NetWiv;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CommandAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class CommandJsonContextAttribute(Type contextType) : Attribute
{
    public Type ContextType { get; } = contextType ?? throw new ArgumentNullException(nameof(contextType));
}

public delegate ValueTask<JsonElement> CommandHandler(
    InvokeContext context,
    JsonElement arguments,
    CancellationToken cancellationToken);

public delegate ValueTask<Runtime.RuntimeInvokeResponse> ResponseCommandHandler(
    InvokeContext context,
    JsonElement arguments,
    CancellationToken cancellationToken);

public sealed record CommandDescriptor(string Name, CommandHandler Handler);
public sealed record ResponseCommandDescriptor(string Name, ResponseCommandHandler Handler);

public sealed class InvokeContext
{
    internal InvokeContext(AppHandle app, string windowLabel, string webviewLabel, Uri source, bool trusted, Runtime.RuntimeCommandScope scope, ulong documentId = 0, Request? request = null)
    {
        App = app;
        WindowLabel = windowLabel;
        WebviewLabel = webviewLabel;
        Source = source;
        IsTrusted = trusted;
        Scope = scope;
        DocumentId = documentId;
        Request = request ?? new Request(InvokeBody.FromJson(JsonValue.EmptyObject));
    }

    public AppHandle App { get; }
    public string WindowLabel { get; }
    public string WebviewLabel { get; }
    public Uri Source { get; }
    public bool IsTrusted { get; }
    public Runtime.RuntimeCommandScope Scope { get; }
    public ulong DocumentId { get; }
    public Request Request { get; }
    public State<T> State<T>() where T : notnull => App.State<T>();
    /// <summary>Registers a resource owned by the invoking document, or by the app for trusted backend calls.</summary>
    public uint AddResource<T>(T resource) where T : notnull => App.AddInvocationResource(this, resource);
    /// <summary>Runs a platform operation after the plugin command passed the ACL check.</summary>
    public ValueTask<Runtime.RuntimeInvokeResponse> InvokePlatformAsync(string command, JsonElement args, CancellationToken cancellationToken = default) =>
        App.InvokePlatformAsync(command, args, this, cancellationToken);
}

public sealed class CommandRegistry
{
    private readonly Dictionary<string, ResponseCommandHandler> _handlers = new(StringComparer.Ordinal);

    public void Register(CommandDescriptor descriptor) => Register(descriptor.Name, descriptor.Handler);

    public void Register(string name, CommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(name, async (context, arguments, cancellationToken) =>
            Runtime.RuntimeInvokeResponse.FromJson(await handler(context, arguments, cancellationToken))))
        {
            throw new InvalidOperationException($"A command named '{name}' is already registered.");
        }
    }

    public void Register(ResponseCommandDescriptor descriptor) => RegisterResponse(descriptor.Name, descriptor.Handler);

    public void RegisterResponse(string name, ResponseCommandHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(name, handler)) throw new InvalidOperationException($"A command named '{name}' is already registered.");
    }

    public bool TryGet(string name, out ResponseCommandHandler? handler) => _handlers.TryGetValue(name, out handler);
}

/// <summary>Registration target used by generated module initializers.</summary>
public static class GeneratedCommandRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Action<CommandRegistry>> Registrations = new(StringComparer.Ordinal);
    private static readonly List<Action<CommandRegistry>> LegacyRegistrations = [];

    public static void Register(string externalName, Action<CommandRegistry> registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalName);
        ArgumentNullException.ThrowIfNull(registration);
        lock (Gate)
        {
            if (!Registrations.TryAdd(externalName, registration))
                throw new InvalidOperationException($"A generated command named '{externalName}' is already registered.");
        }
    }

    // Compatibility entrypoint for generators emitted before keyed registration.
    public static void Register(Action<CommandRegistry> registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (Gate)
        {
            LegacyRegistrations.Add(registration);
        }
    }

    internal static void ApplyTo(CommandRegistry registry, IReadOnlyList<string> names)
    {
        lock (Gate)
        {
            foreach (var name in names)
            {
                if (!Registrations.TryGetValue(name, out var registration))
                    throw new InvalidOperationException($"Generated command '{name}' was not found. Ensure its [Command] method is included in this build.");
                registration(registry);
            }
        }
    }

    internal static void ApplyAllTo(CommandRegistry registry)
    {
        lock (Gate)
        {
            foreach (var registration in Registrations.Values) registration(registry);
            foreach (var registration in LegacyRegistrations) registration(registry);
        }
    }
}

public sealed class InvokeException : Exception
{
    public InvokeException(string message) : base(message) => ErrorPayload = JsonValue.From(message);
    public InvokeException(string message, Exception innerException) : base(message, innerException) => ErrorPayload = JsonValue.From(message);
    public InvokeException(JsonElement errorPayload)
        : base(errorPayload.ValueKind == JsonValueKind.String ? errorPayload.GetString() : "The command returned a structured error.")
        => ErrorPayload = errorPayload.Clone();
    public JsonElement ErrorPayload { get; }
}
