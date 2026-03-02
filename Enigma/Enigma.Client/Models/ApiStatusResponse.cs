using System.Text.Json.Serialization;

namespace Enigma.Client.Models;

public class ApiStatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    public string ToDisplayMessage()
    {
        return !string.IsNullOrWhiteSpace(Detail)
            ? Detail
            : !string.IsNullOrWhiteSpace(Message)
                ? Message
                : !string.IsNullOrWhiteSpace(Status)
                    ? Status
                    : "Request completed.";
    }
}
