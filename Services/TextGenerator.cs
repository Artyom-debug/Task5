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
            ComposeSentence(resources.Descriptions.Openings, info, actor, "description:opening", "{title}"),
            ComposeSentence(genreText.Premises, info, actor, $"description:premise:{info.Genre}"),
            ComposeSentence(genreText.Stakes, info, actor, $"description:stakes:{info.Genre}"),
            ComposeSentence(resources.Descriptions.Closings, info, actor, "description:closing"));

        var reviews = new string[ReviewTopics.Length];
        for (var i = 0; i < ReviewTopics.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var topic = ReviewTopics[i];
            var parts = resources.Reviews.Topics[topic];

            var requiredPlaceholder = topic switch
            {
                "overall" => "{title}",
                "acting" when actor.Length > 0 => "{actor}",
                "direction" => "{director}",
                _ => null
            };
            reviews[i] = ComposeSentence(parts, info, actor, $"review:{topic}", requiredPlaceholder);
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

        var genres = descriptions.Genres.Keys.Order(StringComparer.Ordinal).ToArray();
        return new LocaleResources(locale, genres, reviews, descriptions, titles);
    }

    private static string ComposeSentence(SentenceParts parts, MovieBaseInfo info, string actor, string scope, string? requiredSubjectPlaceholder = null)
    {
        var subjects = parts.Subjects.AsEnumerable();
        if (requiredSubjectPlaceholder is not null)
            subjects = subjects.Where(x => x.Contains(requiredSubjectPlaceholder, StringComparison.Ordinal));
        if (actor.Length == 0)
            subjects = subjects.Where(x => !x.Contains("{actor}", StringComparison.Ordinal));

        var subject = Select(subjects.ToArray(), info.MovieSeed, $"{scope}:subject");
        var predicate = Select(parts.Predicates, info.MovieSeed, $"{scope}:predicate");
        var ending = Select(parts.Endings, info.MovieSeed, $"{scope}:ending");

        var formattedEnding = Format(ending, info, actor);
        var separator = formattedEnding.StartsWith(',') ? string.Empty : " ";
        return $"{Format(subject, info, actor)} {Format(predicate, info, actor)}{separator}{formattedEnding}.";
    }

    private static string Select(string[] variants, int movieSeed, string scope)
    {
        if (variants.Length == 0)
            throw new InvalidOperationException($"No text variants are configured for '{scope}'.");

        var source = $"{movieSeed.ToString(CultureInfo.InvariantCulture)}|{scope}";
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
        public Dictionary<string, SentenceParts> Topics { get; init; } = [];
    }

    private sealed class DescriptionResources
    {
        public int Version { get; init; }
        public string Locale { get; init; } = string.Empty;
        public SentenceParts Openings { get; init; } = new();
        public SentenceParts Closings { get; init; } = new();
        public Dictionary<string, GenreDescription> Genres { get; init; } = [];
    }

    private sealed class GenreDescription
    {
        public SentenceParts Premises { get; init; } = new();
        public SentenceParts Stakes { get; init; } = new();
    }

    private sealed class SentenceParts
    {
        public string[] Subjects { get; init; } = [];
        public string[] Predicates { get; init; } = [];
        public string[] Endings { get; init; } = [];
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
