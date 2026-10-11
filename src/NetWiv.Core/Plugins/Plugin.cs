using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NetWiv;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PluginAttribute : Attribute;

/// <summary>Generated metadata used to root plugin factories in consuming applications.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class PluginExportAttribute(Type pluginType) : Attribute
{
    public Type PluginType { get; } = pluginType;
}

public interface IPlugin
{
    string Name { get; }
    void Initialize(PluginContext context);
    string? InitializationScript => null;
    void Setup(AppHandle app) { }
    void OnEvent(AppHandle app, Runtime.RuntimeEvent runtimeEvent) { }
    void OnDocumentChanged(AppHandle app, Runtime.RuntimeDocumentChange change) { }
    void Dispose() { }
}

public sealed class PluginContext
{
    private readonly string _prefix;
    private readonly CommandRegistry _commands;
    private readonly string _name;
    private readonly ICollection<PermissionDefinition> _permissions;

    internal PluginContext(string name, CommandRegistry commands, ICollection<PermissionDefinition> permissions, JsonElement config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _prefix = $"plugin:{name}|";
        _name = name;
        _commands = commands;
        _permissions = permissions;
        Config = config;
    }

    /// <summary>The value of plugins[name], or JSON null when it is absent.</summary>
    public JsonElement Config { get; }

    /// <summary>Reads typed plugin configuration using generated NativeAOT serialization metadata.</summary>
    public T? ReadConfig<T>(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        try { return Config.Deserialize(typeInfo); }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"Error deserializing 'plugins.{_name}' within your Tauri configuration: {error.Message}", error);
        }
    }

    public void RegisterCommand(string name, CommandHandler handler) =>
        _commands.Register(_prefix + name, handler);

    public void RegisterResponseCommand(string name, ResponseCommandHandler handler) =>
        _commands.RegisterResponse(_prefix + name, handler);

    public void Permission(PermissionDefinition permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        var identifier = OwnIdentifier(permission.Identifier);
        string Qualify(string command)
        {
            if (command.StartsWith(_prefix, StringComparison.Ordinal)) return command;
            if (command.Contains(':') || command.Contains('|')) throw new ArgumentException("A plugin permission can reference only its own commands.", nameof(permission));
            return _prefix + command;
        }
        AddPermission(permission with
        {
            Identifier = identifier,
            Allow = permission.Allow.Select(Qualify).ToArray(),
            Deny = permission.Deny.Select(Qualify).ToArray()
        });
    }

    /// <summary>Defines a permission set. Unqualified members belong to this plugin; qualified members may refer to other plugins.</summary>
    public void PermissionSet(string identifier, params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var members = permissions.Select(value =>
        {
            _ = PermissionIdentifier.Parse(value);
            return value.Contains(':') ? value : $"{_name}:{value}";
        }).ToArray();
        AddPermission(new(OwnIdentifier(identifier), [], []) { References = members });
    }

    internal void Complete()
    {
        if (!_permissions.Any(permission => permission.Identifier == _name + ":default"))
            PermissionSet("default");
    }

    private string OwnIdentifier(string identifier)
    {
        _ = PermissionIdentifier.Parse(identifier);
        var qualified = identifier.Contains(':') ? identifier : $"{_name}:{identifier}";
        if (!qualified.StartsWith(_name + ":", StringComparison.Ordinal))
            throw new ArgumentException("A plugin can define permissions only in its own namespace.", nameof(identifier));
        return qualified;
    }

    private void AddPermission(PermissionDefinition permission)
    {
        var previous = _permissions.FirstOrDefault(value => value.Identifier == permission.Identifier);
        if (previous is not null)
        {
            // Tauri prefers sets at the top level and leaves when referenced inside
            // another set. The default set always takes precedence over a leaf.
            if (permission.References is not null)
                permission = permission with { NestedPermission = permission.Identifier == _name + ":default" ? null : previous.References is null ? previous : previous.NestedPermission };
            else if (previous.References is not null)
                permission = previous with { NestedPermission = permission.Identifier == _name + ":default" ? null : permission };
            _permissions.Remove(previous);
        }
        _permissions.Add(permission);
    }
}

public static class GeneratedPluginRegistry
{
    private static readonly object Gate = new();
    private static readonly List<Func<IPlugin>> Factories = [];
    private static readonly Dictionary<Type, Func<IPlugin>> TypedFactories = [];

    public static void Register<T>(Func<T> factory) where T : IPlugin
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate) TypedFactories.TryAdd(typeof(T), () => factory());
    }

    public static void Register(Func<IPlugin> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate) Factories.Add(factory);
    }

    internal static IPlugin[] CreateAll()
    {
        Func<IPlugin>[] factories;
        lock (Gate) factories = [.. TypedFactories.Values, .. Factories];
        return [.. factories.Select(static factory => factory())];
    }
}
