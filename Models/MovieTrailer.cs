namespace Task5.Models;

public sealed class MovieTrailer
{
    public required string VideoUrl { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] VideoBytes { get; init; } = [];
}
