using System.Text.Json.Serialization;
using NetWiv;
using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class PolymorphicCommandTests
{
    [Fact]
    public async Task GeneratedCommandRoundtripsTaggedVariantsAndNullableInputs()
    {
        using var app = new Builder().UseRuntime(new RecordingRuntime()).InvokeHandler("polymorphic_echo").Build();
        foreach (var input in new[] {
            """{"kind":"text","name":"item","text":"hello","note":null}""",
            """{"kind":-7,"name":"item","count":4}""", "null"
        })
        {
            var result = await app.Handle.InvokeAsync("polymorphic_echo", JsonValue.Parse("{\"value\":" + input + "}"));
            Assert.True(System.Text.Json.JsonElement.DeepEquals(JsonValue.Parse(input), result));
        }
    }

    [Theory]
    [InlineData("{\"kind\":\"text\",\"name\":\"item\"}", "missing field `text`")]
    [InlineData("{\"kind\":\"text\",\"name\":\"item\",\"text\":null}", "invalid type: null")]
    [InlineData("{\"kind\":-7,\"name\":\"item\",\"count\":1.5}", "expected i32")]
    [InlineData("{\"kind\":\"unknown\"}", "invalid args")]
    [InlineData("{}", "invalid args")]
    public async Task DerivedFailuresRejectInvocationAndPermitNextValidCall(string input, string reason)
    {
        using var app = new Builder().UseRuntime(new RecordingRuntime()).InvokeHandler("polymorphic_echo").Build();
        var error = await Assert.ThrowsAsync<InvokeException>(() =>
            app.Handle.InvokeAsync("polymorphic_echo", JsonValue.Parse("{\"value\":" + input + "}")).AsTask());
        Assert.Contains(reason, error.Message);
        var result = await app.Handle.InvokeAsync("polymorphic_echo", JsonValue.Parse("""{"value":{"kind":-7,"name":"item","count":3}}"""));
        Assert.Equal(3, result.GetProperty("count").GetInt32());
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(CommandPolyText), "text")]
[JsonDerivedType(typeof(CommandPolyNumber), -7)]
public abstract class CommandPolyItem { public required string Name { get; set; } }
public sealed class CommandPolyText : CommandPolyItem
{
    public required string Text { get; set; }
    public string? Note { get; set; }
}
public sealed class CommandPolyNumber : CommandPolyItem { public required int Count { get; set; } }
public static class PolymorphicCommands
{
    [Command("polymorphic_echo")] public static CommandPolyItem? Echo(CommandPolyItem? value) => value;
}
