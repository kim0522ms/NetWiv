// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Pinned ASP.NET Core 8.0.32 source: 7413d24330774263a78a4f3837ca53493518455a.
#nullable enable

using System.Text.Json;

using Microsoft.AspNetCore.Components.Web;

namespace NetWiv.BlazorHybrid.Aot.Hosting.Events;

internal static class ClipboardEventArgsReader
{
    private static readonly JsonEncodedText TypeKey = JsonEncodedText.Encode("type");

    internal static ClipboardEventArgs Read(JsonElement jsonElement)
    {
        var eventArgs = new ClipboardEventArgs();
        foreach (var property in jsonElement.EnumerateObject())
        {
            if (property.NameEquals(TypeKey.EncodedUtf8Bytes))
            {
                eventArgs.Type = property.Value.GetString()!;
            }
            else
            {
                throw new JsonException($"Unknown property {property.Name}");
            }
        }

        return eventArgs;
    }
}
