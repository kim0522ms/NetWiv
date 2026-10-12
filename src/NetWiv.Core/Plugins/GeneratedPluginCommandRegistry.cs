namespace NetWiv;

/// <summary>Type-scoped binding storage for generated plugin commands. Registration does not expose commands.</summary>
public static class GeneratedPluginCommandRegistry
{
    private static class Commands<TPlugin> where TPlugin : IPlugin
    {
        internal static readonly Dictionary<string, CommandHandler> Handlers = new(StringComparer.Ordinal);
    }

    /// <summary>Used by generated module initializers; no runtime method discovery is performed.</summary>
    public static void Register<TPlugin>(string name, CommandHandler handler) where TPlugin : IPlugin
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        if (name.Contains(':') || name.Contains('|'))
            throw new ArgumentException("Generated plugin commands require local names without ':' or '|'.", nameof(name));
        var handlers = Commands<TPlugin>.Handlers;
        lock (handlers)
        {
            if (!handlers.TryAdd(name, handler))
                throw new InvalidOperationException($"A generated command named '{name}' is already registered for '{typeof(TPlugin)}'.");
        }
    }

    internal static void ApplyTo<TPlugin>(PluginContext context, string[] names) where TPlugin : IPlugin
    {
        ArgumentNullException.ThrowIfNull(names);
        var handlers = Commands<TPlugin>.Handlers;
        var selected = new CommandHandler[names.Length];
        lock (handlers)
        {
            for (var i = 0; i < names.Length; i++)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(names[i]);
                if (!handlers.TryGetValue(names[i], out var handler))
                    throw new InvalidOperationException($"Generated plugin command '{names[i]}' was not found on '{typeof(TPlugin)}'. Use its local [Command] name.");
                selected[i] = handler;
            }
        }
        for (var i = 0; i < names.Length; i++) context.RegisterCommand(names[i], selected[i]);
    }
}
