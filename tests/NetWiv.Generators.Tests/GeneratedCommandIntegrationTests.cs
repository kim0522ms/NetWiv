using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;
using NetWiv.Runtime;
using Xunit;

[assembly: CommandJsonContext(typeof(NetWiv.Generators.Tests.IntegrationJsonContext))]

namespace NetWiv.Generators.Tests;

public sealed class GeneratedCommandIntegrationTests
{
    [Fact]
    public async Task AnnotatedCommandsAreUnavailableUntilSelected()
    {
        var runtime = new RecordingRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();

        var exception = await Assert.ThrowsAsync<InvokeException>(() =>
            app.Handle.InvokeAsync("reverse_bytes", Parse("{\"bytes\":[1]}")).AsTask());

        Assert.Equal("Command reverse_bytes not found", exception.Message);
    }

    [Fact]
    public async Task AnnotatedPluginIsRegisteredWithoutReflection()
    {
        var runtime = new RecordingRuntime();
        using var app = new Builder().UseRuntime(runtime).Build();

        var result = await app.Handle.InvokeAsync("plugin:integration|ping", JsonValue.EmptyObject);

        Assert.Equal("pong", result.GetString());
    }

    [Fact]
    public async Task BindsNestedDtoCollectionsEnumNullableAndState()
    {
        using var app = CreateApp(out _);
        var arguments = Parse("""
            {
              "request": {
                "person": { "name": "Ada" },
                "mode": "loud",
                "note": null,
                "tags": ["compiler", "aot"],
                "scores": { "correctness": 10 }
              }
            }
            """);

        var result = await app.Handle.InvokeAsync("transform_complex", arguments);

        Assert.Equal("ADA:2:10", result.GetProperty("summary").GetString());
        Assert.Equal(1, result.GetProperty("invocation").GetInt32());
    }

