using System.Buffers;
using System.Text.Json;

namespace NetWiv.Plugin.Cli;

/// <summary>Exposes application command-line matches to the official CLI guest.</summary>
public sealed class CliPlugin(IReadOnlyList<string>? arguments = null) : IPlugin
{
    private readonly IReadOnlyList<string>? _arguments = arguments;
    private JsonElement _configuration = JsonValue.EmptyObject;

    public string Name => "cli";

    public void Initialize(PluginContext context)
    {
        _configuration = context.Config.ValueKind switch
        {
            JsonValueKind.Object => context.Config.Clone(),
            JsonValueKind.Null or JsonValueKind.Undefined => JsonValue.EmptyObject,
            _ => throw new InvokeException("CLI configuration must be an object.")
        };
        CliMatches.ValidateConfiguration(_configuration);
        context.RegisterCommand("cli_matches", (_, _, _) => ValueTask.FromResult(
            CliMatches.Parse(_configuration, _arguments ?? Environment.GetCommandLineArgs().Skip(1).ToArray())));
        context.Permission(new("allow-cli-matches", ["cli_matches"], []));
        context.Permission(new("deny-cli-matches", [], ["cli_matches"]));
        context.PermissionSet("default", "allow-cli-matches");
    }
}

internal static class CliMatches
{
    private sealed record Option(string Name, char? Short, bool TakesValue, bool Multiple, bool Required,
        int? Index, HashSet<string>? PossibleValues);

