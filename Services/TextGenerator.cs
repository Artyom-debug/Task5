using Bogus;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Task5.Models;

namespace Task5.Services;

public sealed class TextGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] ReviewTopics =
    [
        "overall", "plot", "characters", "acting", "direction",
        "visuals", "atmosphere", "pacing", "soundtrack", "ending"
    ];

    private readonly Dictionary<string, LocaleResources> _resources;

    public TextGenerator(IHostEnvironment environment)
    {
        _resources = new Dictionary<string, LocaleResources>(StringComparer.Ordinal)
        {
            ["en"] = LoadResources(environment.ContentRootPath, "en"),
            ["ru"] = LoadResources(environment.ContentRootPath, "ru")
        };
    }

    public MovieBaseInfo GenerateShortMovieInformation(string locale, int movieSeed, int index)
    {
        if (movieSeed < 0)
            throw new ArgumentOutOfRangeException(nameof(movieSeed));
        if (index < 1)
            throw new ArgumentOutOfRangeException(nameof(index));

        var resources = _resources[NormalizeBogusLocale(locale)];
        var faker = new Faker(resources.Locale)
        {
            Random = new Randomizer(movieSeed)
        };

        var genre = Select(resources.Genres, movieSeed, "movie:genre");
        var titleWords = resources.Titles.Genres[genre];
        var title = resources.Titles.Pattern
            .Replace("{adjective}", Select(titleWords.Adjectives, movieSeed, $"title:adjective:{genre}"), StringComparison.Ordinal)
            .Replace("{noun}", Select(titleWords.Nouns, movieSeed, $"title:noun:{genre}"), StringComparison.Ordinal);
        var year = faker.Random.Int(1980, 2030);
        var actorCount = faker.Random.Int(1, 5);
        var actors = Enumerable.Range(0, actorCount)
            .Select(_ => faker.Name.FullName())
            .ToArray();
        var director = faker.Name.FullName();

        return new MovieBaseInfo
        {
            MovieSeed = movieSeed,
            SequenceIndex = index,
            Title = title,
            Year = year,
            Genre = genre,
            Actors = actors,
            Director = director
        };
    }

    public bool IsSupportedGenre(string locale, string? genre) =>
        !string.IsNullOrWhiteSpace(genre) &&
        _resources[NormalizeBogusLocale(locale)].Descriptions.Genres.ContainsKey(genre);

    // Kept asynchronous at the API boundary so the controller does not need to change.
    // All work here is local and deterministic; no network request is made.
    public Task<GeneratedMovieText> GenerateFullMovieTextAsync(
        string locale,
        MovieBaseInfo info,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        cancellationToken.ThrowIfCancellationRequested();

        var resources = _resources[NormalizeBogusLocale(locale)];
        if (!resources.Descriptions.Genres.TryGetValue(info.Genre, out var genreText))
            throw new ArgumentException($"Genre '{info.Genre}' is not supported for locale '{resources.Locale}'.", nameof(info));

        var actor = info.Actors?.FirstOrDefault() ?? string.Empty;
        var description = string.Join(' ',
            Format(Select(resources.Descriptions.Openings, info.MovieSeed, "description:opening"), info, actor),
            Format(Select(genreText.Premises, info.MovieSeed, $"description:premise:{info.Genre}"), info, actor),
            Format(Select(genreText.Stakes, info.MovieSeed, $"description:stakes:{info.Genre}"), info, actor),
            Format(Select(resources.Descriptions.Closings, info.MovieSeed, "description:closing"), info, actor));

        var reviews = new string[ReviewTopics.Length];
        for (var i = 0; i < ReviewTopics.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var topic = ReviewTopics[i];
            var variants = resources.Reviews.Topics[topic];

            // Keep at least one review tied to the generated title, actor and director.
            var requiredPlaceholder = topic switch
            {
                "overall" => "{title}",
                "acting" when actor.Length > 0 => "{actor}",
                "direction" => "{director}",
                _ => null
            };
            if (requiredPlaceholder is not null)
                variants = variants.Where(x => x.Contains(requiredPlaceholder, StringComparison.Ordinal)).ToArray();

            // A client-supplied movie might omit actors. In that case, use only
            // variants that do not require {actor}.
            if (actor.Length == 0)
                variants = variants.Where(x => !x.Contains("{actor}", StringComparison.Ordinal)).ToArray();

            var template = Select(variants, info.MovieSeed, $"review:{topic}");
            reviews[i] = Format(template, info, actor);
        }

        return Task.FromResult(new GeneratedMovieText
        {
            Description = description,
            Reviews = reviews
        });
    }

    private static LocaleResources LoadResources(string contentRoot, string locale)
    {
        var reviewPath = Path.Combine(contentRoot, "Resources", "MovieReviews", $"{locale}.json");
        var descriptionPath = Path.Combine(contentRoot, "Resources", "MovieDescriptions", $"{locale}.json");
        var titlePath = Path.Combine(contentRoot, "Resources", "MovieTitles", $"{locale}.json");

        var reviews = JsonSerializer.Deserialize<ReviewResources>(File.ReadAllText(reviewPath), JsonOptions)
            ?? throw new InvalidOperationException($"Invalid review resource: {reviewPath}");
        var descriptions = JsonSerializer.Deserialize<DescriptionResources>(File.ReadAllText(descriptionPath), JsonOptions)
            ?? throw new InvalidOperationException($"Invalid description resource: {descriptionPath}");
        var titles = JsonSerializer.Deserialize<TitleResources>(File.ReadAllText(titlePath), JsonOptions)
            ?? throw new InvalidOperationException($"Invalid title resource: {titlePath}");

        if (reviews.Version != 1 || descriptions.Version != 1 || titles.Version != 1 ||
            reviews.Locale != locale || descriptions.Locale != locale || titles.Locale != locale)
            throw new InvalidOperationException($"Unsupported movie-text resources for locale '{locale}'.");

        if (reviews.Topics is null || reviews.Topics.Count != ReviewTopics.Length ||
            ReviewTopics.Any(topic => !reviews.Topics.TryGetValue(topic, out var variants) ||
                                      variants is not { Length: > 0 } ||
                                      variants.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException($"Incomplete review topics for locale '{locale}'.");

        if (!reviews.Topics["overall"].Any(x => x.Contains("{title}", StringComparison.Ordinal)) ||
            !reviews.Topics["acting"].Any(x => x.Contains("{actor}", StringComparison.Ordinal)) ||
            !reviews.Topics["direction"].Any(x => x.Contains("{director}", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Missing personalized review variants for locale '{locale}'.");

        if (descriptions.Openings is not { Length: > 0 } ||
            descriptions.Closings is not { Length: > 0 } ||
            descriptions.Openings.Any(string.IsNullOrWhiteSpace) ||
            descriptions.Closings.Any(string.IsNullOrWhiteSpace) ||
            descriptions.Genres is not { Count: > 0 } ||
            descriptions.Genres.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Value?.Premises is not { Length: > 0 } ||
                pair.Value.Stakes is not { Length: > 0 } ||
                pair.Value.Premises.Any(string.IsNullOrWhiteSpace) ||
                pair.Value.Stakes.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException($"Incomplete descriptions for locale '{locale}'.");

        var genres = descriptions.Genres.Keys.Order(StringComparer.Ordinal).ToArray();
        if (titles.Pattern is null ||
            !titles.Pattern.Contains("{adjective}", StringComparison.Ordinal) ||
            !titles.Pattern.Contains("{noun}", StringComparison.Ordinal) ||
            titles.Genres is null || titles.Genres.Count != genres.Length ||
            genres.Any(genre => !titles.Genres.TryGetValue(genre, out var words) ||
                                words?.Adjectives is not { Length: > 0 } ||
                                words.Nouns is not { Length: > 0 } ||
                                words.Adjectives.Any(string.IsNullOrWhiteSpace) ||
                                words.Nouns.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException($"Incomplete title words for locale '{locale}'.");

        return new LocaleResources(locale, genres, reviews, descriptions, titles);
    }

    private static string Select(string[] variants, int movieSeed, string scope)
    {
        if (variants.Length == 0)
            throw new InvalidOperationException($"No text variants are configured for '{scope}'.");

        var source = $"text-v1|{movieSeed.ToString(CultureInfo.InvariantCulture)}|{scope}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var value = BinaryPrimitives.ReadUInt32LittleEndian(hash);
        return variants[(int)(value % (uint)variants.Length)];
    }

    private static string Format(string template, MovieBaseInfo info, string actor) => template
        .Replace("{title}", info.Title, StringComparison.Ordinal)
        .Replace("{director}", info.Director, StringComparison.Ordinal)
        .Replace("{actor}", actor, StringComparison.Ordinal);

    private static string NormalizeBogusLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        var normalized = locale.Trim().Replace('_', '-').ToLowerInvariant();
        return normalized switch
        {
            "en" or "en-us" => "en",
            "ru" or "ru-ru" => "ru",
            _ => throw new ArgumentException($"Locale '{locale}' is not supported.", nameof(locale))
        };
    }

    private sealed record LocaleResources(string Locale, string[] Genres, ReviewResources Reviews, DescriptionResources Descriptions, TitleResources Titles);

    private sealed class ReviewResources
    {
        public int Version { get; init; }
        public string Locale { get; init; } = string.Empty;
        public Dictionary<string, string[]> Topics { get; init; } = [];
    }

    private sealed class DescriptionResources
    {
        public int Version { get; init; }
        public string Locale { get; init; } = string.Empty;
        public string[] Openings { get; init; } = [];
        public string[] Closings { get; init; } = [];
        public Dictionary<string, GenreDescription> Genres { get; init; } = [];
    }

    private sealed class GenreDescription
    {
        public string[] Premises { get; init; } = [];
        public string[] Stakes { get; init; } = [];
    }

    private sealed class TitleResources
    {
        public int Version { get; init; }
        public string Locale { get; init; } = string.Empty;
        public string Pattern { get; init; } = string.Empty;
        public Dictionary<string, TitleWords> Genres { get; init; } = [];
    }

    private sealed class TitleWords
    {
        public string[] Adjectives { get; init; } = [];
        public string[] Nouns { get; init; } = [];
    }
}
