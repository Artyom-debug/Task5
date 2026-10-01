namespace Task5.Models;

public sealed class ExpandedMovieInformation
{
    public required int LikesCount { get; init; }

    public required string Description { get; init; }

    public required IReadOnlyList<string> Reviews { get; init; }

    public required int ReviewsCount { get; init; }
}
