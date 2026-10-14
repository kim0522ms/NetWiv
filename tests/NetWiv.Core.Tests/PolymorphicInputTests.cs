using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;

internal static class PolymorphicInputTests
{
    public static Task DerivedMembers()
    {
        var text = Read("""{"kind":"text","name":"item","text":"hello","next":{"kind":-7,"name":"nested","count":3}}""");
        Assert.True(text is PolyText { Text: "hello", Next: PolyNumber { Count: 3 } }, "Derived and nested variants deserialize");
        var number = Read("""{"kind":-7,"name":"item","count":4}""");
        Assert.True(number is PolyNumber { Count: 4 }, "Integer discriminator selects variant");
        Assert.Equal("text", JsonSerializer.SerializeToElement(text, PolymorphicInputContext.Default.PolyItem).GetProperty("kind").GetString());
        var nullable = CommandArgumentJson.Deserialize(JsonValue.Parse("null"), PolymorphicInputContext.Default.PolyItem, "poly", "value", true);
        Assert.True(nullable is null, "Nullable root accepts null");
        Reject("null", "invalid type: null");
        Reject("""{"kind":"text","name":"item"}""", "missing field `text`");
        Reject("""{"kind":"text","name":"item","text":null}""", "invalid type: null");
        Reject("""{"kind":-7,"name":"item","count":"bad"}""", "expected i32");
        Reject("""{"kind":-7,"name":"item","count":1.5}""", "expected i32");
        Reject("""{"kind":"text","name":"item","text":"ok","next":{"kind":-7,"name":"nested","count":null}}""", "invalid type: null");
        Reject("""{"kind":"text","name":null,"text":"ok"}""", "invalid type: null");
        var entry = CommandArgumentJson.Deserialize(JsonValue.Parse("""{"$type":1,"name":"interface"}"""),
            PolymorphicInputContext.Default.IPolyEntry, "poly", "value", false);
        Assert.True(entry is PolyEntry { Name: "interface" }, "Interface metadata selects concrete variant");
        Assert.Equal(1, JsonSerializer.SerializeToElement(entry, PolymorphicInputContext.Default.IPolyEntry).GetProperty("$type").GetInt32());
        Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(JsonValue.Parse("""{"$type":1,"name":null}"""),
            PolymorphicInputContext.Default.IPolyEntry, "poly", "value", false));
        return Task.CompletedTask;
    }

    public static Task DiscriminatorErrors()
    {
        foreach (var json in new[] {
            "{}", """{"kind":"unknown"}""", """{"kind":"-7"}""", """{"kind":true}""",
            """{"kind":null}""", """{"kind":1.5}""", """{"kind":2147483648}""",
            """{"kind":"text","kind":-7,"name":"item","text":"ok","count":3}""",
            """{"name":"item","kind":"text","text":"ok"}"""
        })
        {
            try { JsonSerializer.Deserialize(json, PolymorphicInputContext.Default.PolyItem); }
            catch (Exception error) when (error is JsonException or NotSupportedException)
            {
                Assert.Throws<InvokeException>(() => Read(json));
                continue;
            }
            throw new InvalidOperationException("STJ unexpectedly accepted invalid discriminator input");
        }
        // Unknown discriminator fallback is not exposed by the bindings, but Core
        // continues to respect explicit STJ metadata used without generated bindings.
        var fallback = CommandArgumentJson.Deserialize(JsonValue.Parse("""{"$type":"unknown","value":3}"""),
            PolymorphicInputContext.Default.PolyFallback, "poly", "value", false);
        Assert.Equal(3, fallback.Value);
        return Task.CompletedTask;
    }

    private static PolyItem Read(string json) => CommandArgumentJson.Deserialize(JsonValue.Parse(json),
        PolymorphicInputContext.Default.PolyItem, "poly", "value", false);

    private static void Reject(string json, string reason)
    {
        try { Read(json); }
        catch (InvokeException error) { Assert.Contains(reason, error.Message); return; }
        throw new InvalidOperationException("Expected polymorphic input rejection");
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PolyText), "text")]
[JsonDerivedType(typeof(PolyNumber), -7)]
internal abstract class PolyItem { public required string Name { get; set; } }
internal sealed class PolyText : PolyItem
{
    public required string Text { get; set; }
    public PolyItem? Next { get; set; }
}
internal sealed class PolyNumber : PolyItem { public required int Count { get; set; } }

[JsonPolymorphic, JsonDerivedType(typeof(PolyEntry), 1)]
internal interface IPolyEntry { string Name { get; } }
internal sealed class PolyEntry : IPolyEntry { public required string Name { get; set; } }

[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(PolyFallbackDerived), "derived")]
internal class PolyFallback { public int Value { get; set; } }
internal sealed class PolyFallbackDerived : PolyFallback;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PolyItem))]
[JsonSerializable(typeof(IPolyEntry))]
[JsonSerializable(typeof(PolyFallback))]
internal partial class PolymorphicInputContext : JsonSerializerContext;
