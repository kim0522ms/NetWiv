using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace NetWiv.BlazorHybrid.Shared;

public sealed class UiInteropRequest
{
    public string Name { get; set; } = "";
    public UiInteropItem?[] Items { get; set; } = [];
}

public sealed class UiInteropItem
{
    public string Text { get; set; } = "";
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public long Value { get; set; }
}

public sealed class UiInteropReply
{
    public string Message { get; set; } = "";
    public UiInteropItem?[] Items { get; set; } = [];
    public string? Optional { get; set; }
}

public sealed class UiInteropCallbacks
{
    [JSInvokable]
    public async Task<UiInteropReply> DescribeAsync(UiInteropRequest request)
    {
        await Task.Yield();
        return new() { Message = "Instance callback: " + request.Name, Items = request.Items, Optional = null };
    }
}

public interface IUiInteropReference : IDisposable
{
    object Value { get; }
}

public interface IUiInteropReferenceFactory
{
    IUiInteropReference Create(UiInteropCallbacks callbacks);
}

public sealed class BrowserUiInteropReferenceFactory : IUiInteropReferenceFactory
{
    public IUiInteropReference Create(UiInteropCallbacks callbacks) => new Reference(DotNetObjectReference.Create(callbacks));

    private sealed class Reference(DotNetObjectReference<UiInteropCallbacks> reference) : IUiInteropReference
    {
        public object Value => reference;
        public void Dispose() => reference.Dispose();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UiInteropRequest))]
[JsonSerializable(typeof(UiInteropReply))]
public partial class UiInteropJsonContext : JsonSerializerContext;
