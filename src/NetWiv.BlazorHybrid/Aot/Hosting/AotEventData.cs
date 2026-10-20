// Adapted from ASP.NET Core WebEventData.cs, WebEventDescriptorReader.cs and
// EventHandlers.cs at 7413d24330774263a78a4f3837ca53493518455a (.NET 8.0.32).
// Licensed to the .NET Foundation under the MIT license.
#pragma warning disable BL0006 // This platform adapter uses the official event-field infrastructure.
using System.Text.Json;
using Microsoft.AspNetCore.Components.RenderTree;
using NetWiv.BlazorHybrid.Aot.Hosting.Events;

namespace NetWiv.BlazorHybrid.Aot.Hosting;

internal sealed record AotEventData(ulong HandlerId, EventFieldInfo? FieldInfo, EventArgs Arguments)
{
    internal static AotEventData Read(JsonElement descriptor, JsonElement arguments)
    {
        ulong id = 0;
        string? name = null;
        EventFieldInfo? field = null;
        foreach (var property in descriptor.EnumerateObject())
        {
            switch (property.Name)
            {
                case "eventHandlerId": id = property.Value.GetUInt64(); break;
                case "eventName": name = property.Value.GetString(); break;
                case "eventFieldInfo": field = ReadField(property.Value); break;
                default: throw new JsonException("Unknown event descriptor property: " + property.Name);
            }
        }
        return new(id, field, ReadArguments(name ?? throw new JsonException("Missing event name."), arguments));
    }

    private static EventFieldInfo? ReadField(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Null) return null;
        var result = new EventFieldInfo();
        foreach (var property in json.EnumerateObject())
        {
            switch (property.Name)
            {
                case "componentId": result.ComponentId = property.Value.GetInt32(); break;
                case "fieldValue": result.FieldValue = property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? property.Value.GetBoolean() : property.Value.GetString()!; break;
                default: throw new JsonException("Unknown event field property: " + property.Name);
            }
        }
        return result;
    }

    private static EventArgs ReadArguments(string name, JsonElement json) => name switch
    {
        "input" or "change" => ChangeEventArgsReader.Read(json),
        "copy" or "cut" or "paste" => ClipboardEventArgsReader.Read(json),
        "drag" or "dragend" or "dragenter" or "dragleave" or "dragover" or "dragstart" or "drop" => DragEventArgsReader.Read(json),
        "focus" or "blur" or "focusin" or "focusout" => FocusEventArgsReader.Read(json),
        "keydown" or "keyup" or "keypress" => KeyboardEventArgsReader.Read(json),
        "contextmenu" or "click" or "mouseover" or "mouseout" or "mousemove" or "mousedown" or "mouseup" or
            "dblclick" or "mouseenter" or "mouseleave" => MouseEventArgsReader.Read(json),
        "error" => ErrorEventArgsReader.Read(json),
        "loadstart" or "timeout" or "abort" or "load" or "loadend" or "progress" => ProgressEventArgsReader.Read(json),
        "touchcancel" or "touchend" or "touchmove" or "touchenter" or "touchleave" or "touchstart" => TouchEventArgsReader.Read(json),
        "gotpointercapture" or "lostpointercapture" or "pointercancel" or "pointerdown" or "pointerenter" or "pointerleave" or
            "pointermove" or "pointerout" or "pointerover" or "pointerup" => PointerEventArgsReader.Read(json),
        "wheel" or "mousewheel" => WheelEventArgsReader.Read(json),
        "invalid" or "reset" or "select" or "selectstart" or "selectionchange" or "submit" or
            "beforecopy" or "beforecut" or "beforepaste" or "canplay" or "canplaythrough" or "cuechange" or
            "durationchange" or "emptied" or "pause" or "play" or "playing" or "ratechange" or "seeked" or
            "seeking" or "stalled" or "stop" or "suspend" or "timeupdate" or "volumechange" or "waiting" or
            "activate" or "beforeactivate" or "beforedeactivate" or "deactivate" or "ended" or "fullscreenchange" or
            "fullscreenerror" or "loadeddata" or "loadedmetadata" or "pointerlockchange" or "pointerlockerror" or
            "readystatechange" or "scroll" or "toggle" or "cancel" or "close" => EventArgs.Empty,
        _ => throw new NotSupportedException($"Custom DOM event '{name}' is not supported by the NativeAOT host.")
    };
}
