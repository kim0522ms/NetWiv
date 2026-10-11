using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Parsing;

namespace NetWiv.Build.Configuration;

internal static class TomlLite
{
    public static JsonObject Parse(string text)
    {
        try
        {
            var options = new TomlParserOptions
            {
                Mode = TomlParserMode.Strict,
                DecodeScalars = true
            };
            var parser = TomlParser.Create(text, options, new TomlSerializerOptions());
            var containers = new Stack<JsonNode>();
            JsonObject? root = null;
            string? property = null;

            while (parser.MoveNext())
            {
                var item = parser.Current;
                switch (item.Kind)
                {
                    case TomlParseEventKind.StartDocument:
                    case TomlParseEventKind.EndDocument:
                        break;
                    case TomlParseEventKind.PropertyName:
                        property = parser.GetPropertyName();
                        break;
                    case TomlParseEventKind.StartTable:
                    {
                        var table = new JsonObject();
                        if (containers.Count == 0) root = table;
                        else Add(containers.Peek(), ref property, table);
                        containers.Push(table);
                        break;
                    }
                    case TomlParseEventKind.EndTable:
                    case TomlParseEventKind.EndArray:
                        _ = containers.Pop();
                        break;
                    case TomlParseEventKind.StartArray:
                    {
                        var array = new JsonArray();
                        Add(containers.Peek(), ref property, array);
                        containers.Push(array);
                        break;
                    }
                    case TomlParseEventKind.String:
                        Add(containers.Peek(), ref property, JsonValue.Create(parser.GetString()));
                        break;
                    case TomlParseEventKind.Integer:
                        Add(containers.Peek(), ref property, JsonValue.Create(item.GetInt64()));
                        break;
                    case TomlParseEventKind.Float:
                    {
                        var number = item.GetDouble();
                        if (!double.IsFinite(number)) throw new NetWivConfigException("TOML non-finite floats cannot be represented in a Tauri JSON configuration.");
                        Add(containers.Peek(), ref property, JsonValue.Create(number));
                        break;
                    }
                    case TomlParseEventKind.Boolean:
                        Add(containers.Peek(), ref property, JsonValue.Create(item.GetBoolean()));
                        break;
                    case TomlParseEventKind.DateTime:
                        Add(containers.Peek(), ref property, JsonValue.Create(item.GetTomlDateTime().ToString()));
                        break;
                    default:
                        throw new NetWivConfigException($"Unsupported TOML parser event: {item.Kind}.");
                }
            }

            if (parser.HasErrors)
                throw new NetWivConfigException($"TOML parse error: {parser.Diagnostics}");
            return root ?? throw new NetWivConfigException("TOML document did not contain a root table.");
        }
        catch (NetWivConfigException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
        {
            throw new NetWivConfigException($"TOML parse error: {exception.Message}");
        }
    }

    private static void Add(JsonNode container, ref string? property, JsonNode? value)
    {
        if (container is JsonObject table)
        {
            var name = property ?? throw new NetWivConfigException("TOML value is missing a property name.");
            table.Add(name, value);
            property = null;
            return;
        }
        if (container is JsonArray array)
        {
            array.Add(value);
            return;
        }
        throw new NetWivConfigException("TOML parser produced an invalid container.");
    }
}
