using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace NetWiv.BlazorHybrid.Aot;

/// <summary>Registers concrete component types and their constructors for NativeAOT hosting.</summary>
/// <remarks>Register every component used by the UI, including closed generic component types.</remarks>
public sealed class BlazorAotComponentRegistry
{
    private readonly Dictionary<Type, Func<IServiceProvider, IComponent>> _factories;
    private readonly bool _readOnly;

    public BlazorAotComponentRegistry() => _factories = [];

    private BlazorAotComponentRegistry(Dictionary<Type, Func<IServiceProvider, IComponent>> factories)
    {
        _factories = factories;
        _readOnly = true;
    }

    /// <summary>Preserves a component's metadata and registers its parameterless constructor.</summary>
    public BlazorAotComponentRegistry Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>()
        where TComponent : IComponent, new() => Register<TComponent>(static _ => new TComponent());

    /// <summary>Preserves a component's metadata and registers an explicit constructor using page-scoped services.</summary>
    public BlazorAotComponentRegistry Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        Func<IServiceProvider, TComponent> factory) where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (_readOnly) throw new InvalidOperationException("The NativeAOT component registration snapshot is read-only.");
        if (typeof(TComponent) == typeof(Router))
            throw new NotSupportedException("The framework Router's assembly discovery is not supported by the NativeAOT host. Use explicitly registered components and NavigationManager for static routing.");
        if (!_factories.TryAdd(typeof(TComponent), services => factory(services)))
            throw new ArgumentException($"Component '{typeof(TComponent)}' is already registered.", nameof(factory));
        return this;
    }

    internal BlazorAotComponentRegistry Snapshot() => new(new(_factories));
    internal bool Contains(Type componentType) => _factories.ContainsKey(componentType);
    internal IComponentActivator CreateActivator(IServiceProvider services) => new Activator(this, services);

    private sealed class Activator(BlazorAotComponentRegistry registry, IServiceProvider services) : IComponentActivator
    {
        public IComponent CreateInstance([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type componentType) =>
            registry._factories.TryGetValue(componentType, out var factory)
                ? factory(services)
                : throw new NotSupportedException($"Component '{componentType}' is not registered. Register its concrete type with BlazorAotOptions.Components.Register<TComponent>().");
    }
}
