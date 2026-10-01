namespace Task5.Models;

public sealed class FullMovieInformation
{
    public required MovieBaseInfo ShortMovie { get; init; }

    public required ExpandedMovieInformation ExpandedMovie { get; init; }

    public required MovieTrailer Trailer { get; init; }
}
