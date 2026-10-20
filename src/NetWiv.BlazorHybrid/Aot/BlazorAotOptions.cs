using System.Text.Json.Serialization.Metadata;

namespace NetWiv.BlazorHybrid.Aot;

/// <summary>Statically declares the components and interop contract of a Hybrid application.</summary>
public sealed class BlazorAotOptions
{
    public BlazorAotComponentRegistry Components { get; private init; } = new();
    public BlazorAotInteropRegistry Interop { get; private init; } = new();

    /// <summary>
    /// Source-generated metadata for application DTOs. Built-in transport types are supplied
    /// by NetWiv. Unregistered types fail before a JavaScript call is sent.
    /// </summary>
    public IJsonTypeInfoResolver? JsonTypeInfoResolver { get; set; }

    internal BlazorAotOptions Snapshot() => new()
    {
        Components = Components.Snapshot(),
        Interop = Interop.Snapshot(),
        JsonTypeInfoResolver = JsonTypeInfoResolver
    };
}
