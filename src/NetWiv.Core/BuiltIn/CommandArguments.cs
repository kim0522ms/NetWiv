using System.Text.Json;

namespace NetWiv;

internal static class CommandArguments
{
    internal static JsonElement Required(JsonElement args, string command, string name)
    {
        if (!args.TryGetProperty(name, out var value))
            throw Error(command, name, $"command {command} missing required key {name}");
        return value;
    }

    internal static string String(JsonElement args, string command, string name)
    {
        var value = Required(args, command, name);
        if (value.ValueKind != JsonValueKind.String)
            throw Error(command, name, $"invalid type: {CommandArgumentJson.Unexpected(value)}, expected a string");
        return value.GetString()!;
    }

    internal static InvokeException Error(string command, string name, string reason) =>
        new($"invalid args `{name}` for command `{command}`: {reason}");
}