    internal static JsonElement Parse(JsonElement configuration, IReadOnlyList<string> argv)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) WriteMatches(writer, configuration, argv);
        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    internal static void ValidateConfiguration(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object) throw new InvokeException("CLI configuration must be an object.");
        foreach (var property in config.EnumerateObject())
            if (property.Name is not ("description" or "args" or "subcommands"))
                throw new InvokeException($"CLI configuration option '{property.Name}' is not supported yet.");
        _ = ReadOptions(config);
        var subcommands = Property(config, "subcommands");
        if (subcommands.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object))
            throw new InvokeException("CLI subcommands must be an object.");
        if (subcommands.ValueKind == JsonValueKind.Object)
            foreach (var command in subcommands.EnumerateObject()) ValidateConfiguration(command.Value);
    }

    private static void WriteMatches(Utf8JsonWriter writer, JsonElement config, IReadOnlyList<string> argv)
    {
        var options = ReadOptions(config);
        var matches = options.ToDictionary(option => option.Name, _ => new List<string>(), StringComparer.Ordinal);
        var counts = options.ToDictionary(option => option.Name, _ => 0, StringComparer.Ordinal);
        var positional = options.Where(option => option.Index is not null).OrderBy(option => option.Index).ToArray();
        var subcommands = Property(config, "subcommands");
        if (subcommands.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Object))
            throw new InvokeException("CLI subcommands must be an object.");
        var subcommand = (Name: (string?)null, Config: default(JsonElement), Start: 0);
        var nextPositional = 0;
        var named = true;
        for (var index = 0; index < argv.Count; index++)
        {
            var token = argv[index];
            if (named && token == "--") { named = false; continue; }
            if (named && token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2)
            {
                var separator = token.IndexOf('=');
                var name = separator < 0 ? token[2..] : token[2..separator];
                var option = options.FirstOrDefault(item => item.Index is null && item.Name == name)
                    ?? throw new InvokeException($"Unknown option '--{name}'.");
                Add(option, separator < 0 ? null : token[(separator + 1)..], argv, ref index, matches, counts, subcommands);
                continue;
            }
            if (named && token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1)
            {
                for (var character = 1; character < token.Length; character++)
                {
                    var option = options.FirstOrDefault(item => item.Index is null && item.Short == token[character])
                        ?? throw new InvokeException($"Unknown option '-{token[character]}'.");
                    var inline = option.TakesValue && character + 1 < token.Length ? token[(character + 1)..].TrimStart('=') : null;
                    Add(option, inline, argv, ref index, matches, counts, subcommands);
                    if (option.TakesValue) break;
                }
                continue;
            }
            if (subcommands.ValueKind == JsonValueKind.Object && subcommands.TryGetProperty(token, out var nested))
            {
                subcommand = (token, nested, index + 1);
                break;
            }
            if (nextPositional >= positional.Length) throw new InvokeException($"Unexpected argument '{token}'.");
            var target = positional[nextPositional];
            matches[target.Name].Add(token);
            counts[target.Name]++;
            ValidateValue(target, token);
            if (!target.Multiple) nextPositional++;
        }
        foreach (var option in options)
            if (option.Required && counts[option.Name] == 0)
                throw new InvokeException($"Required argument '{option.Name}' is missing.");
        writer.WriteStartObject();
        writer.WritePropertyName("args"); writer.WriteStartObject();
        foreach (var option in options)
        {
            writer.WritePropertyName(option.Name); writer.WriteStartObject();
            writer.WritePropertyName("value");
            if (!option.TakesValue) writer.WriteBooleanValue(counts[option.Name] > 0);
            else if (option.Multiple)
            {
                if (counts[option.Name] == 0) writer.WriteNullValue();
                else { writer.WriteStartArray(); foreach (var value in matches[option.Name]) writer.WriteStringValue(value); writer.WriteEndArray(); }
            }
            else if (counts[option.Name] == 0) writer.WriteNullValue();
            else writer.WriteStringValue(matches[option.Name][0]);
            writer.WriteNumber("occurrences", option.TakesValue && !option.Multiple ? Math.Min(counts[option.Name], 1) : Math.Min(counts[option.Name], byte.MaxValue));
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WritePropertyName("subcommand");
        if (subcommand.Name is null) writer.WriteNullValue();
        else
        {
            writer.WriteStartObject(); writer.WriteString("name", subcommand.Name);
            writer.WritePropertyName("matches");
            WriteMatches(writer, subcommand.Config, argv.Skip(subcommand.Start).ToArray());
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }

    private static void Add(Option option, string? inline, IReadOnlyList<string> argv, ref int index,
        Dictionary<string, List<string>> matches, Dictionary<string, int> counts, JsonElement subcommands)
    {
        if (!option.TakesValue)
        {
            if (inline is not null) throw new InvokeException($"Option '--{option.Name}' does not take a value.");
            counts[option.Name]++;
            return;
        }
        if (!option.Multiple && counts[option.Name] > 0) throw new InvokeException($"Option '--{option.Name}' was supplied more than once.");
        var value = inline ?? (++index < argv.Count ? argv[index] : throw new InvokeException($"Option '--{option.Name}' requires a value."));
        ValidateValue(option, value);
        matches[option.Name].Add(value);
        counts[option.Name]++;
        if (!option.Multiple) return;
        while (index + 1 < argv.Count && !argv[index + 1].StartsWith("-", StringComparison.Ordinal) &&
            !(subcommands.ValueKind == JsonValueKind.Object && subcommands.TryGetProperty(argv[index + 1], out _)))
        {
            value = argv[++index]; ValidateValue(option, value);
            matches[option.Name].Add(value); counts[option.Name]++;
        }
    }

    private static void ValidateValue(Option option, string value)
    {
        if (option.PossibleValues is not null && !option.PossibleValues.Contains(value))
            throw new InvokeException($"Invalid value '{value}' for '{option.Name}'.");
    }

    private static List<Option> ReadOptions(JsonElement config)
    {
        var args = Property(config, "args");
        if (args.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return [];
        if (args.ValueKind != JsonValueKind.Array) throw new InvokeException("CLI args must be an array.");
        var options = new List<Option>();
        foreach (var arg in args.EnumerateArray())
        {
            if (arg.ValueKind != JsonValueKind.Object) throw new InvokeException("CLI argument must be an object.");
            foreach (var property in arg.EnumerateObject())
                if (property.Name is not ("name" or "short" or "takesValue" or "multiple" or "required" or "index" or "possibleValues" or "description" or "longDescription"))
                    throw new InvokeException($"CLI argument option '{property.Name}' is not supported yet.");
            var nameValue = Property(arg, "name");
            var name = nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(name) || options.Any(item => item.Name == name)) throw new InvokeException("CLI argument names must be unique and nonempty.");
            var shortValue = Property(arg, "short");
            if (shortValue.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.String))
                throw new InvokeException("CLI short option must be one character.");
            char? shortName = shortValue.ValueKind == JsonValueKind.String ? shortValue.GetString() is { Length: 1 } text ? text[0] : throw new InvokeException("CLI short option must be one character.") : null;
            var indexValue = Property(arg, "index");
            var position = indexValue.ValueKind == JsonValueKind.Number ? indexValue.GetInt32() : (int?)null;
            if (position is < 1 || position is not null && options.Any(item => item.Index == position)) throw new InvokeException("CLI positional indexes must be unique positive numbers.");
            var values = Property(arg, "possibleValues");
            if (values.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.Array))
                throw new InvokeException("CLI possibleValues must be an array.");
            var possible = values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Select(item => item.GetString() ?? "").ToHashSet(StringComparer.Ordinal) : null;
            var multiple = ReadBoolean(arg, "multiple");
            var takesValue = ReadBoolean(arg, "takesValue") || position is not null;
            if (multiple && !takesValue) throw new InvokeException("A multiple CLI argument must take a value.");
            options.Add(new(name, shortName, takesValue, multiple, ReadBoolean(arg, "required"), position, possible));
        }
        return options;
    }

    private static bool ReadBoolean(JsonElement value, string name) => Property(value, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => false,
        _ => throw new InvokeException($"CLI {name} must be a boolean.")
    };
    private static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) ? property : default;
}
