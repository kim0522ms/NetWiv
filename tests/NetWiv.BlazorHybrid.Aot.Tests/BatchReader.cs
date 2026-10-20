namespace NetWiv.BlazorHybrid.Aot.Tests;

// Reads only the attribute and text frames consumed by these assertions. The
// fixed-width frame/string layout is the pinned 8.0.32 OutOfProcessRenderBatch.
internal sealed class BatchReader(byte[] data)
{
    internal List<(string? Name, ulong Handler)> Events { get; } = [];
    internal List<string?> Text { get; } = [];

    internal void Read()
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);
        stream.Position = data.Length - 16;
        var framesOffset = reader.ReadInt32();
        stream.Position = data.Length - 4;
        var stringTable = reader.ReadInt32();
        stream.Position = framesOffset;
        var count = reader.ReadInt32();
        for (var index = 0; index < count; index++)
        {
            stream.Position = framesOffset + 4 + index * 20;
            var frameType = reader.ReadInt32();
            if (frameType == 3) // Attribute
            {
                var nameIndex = reader.ReadInt32();
                reader.ReadInt32(); // Attribute string value is irrelevant to event dispatch.
                var handler = reader.ReadUInt64();
                if (handler != 0) Events.Add((ReadString(nameIndex), handler));
            }
            else if (frameType == 2) Text.Add(ReadString(reader.ReadInt32()));
        }

        string? ReadString(int index)
        {
            if (index == -1) return null;
            stream.Position = stringTable + index * 4;
            var offset = reader.ReadInt32();
            stream.Position = offset;
            return reader.ReadString();
        }
    }
}