    [Fact]
    public async Task InvalidPayloadRejectsOnlyTheInvocation()
    {
        using var app = CreateApp(out _);

        var invalid = await Assert.ThrowsAsync<InvokeException>(() =>
            app.Handle.InvokeAsync("transform_complex", Parse("{\"request\":false}")).AsTask());
        Assert.StartsWith("invalid args `request` for command `transform_complex`:", invalid.Message, StringComparison.Ordinal);

        var valid = Parse("""
            {"request":{"person":{"name":"Grace"},"mode":"quiet","tags":[],"scores":{}}}
            """);
        var result = await app.Handle.InvokeAsync("transform_complex", valid);
        Assert.Equal("grace:0:0", result.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task MissingNullAndExtraArgumentsHaveStableInvocationBehavior()
    {
        using var app = CreateApp(out _);

        var missing = await Assert.ThrowsAsync<InvokeException>(() =>
            app.Handle.InvokeAsync("transform_complex", JsonValue.EmptyObject).AsTask());
        Assert.Equal("invalid args `request` for command `transform_complex`: command transform_complex missing required key request", missing.Message);

        var nullValue = await Assert.ThrowsAsync<InvokeException>(() =>
            app.Handle.InvokeAsync("transform_complex", Parse("{\"request\":null}")).AsTask());
        Assert.Equal("invalid args `request` for command `transform_complex`: invalid type: null, expected struct ComplexRequest", nullValue.Message);

        var withExtra = Parse("""
            {"request":{"person":{"name":"Lin"},"mode":"quiet","tags":[],"scores":{}},"ignored":true}
            """);
        var result = await app.Handle.InvokeAsync("transform_complex", withExtra);
        Assert.Equal("lin:0:0", result.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task AsyncExceptionPropagatesAsInvocationFailure()
    {
        using var app = CreateApp(out _);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.Handle.InvokeAsync("fail_async", JsonValue.EmptyObject).AsTask());

        Assert.Equal("expected async failure", exception.Message);
    }

    [Theory]
    [InlineData("null", "invalid type: null, expected struct TypedRequest")]
    [InlineData("\"wrong\"", "invalid type: string \"wrong\", expected struct TypedRequest")]
    [InlineData("{\"message\":1,\"count\":\"two\"}", "invalid type: string \"two\", expected u32")]
    [InlineData("{\"message\":\"ok\"}", "missing field `count`")]
    public async Task DtoErrorsMatchPinnedSerdeFixtures(string request, string expected)
    {
        using var app = new Builder().UseRuntime(new RecordingRuntime()).InvokeHandler("typed_request").Build();
        var error = await Assert.ThrowsAsync<InvokeException>(() =>
            app.Handle.InvokeAsync("typed_request", Parse("{\"request\":" + request + "}")).AsTask());
        Assert.Equal("invalid args `request` for command `typed_request`: " + expected, error.Message);
    }

    [Fact]
    public async Task JsonElementIsClonedAndGenericChannelUsesTypedMetadata()
    {
        using var app = CreateApp(out var runtime);
        using var inputDocument = JsonDocument.Parse("{\"value\":42}");
        var echoArgs = JsonValue.Parse($"{{\"value\":{inputDocument.RootElement.GetRawText()}}}");

        var echoed = await app.Handle.InvokeAsync("echo_element", echoArgs);
        inputDocument.Dispose();
        Assert.Equal(42, echoed.GetProperty("value").GetInt32());

        await app.Handle.InvokeAsync(
            "send_progress",
            Parse("{\"updates\":\"__CHANNEL__:42\"}"));
        Assert.Contains("runCallback(42", runtime.LastScript, StringComparison.Ordinal);
        Assert.Contains("half", runtime.LastScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ByteArraysUseTauriNumberArrayWireShape()
    {
        using var app = CreateApp(out _);

        var result = await app.Handle.InvokeAsync("reverse_bytes", Parse("{\"bytes\":[0,127,255] }"));

        Assert.True(JsonElement.DeepEquals(Parse("[255,127,0]"), result));
        var invalid = await Assert.ThrowsAsync<InvokeException>(() =>
            app.Handle.InvokeAsync("reverse_bytes", Parse("{\"bytes\":\"AP8=\"}")).AsTask());
        Assert.Equal("invalid args `bytes` for command `reverse_bytes`: invalid type, expected a byte array", invalid.Message);
    }

    private static App CreateApp(out RecordingRuntime runtime)
    {
        runtime = new RecordingRuntime();
        return new Builder()
            .UseRuntime(runtime)
            .Manage(new IntegrationCounter())
            .InvokeHandler("transform_complex", "fail_async", "echo_element", "send_progress", "reverse_bytes")
            .Build();
    }

    private static JsonElement Parse(string json) => JsonValue.Parse(json);
}

public sealed class IntegrationCounter
{
    private int _count;
    public int Next() => Interlocked.Increment(ref _count);
}

public sealed record Person(string Name);

[JsonConverter(typeof(JsonStringEnumConverter<SampleMode>))]
public enum SampleMode
{
    Quiet,
    Loud,
}

public sealed record ComplexRequest(
    Person Person,
    SampleMode Mode,
    string? Note,
    string[] Tags,
    Dictionary<string, int> Scores);

public sealed record ComplexResponse(string Summary, int Invocation);

public sealed record ProgressMessage(string Stage, int Percent);
public sealed record TypedRequest(string Message, uint Count);

public static class IntegrationCommands
{
    [Command("typed_request")]
    public static TypedRequest TypedRequest(TypedRequest request) => request;
    [Command]
    public static async Task<ComplexResponse> TransformComplex(
        ComplexRequest request,
        State<IntegrationCounter> counter)
    {
        await Task.Yield();
        var name = request.Mode == SampleMode.Loud
            ? request.Person.Name.ToUpperInvariant()
            : request.Person.Name.ToLowerInvariant();
        return new ComplexResponse(
            $"{name}:{request.Tags.Length}:{request.Scores.Values.Sum()}",
            counter.Value.Next());
    }

    [Command]
    public static async Task FailAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("expected async failure");
    }

    [Command]
    public static JsonElement EchoElement(JsonElement value) => value;

    [Command]
    public static void SendProgress(Channel<ProgressMessage> updates) =>
        updates.Send(new ProgressMessage("half", 50));

    [Command]
    public static byte[] ReverseBytes(byte[] bytes) => [.. bytes.Reverse()];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(ComplexRequest))]
[JsonSerializable(typeof(ComplexResponse))]
[JsonSerializable(typeof(ProgressMessage))]
[JsonSerializable(typeof(TypedRequest))]
[JsonSerializable(typeof(ConstructorRecord))]
[JsonSerializable(typeof(SingleConstructorDto))]
[JsonSerializable(typeof(PreferredDefaultDto))]
[JsonSerializable(typeof(DefaultStructDto))]
[JsonSerializable(typeof(MarkedStructDto))]
[JsonSerializable(typeof(OverrideNumbers))]
public partial class IntegrationJsonContext : JsonSerializerContext;

[Plugin]
public sealed class IntegrationPlugin : IPlugin
{
    public string Name => "integration";
    public void Initialize(PluginContext context) =>
        context.RegisterCommand("ping", static (_, _, _) => ValueTask.FromResult(JsonValue.From("pong")));
}

public sealed class RecordingRuntime : IDesktopRuntime
{
    public string LastScript { get; private set; } = string.Empty;
    public void Run(RuntimeContext context) { }
    public void Dispatch(Action action) => action();
    public Task<JsonElement> InvokeAsync(
        string command,
        JsonElement args,
        CancellationToken cancellationToken = default) =>
        Task.FromException<JsonElement>(new NotSupportedException(command));
    public void EvaluateScript(string webviewLabel, string script) => LastScript = script;
    public void Exit(int exitCode = 0) { }
}
