using System.Text.Json;
using System.Text.Json.Serialization;
using NetWiv;

[assembly: GenerateFrontendBindings]
[assembly: CommandJsonContext(typeof(NetWiv.Plugin.LibraryCheck.PackageJsonContext))]

namespace NetWiv.Plugin.LibraryCheck;

[Plugin]
public sealed class LibraryCheckPlugin : IPlugin
{
    public string Name => "library-check";

    public void Initialize(PluginContext context)
    {
        context.Permission(new PermissionDefinition("default", ["ping", "profile", "blocked", "hidden"], ["blocked"]));
        context.RegisterGeneratedCommands<LibraryCheckPlugin>("ping", "profile", "blocked");
    }

    [Command] public static JsonElement Ping() => JsonValue.From("pong");
    [Command] public static PackageProfile Profile(PackageProfile value) => value;
    [Command] public static JsonElement Blocked() => JsonValue.From("ACL must reject this command");
    [Command] public static JsonElement Hidden() => JsonValue.From("This command is never registered");
}

public static class PackageCommands
{
    [Command] public static PackageProfile PackageEcho(PackageProfile value) => value;
}

public sealed record PackageProfile(string DisplayName, int Count)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] public string? InputOnly { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)] public string OutputOnly => InputOnly ?? "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
[JsonSerializable(typeof(PackageProfile))]
internal partial class PackageJsonContext : JsonSerializerContext;
