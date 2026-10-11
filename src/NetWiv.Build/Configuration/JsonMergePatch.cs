using System.Text.Json.Nodes;

namespace NetWiv.Build.Configuration;

public static class JsonMergePatch
{
    public static JsonNode? Apply(JsonNode? target, JsonNode? patch) => Merge(target, patch, false);

    /// <summary>Combines CLI patches while retaining null as a later deletion instruction.</summary>
    public static JsonNode? Compose(JsonNode? target, JsonNode? patch) => Merge(target, patch, true);

    private static JsonNode? Merge(JsonNode? target, JsonNode? patch, bool preserveNull)
    {
        if (patch is not JsonObject patchObject)
            return patch?.DeepClone();

        var result = target is JsonObject targetObject
            ? (JsonObject)targetObject.DeepClone()
            : [];

        foreach (var property in patchObject)
        {
            if (property.Value is null && !preserveNull)
            {
                result.Remove(property.Key);
                continue;
            }

            result[property.Key] = property.Value is JsonObject
                ? Merge(result[property.Key], property.Value, preserveNull)
                : property.Value?.DeepClone();
        }

        return result;
    }
}
