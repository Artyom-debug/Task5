using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using Task5.Dtos;
using Task5.Models;
using Task5.Services;
using Task5.Utils;

namespace Task5.Controllers;

[ApiController]
[Route("api/movies")]
public sealed class MovieController : ControllerBase
{
    private static readonly TimeSpan MovieCacheLifetime = TimeSpan.FromHours(4);
    private static readonly ulong DefaultSeed = SeedGenerator.GenerateUserSeed();

    [HttpGet("seed")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<string> GetDefaultSeed() => Ok(DefaultSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private readonly TextGenerator _textGenerator;
    private readonly TrailerGenerator _trailerGenerator;
    private readonly IMemoryCache _cache;

    public MovieController(TextGenerator textGenerator, TrailerGenerator trailerGenerator, IMemoryCache memoryCache)
    {
        _textGenerator = textGenerator;
        _trailerGenerator = trailerGenerator;
        _cache = memoryCache;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<MovieBaseInfo>> GetMovies([FromQuery] Page query, [FromQuery] ulong? seed, [FromQuery] string locale)
    {
        if (query.PageNumber < 1 || query.PageSize < 1)
            return BadRequest("PageNumber and PageSize must be positive.");

        if (!IsSupportedLocale(locale))
            return BadRequest("Supported locales: en, en-US, ru, ru-RU.");


        var lastIndex = (long)query.PageNumber * query.PageSize;
        if (lastIndex > int.MaxValue)
            return BadRequest("Movie index is too large.");

        var movies = new List<MovieBaseInfo>(query.PageSize);
        var firstIndex = lastIndex - query.PageSize + 1;

        for (var offset = 0; offset < query.PageSize; offset++)
        {
            var index = (int)(firstIndex + offset);
            var movieSeed = SeedGenerator.GenerateMovieSeed(seed ?? DefaultSeed, locale, index);
            movies.Add(_textGenerator.GenerateShortMovieInformation(locale, movieSeed, index));
        }

        return Ok(movies);
    }

    [HttpPost("movie")]
    public async Task<ActionResult<FullMovieInformation>> GetMovie([FromBody] MovieDetailsRequest request, CancellationToken cancellationToken)
    {
        if (request.Movie is null || request.Movie.MovieSeed < 0 || request.Movie.SequenceIndex < 1 ||
            string.IsNullOrWhiteSpace(request.Movie.Title) ||
            string.IsNullOrWhiteSpace(request.Movie.Director) ||
            string.IsNullOrWhiteSpace(request.Movie.Genre))
            return BadRequest("A valid short movie is required.");

        if (!IsSupportedLocale(request.Locale))
            return BadRequest("Supported locales: en, en-US, ru, ru-RU.");

        if (!_textGenerator.IsSupportedGenre(request.Locale, request.Movie.Genre))
            return BadRequest("Movie genre is not supported for the selected locale.");

        if (!IsValidAverage(request.AvgLikes) || !IsValidAverage(request.AvgReviews))
            return BadRequest("AvgLikes and AvgReviews must be between 0 and 10.");

        var movie = request.Movie;
        var cacheKey = GetCacheKey(movie.MovieSeed);

        if (!_cache.TryGetValue(cacheKey, out CachedMovieContent? content))
        {
            var textTask = _textGenerator.GenerateFullMovieTextAsync(request.Locale, movie, cancellationToken);
            var trailerTask = _trailerGenerator.GenerateAsync(movie, request.Locale, cancellationToken);

            await Task.WhenAll(textTask, trailerTask);

            content = new CachedMovieContent(textTask.Result, trailerTask.Result);
            _cache.Set(cacheKey, content, MovieCacheLifetime);
        }

        var likesCount = MovieCountGenerator.GenerateCount(movie.MovieSeed, request.AvgLikes, "likes");
        var reviewsCount = MovieCountGenerator.GenerateCount(movie.MovieSeed, request.AvgReviews, "reviews");

        return Ok(new FullMovieInformation
        {
            ShortMovie = movie,
            ExpandedMovie = new ExpandedMovieInformation
            {
                LikesCount = likesCount,
                Description = content!.Text.Description,
                Reviews = content.Text.Reviews,
                ReviewsCount = reviewsCount
            },
            Trailer = content.Trailer
        });
    }

    [HttpGet("{movieSeed:int}/trailer")]
    public IActionResult GetTrailer(int movieSeed)
    {
        if (!_cache.TryGetValue(GetCacheKey(movieSeed), out CachedMovieContent? content))
            return NotFound();

        return File(content!.Trailer.VideoBytes, "video/mp4", enableRangeProcessing: true);
    }

    private static bool IsValidAverage(double value) => double.IsFinite(value) && value is >= 0 and <= 10;

    private static bool IsSupportedLocale(string? locale)
    {
        var normalized = locale?.Trim().Replace('_', '-').ToLowerInvariant();
        return normalized is "en" or "en-us" or "ru" or "ru-ru";
    }

    private static string GetCacheKey(int movieSeed) => $"movie-v2:{movieSeed}";

    private sealed record CachedMovieContent(GeneratedMovieText Text, MovieTrailer Trailer);
}
