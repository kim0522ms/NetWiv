using System.Text.Json.Nodes;

namespace NetWiv.Build.Configuration;

internal static class Json5
{
    public static JsonNode Parse(string text)
    {
        try
        {
            return global::Json5.Json5.Parse(text)
                ?? throw new NetWivConfigException("JSON5 document is empty.");
        }
        catch (Exception exception) when (exception is not NetWivConfigException)
        {
            throw new NetWivConfigException($"JSON5 parse error: {exception.Message}");
        }
    }
}
