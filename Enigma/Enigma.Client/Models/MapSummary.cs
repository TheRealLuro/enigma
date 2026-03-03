using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

[JsonConverter(typeof(MapSummaryJsonConverter))]
public class MapSummary
{
    public string Id { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string? MapImage { get; set; }
    public bool ImageAvailable { get; set; }
    public string ImageStatus { get; set; } = string.Empty;
    public string? ImageUploadError { get; set; }
    public string Theme { get; set; } = string.Empty;
    public string ThemeLabel { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public int Size { get; set; }
    public string Founder { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public int Value { get; set; }
    public int SoldForLast { get; set; }
    public int Plays { get; set; }
    public string BestTime { get; set; } = "N/A";
    public string BestTimeDisplay { get; set; } = "N/A";
    public int? BestTimeMs { get; set; }
    public string UserWithBestTime { get; set; } = string.Empty;
    public string? TimeFounded { get; set; }
    public string TimeFoundedDisplay { get; set; } = string.Empty;
    public double RatingAverage { get; set; }
    public int RatingCount { get; set; }
}

public sealed class MapSummaryJsonConverter : JsonConverter<MapSummary>
{
    public override MapSummary? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (root.ValueKind == JsonValueKind.String)
        {
            return new MapSummary { Id = root.GetString() ?? string.Empty };
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected a map object or map id string.");
        }

        var mapImage = GetString(root, "map_image");
        var bestTimeElement = GetProperty(root, "best_time");
        var ratingElement = GetProperty(root, "rating") ?? GetProperty(root, "ratings");
        var ratingAverage = GetDouble(root, "rating_average") ?? ComputeAverageRating(ratingElement);
        var ratingCount = GetInt(root, "rating_count") ?? ComputeRatingCount(ratingElement);
        var bestTimeMs = GetInt(root, "best_time_ms") ?? ComputeBestTimeMilliseconds(bestTimeElement);
        var bestTimeDisplay = GetString(root, "best_time_display") ?? FormatBestTime(bestTimeElement);
        var themeLabel = GetString(root, "theme_label") ?? GetString(root, "theme") ?? string.Empty;
        var foundedDisplay = GetString(root, "founded_display")
            ?? GetString(root, "time_founded_display")
            ?? GetString(root, "time_founded")
            ?? string.Empty;

        return new MapSummary
        {
            Id = GetString(root, "id") ?? GetString(root, "_id") ?? string.Empty,
            MapName = GetString(root, "map_name") ?? string.Empty,
            MapImage = mapImage,
            ImageAvailable = GetBool(root, "image_available") ?? !string.IsNullOrWhiteSpace(mapImage),
            ImageStatus = GetString(root, "image_status") ?? (string.IsNullOrWhiteSpace(mapImage) ? "pending_upload" : "ready"),
            ImageUploadError = GetString(root, "image_upload_error"),
            Theme = themeLabel,
            ThemeLabel = themeLabel,
            Difficulty = GetString(root, "difficulty") ?? string.Empty,
            Size = GetInt(root, "size") ?? 0,
            Founder = GetString(root, "founder") ?? string.Empty,
            Owner = GetString(root, "owner") ?? string.Empty,
            Value = GetInt(root, "value") ?? 0,
            SoldForLast = GetInt(root, "sold_for_last") ?? 0,
            Plays = GetInt(root, "plays") ?? 0,
            BestTime = bestTimeDisplay,
            BestTimeDisplay = bestTimeDisplay,
            BestTimeMs = bestTimeMs,
            UserWithBestTime = GetString(root, "user_with_best_time") ?? string.Empty,
            TimeFounded = GetString(root, "time_founded"),
            TimeFoundedDisplay = foundedDisplay,
            RatingAverage = ratingAverage,
            RatingCount = ratingCount,
        };
    }

    public override void Write(Utf8JsonWriter writer, MapSummary value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("map_name", value.MapName);

        if (!string.IsNullOrWhiteSpace(value.MapImage))
        {
            writer.WriteString("map_image", value.MapImage);
        }
        else
        {
            writer.WriteNull("map_image");
        }

        writer.WriteBoolean("image_available", value.ImageAvailable);
        writer.WriteString("image_status", value.ImageStatus);

        if (!string.IsNullOrWhiteSpace(value.ImageUploadError))
        {
            writer.WriteString("image_upload_error", value.ImageUploadError);
        }
        else
        {
            writer.WriteNull("image_upload_error");
        }

        writer.WriteString("theme", value.Theme);
        writer.WriteString("theme_label", value.ThemeLabel);
        writer.WriteString("difficulty", value.Difficulty);
        writer.WriteNumber("size", value.Size);
        writer.WriteString("founder", value.Founder);
        writer.WriteString("owner", value.Owner);
        writer.WriteNumber("value", value.Value);
        writer.WriteNumber("sold_for_last", value.SoldForLast);
        writer.WriteNumber("plays", value.Plays);
        writer.WriteString("best_time", value.BestTime);
        writer.WriteString("best_time_display", value.BestTimeDisplay);

        if (value.BestTimeMs.HasValue)
        {
            writer.WriteNumber("best_time_ms", value.BestTimeMs.Value);
        }
        else
        {
            writer.WriteNull("best_time_ms");
        }

        writer.WriteString("user_with_best_time", value.UserWithBestTime);

        if (!string.IsNullOrWhiteSpace(value.TimeFounded))
        {
            writer.WriteString("time_founded", value.TimeFounded);
        }
        else
        {
            writer.WriteNull("time_founded");
        }

        writer.WriteString("time_founded_display", value.TimeFoundedDisplay);
        writer.WriteString("founded_display", value.TimeFoundedDisplay);
        writer.WriteNumber("rating_average", value.RatingAverage);
        writer.WriteNumber("rating_count", value.RatingCount);
        writer.WriteEndObject();
    }

    private static JsonElement? GetProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatBestTime(JsonElement? value)
    {
        if (value is null)
        {
            return "N/A";
        }

        var element = value.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? "N/A";
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return "N/A";
        }

        var hours = GetInt(element, "hours") ?? 0;
        var minutes = GetInt(element, "minutes") ?? 0;
        var seconds = GetInt(element, "seconds") ?? 0;
        var milliseconds = GetInt(element, "milliseconds") ?? 0;
        return $"{hours:00}:{minutes:00}:{seconds:00}:{milliseconds:000}";
    }

    private static int? ComputeBestTimeMilliseconds(JsonElement? value)
    {
        if (value is null)
        {
            return null;
        }

        var element = value.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            var parts = (element.GetString() ?? string.Empty).Split(':');
            if (parts.Length != 4)
            {
                return null;
            }

            if (int.TryParse(parts[0], out var hours)
                && int.TryParse(parts[1], out var minutes)
                && int.TryParse(parts[2], out var seconds)
                && int.TryParse(parts[3], out var milliseconds))
            {
                return (hours * 3_600_000) + (minutes * 60_000) + (seconds * 1_000) + milliseconds;
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var objectHours = GetInt(element, "hours") ?? 0;
        var objectMinutes = GetInt(element, "minutes") ?? 0;
        var objectSeconds = GetInt(element, "seconds") ?? 0;
        var objectMilliseconds = GetInt(element, "milliseconds") ?? 0;
        return (objectHours * 3_600_000) + (objectMinutes * 60_000) + (objectSeconds * 1_000) + objectMilliseconds;
    }

    private static double ComputeAverageRating(JsonElement? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value.Value.ValueKind == JsonValueKind.Array)
        {
            var ratings = value.Value.EnumerateArray()
                .Select(ParseRatingElement)
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .ToList();

            return ratings.Count == 0 ? 0 : Math.Round(ratings.Average(), 2);
        }

        if (value.Value.ValueKind == JsonValueKind.Number)
        {
            return value.Value.GetDouble();
        }

        if (value.Value.ValueKind == JsonValueKind.String && double.TryParse(value.Value.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static int ComputeRatingCount(JsonElement? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value.Value.ValueKind == JsonValueKind.Array)
        {
            return value.Value.EnumerateArray().Count(item => ParseRatingElement(item).HasValue);
        }

        if (value.Value.ValueKind == JsonValueKind.Number)
        {
            return 1;
        }

        return value.Value.ValueKind == JsonValueKind.String && double.TryParse(value.Value.GetString(), out _)
            ? 1
            : 0;
    }

    private static double? ParseRatingElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var numericValue))
        {
            return numericValue;
        }

        if (element.ValueKind == JsonValueKind.String && double.TryParse(element.GetString(), out numericValue))
        {
            return numericValue;
        }

        return null;
    }
}
