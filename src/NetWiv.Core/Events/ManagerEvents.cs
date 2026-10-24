using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using NetWiv.Runtime;

namespace NetWiv;

public sealed partial class App
{
    public IDisposable Listen(string eventName, Action<AppEvent> handler) => Handle.Listen(eventName, handler);
    public IDisposable Once(string eventName, Action<AppEvent> handler) => Handle.Once(eventName, handler);
    public IDisposable ListenAny(string eventName, Action<AppEvent> handler) => Handle.ListenAny(eventName, handler);
    public IDisposable OnceAny(string eventName, Action<AppEvent> handler) => Handle.OnceAny(eventName, handler);
    public void Unlisten(long id) => Handle.Unlisten(id);
    public void Emit(string eventName, JsonElement payload) => Handle.Emit(eventName, payload);
    public void Emit<T>(string eventName, T payload, JsonTypeInfo<T> typeInfo) => Handle.Emit(eventName, payload, typeInfo);
    public void EmitTo(RuntimeEventTarget target, string eventName, JsonElement payload) => Handle.EmitTo(target, eventName, payload);
    public void EmitTo<T>(RuntimeEventTarget target, string eventName, T payload, JsonTypeInfo<T> typeInfo) => Handle.EmitTo(target, eventName, payload, typeInfo);
    public void EmitTo(string label, string eventName, JsonElement payload) => Handle.EmitTo(label, eventName, payload);
    public void EmitTo<T>(string label, string eventName, T payload, JsonTypeInfo<T> typeInfo) => Handle.EmitTo(label, eventName, payload, typeInfo);
}

public sealed partial class AppHandle
{
    public void Emit<T>(string eventName, T payload, JsonTypeInfo<T> typeInfo) =>
        Emit(eventName, JsonSerializer.SerializeToElement(payload, typeInfo));
    public void EmitTo<T>(RuntimeEventTarget target, string eventName, T payload, JsonTypeInfo<T> typeInfo) =>
        EmitTo(target, eventName, JsonSerializer.SerializeToElement(payload, typeInfo));
    public void EmitTo<T>(string label, string eventName, T payload, JsonTypeInfo<T> typeInfo) =>
        EmitTo(label, eventName, JsonSerializer.SerializeToElement(payload, typeInfo));

    public IDisposable Listen(string eventName, Action<AppEvent> handler) => Events.Listen(eventName, RuntimeEventTarget.App, handler);
    public IDisposable Once(string eventName, Action<AppEvent> handler) => Events.Once(eventName, RuntimeEventTarget.App, handler);
    public IDisposable ListenAny(string eventName, Action<AppEvent> handler) => Events.Listen(eventName, handler);
    public IDisposable OnceAny(string eventName, Action<AppEvent> handler) => Events.Once(eventName, handler);
    public void Unlisten(long id) => Events.Unlisten(id);
    public void EmitTo(RuntimeEventTarget target, string eventName, JsonElement payload) => Emit(eventName, payload, target);
    public void EmitTo(string label, string eventName, JsonElement payload) => Emit(eventName, payload, new RuntimeEventTarget("AnyLabel", label));
}

public partial class Window
{
    public IDisposable ListenAny(string eventName, Action<AppEvent> handler) => App.ListenAny(eventName, handler);
    public IDisposable OnceAny(string eventName, Action<AppEvent> handler) => App.OnceAny(eventName, handler);
    public void Unlisten(long id) => App.Unlisten(id);
    public void Emit(string eventName, JsonElement payload) => App.Emit(eventName, payload);
    public void Emit<T>(string eventName, T payload, JsonTypeInfo<T> typeInfo) => App.Emit(eventName, payload, typeInfo);
    public void EmitTo(RuntimeEventTarget target, string eventName, JsonElement payload) => App.EmitTo(target, eventName, payload);
    public void EmitTo<T>(RuntimeEventTarget target, string eventName, T payload, JsonTypeInfo<T> typeInfo) => App.EmitTo(target, eventName, payload, typeInfo);
    public void EmitTo(string label, string eventName, JsonElement payload) => App.EmitTo(label, eventName, payload);
    public void EmitTo<T>(string label, string eventName, T payload, JsonTypeInfo<T> typeInfo) => App.EmitTo(label, eventName, payload, typeInfo);
    public Task EmitToAsync(string label, string eventName, JsonElement payload)
    {
        EmitTo(label, eventName, payload);
        return Task.CompletedTask;
    }
}

public partial class Webview
{
    public IDisposable ListenAny(string eventName, Action<AppEvent> handler) => App.ListenAny(eventName, handler);
    public IDisposable OnceAny(string eventName, Action<AppEvent> handler) => App.OnceAny(eventName, handler);
    public void Unlisten(long id) => App.Unlisten(id);
    public void Emit(string eventName, JsonElement payload) => App.Emit(eventName, payload);
    public void Emit<T>(string eventName, T payload, JsonTypeInfo<T> typeInfo) => App.Emit(eventName, payload, typeInfo);
    public void EmitTo(RuntimeEventTarget target, string eventName, JsonElement payload) => App.EmitTo(target, eventName, payload);
    public void EmitTo<T>(RuntimeEventTarget target, string eventName, T payload, JsonTypeInfo<T> typeInfo) => App.EmitTo(target, eventName, payload, typeInfo);
    public void EmitTo(string label, string eventName, JsonElement payload) => App.EmitTo(label, eventName, payload);
    public void EmitTo<T>(string label, string eventName, T payload, JsonTypeInfo<T> typeInfo) => App.EmitTo(label, eventName, payload, typeInfo);
    public Task EmitToAsync(string label, string eventName, JsonElement payload)
    {
        EmitTo(label, eventName, payload);
        return Task.CompletedTask;
    }
}
