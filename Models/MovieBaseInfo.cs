namespace Task5.Models;

public sealed record MovieBaseInfo
{
    public required int SequenceIndex { get; init; }

    public required int MovieSeed { get; init; }

    public required string Title { get; init; }

    public required int Year { get; init; }

    public required string Genre { get; init; }

    public required string Director { get; init; }

    public required IReadOnlyList<string> Actors { get; init; }
}
