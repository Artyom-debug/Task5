using System.Text.Json.Serialization;

namespace Task5.Models;

public sealed class GeneratedMovieText
{
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("reviews")]
    public string[] Reviews { get; init; } = [];
}
