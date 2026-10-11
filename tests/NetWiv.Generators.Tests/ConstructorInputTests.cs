using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NetWiv;
using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class ConstructorInputTests
{
    [Fact]
    public void RequiredFieldsFollowTheSourceGeneratedConstructorSelection()
    {
        Check(IntegrationJsonContext.Default.SingleConstructorDto, true);
        Check(IntegrationJsonContext.Default.PreferredDefaultDto, false);
        Check(IntegrationJsonContext.Default.DefaultStructDto, false);
        Check(IntegrationJsonContext.Default.MarkedStructDto, true);

        static void Check<T>(JsonTypeInfo<T> info, bool required)
        {
            using var empty = JsonDocument.Parse("{}");
            var property = Assert.Single(info.Properties, property => property.Name == "name");
            Assert.Equal(required, property.AssociatedParameter is { HasDefaultValue: false, IsNullable: false });
            if (required)
                Assert.Throws<InvokeException>(() => CommandArgumentJson.Deserialize(empty.RootElement, info, "ctor", "input", false));
            else
                CommandArgumentJson.Deserialize(empty.RootElement, info, "ctor", "input", false);
            using var present = JsonDocument.Parse("""{"name":"Ada"}""");
            var result = CommandArgumentJson.Deserialize(present.RootElement, info, "ctor", "input", false);
            Assert.Equal("Ada", JsonSerializer.SerializeToElement(result, info).GetProperty("name").GetString());
        }
    }

    [Fact]
    public async Task RecordRequiredNullableAndDefaultFieldsRunThroughGeneratedInvocation()
    {
        using var app = new Builder().UseRuntime(new RecordingRuntime()).InvokeHandler("constructor_record").Build();
        using var missing = JsonDocument.Parse("""{"input":{"count":2}}""");
        var error = await Assert.ThrowsAsync<InvokeException>(() => app.Handle.InvokeAsync("constructor_record", missing.RootElement).AsTask());
        Assert.Contains("missing field `name`", error.Message);
        using var valid = JsonDocument.Parse("""{"input":{"name":"Ada","count":2}}""");
        var result = await app.Handle.InvokeAsync("constructor_record", valid.RootElement);
        Assert.Equal("Ada", result.GetProperty("name").GetString());
        Assert.Equal(2, result.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("note").ValueKind);
        Assert.Equal(3, result.GetProperty("retry").GetInt32());
    }
}

public sealed record ConstructorRecord(string Name, int Count, string? Note = null, int Retry = 3);
public sealed class SingleConstructorDto(string name) { public string Name { get; } = name; }
public sealed class PreferredDefaultDto
{
    public string Name { get; set; } = "";
    public PreferredDefaultDto() { }
    public PreferredDefaultDto(string name) => Name = name;
}
public struct DefaultStructDto { public string Name { get; set; } public DefaultStructDto(string name) => Name = name; }
public struct MarkedStructDto { public string Name { get; set; } [JsonConstructor] public MarkedStructDto(string name) => Name = name; }

public static class ConstructorCommands
{
    [Command("constructor_record")]
    public static ConstructorRecord Echo(ConstructorRecord input) => input;
}
