// Copyright 2019-2024 Tauri Programme within The Commons Conservancy
// SPDX-License-Identifier: Apache-2.0 OR MIT
using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Runtime;

internal static class InvokeResponseTests
{
    internal static Task DeserializeInvokeResponseBody()
    {
        var json = RuntimeInvokeResponse.FromJson(JsonValue.Parse("[1, 123, 1231]"));
        Assert.Equal("1,123,1231", string.Join(',', json.Deserialize(ResponseJson.Default.UInt16Array)!));
        json = RuntimeInvokeResponse.FromJson(JsonValue.From("string value"));
        Assert.Equal("string value", json.Deserialize(ResponseJson.Default.String));
        Assert.Throws<JsonException>(() => json.Deserialize(ResponseJson.Default.UInt16Array));
        byte[] values = [1, 2, 3, 4, 5, 6, 1];
        var raw = RuntimeInvokeResponse.FromBinary(values);
        Assert.Equal(string.Join(',', values), string.Join(',', raw.Deserialize(ResponseJson.Default.ByteArray)!));
        return Task.CompletedTask;
    }

    internal static Task BinaryAndJsonBoundaries()
    {
        byte[] values = [0, 127, 255];
        var raw = RuntimeInvokeResponse.FromBinary(values);
        var decoded = raw.Deserialize(ResponseJson.Default.ByteArray)!;
        decoded[0] = 99;
        Assert.Equal((byte)0, values[0]);
        Assert.Equal("0,127,255", string.Join(',', raw.Deserialize(ResponseJson.Default.UInt16Array)!));
        Assert.Throws<JsonException>(() => raw.Deserialize(ResponseJson.Default.String));
        Assert.Equal(0, RuntimeInvokeResponse.FromBinary(ReadOnlyMemory<byte>.Empty).Deserialize(ResponseJson.Default.ByteArray)!.Length);
        var json = RuntimeInvokeResponse.FromJson(JsonValue.Parse("[0, 127, 255]"));
        Assert.Equal("0,127,255", string.Join(',', json.Deserialize(ResponseJson.Default.ByteArray)!));
        foreach (var invalid in new[] { "[-1]", "[256]", "[1.5]", "[null]", "[\"1\"]", "null", "\"AA==\"", "{}" })
            Assert.Throws<JsonException>(() => RuntimeInvokeResponse.FromJson(JsonValue.Parse(invalid)).Deserialize(ResponseJson.Default.ByteArray));
        return Task.CompletedTask;
    }
}

[JsonSerializable(typeof(ushort[]))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(string))]
internal partial class ResponseJson : JsonSerializerContext;
