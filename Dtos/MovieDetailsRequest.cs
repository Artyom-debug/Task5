using Task5.Models;

namespace Task5.Dtos;

public sealed record MovieDetailsRequest
{
    public required MovieBaseInfo Movie { get; init; }

    public required string Locale { get; init; }

    public required double AvgLikes { get; init; }

    public required double AvgReviews { get; init; }
}
