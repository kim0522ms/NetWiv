using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NetWiv;

internal static class CommandNumberTests
{
    internal static Task FormatOracle()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ulong bits = 0xa57c91e34b26d80f;
        for (int index = 0; index < 100_000; index++)
        {
            bits ^= bits << 13;
            bits ^= bits >> 7;
            bits ^= bits << 17;
            var value = BitConverter.UInt64BitsToDouble(bits);
            if (!double.IsFinite(value)) continue;
            var row = bits.ToString("x16", CultureInfo.InvariantCulture) + "\tinvalid type: floating point `" +
                SerdeJsonNumber.Format(value) + "`, expected u8\n";
            hash.AppendData(Encoding.UTF8.GetBytes(row));
        }
        // --format-oracle in compatibility/oracles/command-numbers produces the
        // same deterministic bit corpus using the pinned serde_json formatter.
        Assert.Equal("8c0bf850d4341827952227d8faaf82df993b78711a7ddd727e0f240cd075fa81",
            Convert.ToHexStringLower(hash.GetHashAndReset()));
        return Task.CompletedTask;
    }

    internal static Task PinnedArguments()
    {
        using var stream = typeof(CommandNumberTests).Assembly.GetManifestResourceStream("netwiv.tests.command-numbers.json")!;
        using var oracle = JsonDocument.Parse(stream);
        var failures = new List<string>();
        foreach (var vector in oracle.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var input = vector.GetProperty("input").GetString()!;
            var results = vector.GetProperty("results");
            Check(input, results, "byte", NumericJson.Default.Byte, failures);
            Check(input, results, "sbyte", NumericJson.Default.SByte, failures);
            Check(input, results, "short", NumericJson.Default.Int16, failures);
            Check(input, results, "ushort", NumericJson.Default.UInt16, failures);
            Check(input, results, "int", NumericJson.Default.Int32, failures);
            Check(input, results, "uint", NumericJson.Default.UInt32, failures);
            Check(input, results, "long", NumericJson.Default.Int64, failures);
            Check(input, results, "ulong", NumericJson.Default.UInt64, failures);
            Check(input, results, "float", NumericJson.Default.Single, failures);
            Check(input, results, "double", NumericJson.Default.Double, failures);
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join('\n', failures));
        return Task.CompletedTask;
    }

    private static void Check<T>(string input, JsonElement results, string type, JsonTypeInfo<T> info, List<string> failures)
    {
        var expected = results.GetProperty(type);
        try
        {
            var value = CommandArgumentJson.Deserialize(JsonValue.Parse(input), info, "numeric", "value", false);
            if (expected.TryGetProperty("error", out var error))
                throw new InvalidOperationException($"accepted value; expected {error.GetString()}");
            var json = expected.GetProperty("ok").GetString()!;
            if (json == "null")
            {
                if (!(value is float single && !float.IsFinite(single) || value is double number && !double.IsFinite(number)))
                    throw new InvalidOperationException("expected a non-finite floating-point result");
            }
            else Assert.Equal(JsonSerializer.Deserialize(json, info), value);
        }
        catch (InvokeException error)
        {
            var expectedError = expected.TryGetProperty("error", out var message) ? message.GetString() : "<success>";
            if (error.Message != $"invalid args `value` for command `numeric`: {expectedError}")
                failures.Add($"{type}({input}): expected {expectedError}; got {error.Message}");
        }
        catch (Exception error) { failures.Add($"{type}({input}): {error.Message}"); }
    }
}

[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
internal partial class NumericJson : JsonSerializerContext;
