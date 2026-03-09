using System.Text.Json;

namespace Enigma.Client.Components;

internal static class CoopBoardJson
{
    public static bool TryGet(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value);
    }

    public static JsonElement GetObject(JsonElement element, string propertyName)
    {
        return TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
    }

    public static IReadOnlyList<JsonElement> GetArray(JsonElement element, string propertyName)
    {
        return TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : [];
    }

    public static string GetString(JsonElement element, string propertyName, string fallback = "")
    {
        return TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    public static int GetInt(JsonElement element, string propertyName, int fallback = 0)
    {
        return TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;
    }

    public static double GetDouble(JsonElement element, string propertyName, double fallback = 0)
    {
        return TryGet(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed)
            ? parsed
            : fallback;
    }

    public static bool GetBool(JsonElement element, string propertyName, bool fallback = false)
    {
        return TryGet(element, propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;
    }
}
