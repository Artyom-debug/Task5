namespace Task5.Utils;

public static class MovieCountGenerator
{
    public static int GenerateCount(int movieSeed, double avg, string scope)
    {
        if (double.IsNaN(avg) || double.IsInfinity(avg) || avg is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(avg), "Average must be between 0 and 10.");
        var wholePart = (int)Math.Floor(avg);
        var fractionalPart = avg - wholePart;
        if (fractionalPart < 0.0000001)
            return wholePart;
        var scopedSeed = SeedGenerator.GenerateScopedSeed(movieSeed, scope);
        var random = new Random(scopedSeed);
        var probability = random.NextDouble();

        return probability < fractionalPart ? wholePart + 1 : wholePart;
    }
}
