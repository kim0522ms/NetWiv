using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using NetWiv.BlazorHybrid.Aot.Hosting;

internal static class AotEventDataTests
{
    internal static Task BuiltInEvents()
    {
        // Reflection is confined to this managed test: the official attribute
        // list is the independent reference for the adapter's event mappings.
        var handlers = typeof(EventHandlers).GetCustomAttributes<EventHandlerAttribute>().ToArray();
        Check.True(handlers.Length > 0, "The framework built-in event list was empty.");
        using var arguments = JsonDocument.Parse("{}");
        foreach (var handler in handlers)
        {
            Check.True(handler.AttributeName.StartsWith("on", StringComparison.Ordinal), "An event attribute has an unexpected name.");
            var name = handler.AttributeName[2..];
            using var descriptor = JsonDocument.Parse(
                "{\"eventHandlerId\":1,\"eventName\":\"" + name + "\",\"eventFieldInfo\":null}");
            var parsed = AotEventData.Read(descriptor.RootElement, arguments.RootElement);
            Check.True(parsed.Arguments.GetType() == handler.EventArgsType,
                $"Built-in event '{name}' did not parse as {handler.EventArgsType.Name}.");
        }

        foreach (var name in new[] { "compositionstart", "compositionupdate", "compositionend" })
        {
            Check.True(!handlers.Any(handler => handler.AttributeName == "on" + name),
                $"The framework now declares '{name}' as a built-in event; update the pinned adapter contract.");
            using var descriptor = JsonDocument.Parse(
                "{\"eventHandlerId\":1,\"eventName\":\"" + name + "\",\"eventFieldInfo\":null}");
            Check.Throws<NotSupportedException>(() => AotEventData.Read(descriptor.RootElement, arguments.RootElement));
        }
        return Task.CompletedTask;
    }
}
