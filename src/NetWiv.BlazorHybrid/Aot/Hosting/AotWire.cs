using System.Text;
using System.Text.Json;

namespace NetWiv.BlazorHybrid.Aot.Hosting;

// Fixed protocol envelopes never require a runtime-generated serializer.
internal static class AotWire
{
    internal static string Message(string type, Action<Utf8JsonWriter> writeArguments)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            writer.WriteStringValue(type);
            writeArguments(writer);
            writer.WriteEndArray();
        }
        return "__bwv:" + Encoding.UTF8.GetString(stream.ToArray());
    }
}
