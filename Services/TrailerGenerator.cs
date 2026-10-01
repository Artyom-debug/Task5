using System.Diagnostics;
using System.Globalization;
using System.Text;
using Task5.Models;

namespace Task5.Services;

public sealed class TrailerGenerator
{
    private const int TrailerSeconds = 10;
    private const int MinFragments = 4;
    private const int MaxFragments = 6;
    private const int MinFragmentSeconds = 1;
    private const int MaxFragmentSeconds = 3;
    private const double EndMarginSeconds = 0.1;

    private readonly IConfiguration _configuration;

    public TrailerGenerator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<MovieTrailer> GenerateAsync(MovieBaseInfo movie, string locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movie);
        ArgumentException.ThrowIfNullOrWhiteSpace(movie.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(movie.Director);
        ArgumentException.ThrowIfNullOrWhiteSpace(movie.Genre);
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);

        var movieSeed = movie.MovieSeed;
        var genre = movie.Genre;

        var ffmpegPath = _configuration["Trailer:FfmpegPath"];
        var videosDirectory = _configuration["Trailer:VideosDirectory"];
        var soundDirectory = _configuration["Trailer:SoundDirectory"];
        var tempDirectory = _configuration["Trailer:TempDirectory"];
        var titleFontPath = _configuration["Trailer:TitleFontPath"];
        var creditsFontPath = _configuration["Trailer:CreditsFontPath"];
        var language = locale.Trim().Replace('_', '-').Split('-')[0].ToLowerInvariant();
        var directorLabel = _configuration[$"Trailer:DirectorLabels:{language}"];

        if (string.IsNullOrWhiteSpace(ffmpegPath) ||
            string.IsNullOrWhiteSpace(videosDirectory) ||
            string.IsNullOrWhiteSpace(soundDirectory) ||
            string.IsNullOrWhiteSpace(tempDirectory) ||
            string.IsNullOrWhiteSpace(titleFontPath) ||
            string.IsNullOrWhiteSpace(creditsFontPath) ||
            string.IsNullOrWhiteSpace(directorLabel))
            throw new InvalidOperationException("Trailer paths, fonts, or director label are not configured.");

        var ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException("ffmpeg not found", ffmpegPath);

        if (!File.Exists(ffprobePath))
            throw new FileNotFoundException("ffprobe. not found", ffprobePath);

        if (!File.Exists(titleFontPath))
            throw new FileNotFoundException("Title font not found", titleFontPath);

        if (!File.Exists(creditsFontPath))
            throw new FileNotFoundException("Credits font not found", creditsFontPath);

        var videos = GetAssetFiles(videosDirectory, ".mp4");
        var musicFolder = _configuration[$"Trailer:GenreMusicFolders:{genre.Trim()}"];
        var musicDirectory = string.IsNullOrWhiteSpace(musicFolder)
            ? soundDirectory
            : Path.Combine(soundDirectory, musicFolder);
        var musicFiles = GetAssetFiles(musicDirectory, ".mp3");

        if (videos.Length < MinFragments || musicFiles.Length == 0)
            throw new InvalidOperationException("Trailer generation needs at least 3 MP4 files and 1 MP3 file.");

        var random = new Random(movieSeed);

        for (var i = videos.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (videos[i], videos[j]) = (videos[j], videos[i]);
        }

        var fragmentCount = random.Next(MinFragments, MaxFragments + 1);
        var selectedVideos = Enumerable.Range(0, fragmentCount)
            .Select(index => videos[index % videos.Length])
            .ToArray();
        var sourceDurations = new double[selectedVideos.Length];
        var durationsByPath = new Dictionary<string, double>(StringComparer.Ordinal);

        for (var i = 0; i < selectedVideos.Length; i++)
        {
            if (!durationsByPath.TryGetValue(selectedVideos[i], out var duration))
            {
                duration = await GetVideoDurationAsync(ffprobePath, selectedVideos[i], cancellationToken);
                durationsByPath.Add(selectedVideos[i], duration);
            }

            sourceDurations[i] = duration;
        }

        var fragmentDurations = ChooseFragmentDurations(random, sourceDurations);
        var trailerSeconds = TrailerSeconds;
        var startPositions = new string[selectedVideos.Length];

        for (var i = 0; i < selectedVideos.Length; i++)
        {
            var latestStart = Math.Max(0, sourceDurations[i] - fragmentDurations[i] - EndMarginSeconds);
            var start = random.NextDouble() * latestStart;
            startPositions[i] = start.ToString("0.###", CultureInfo.InvariantCulture);
        }

        var eligibleMusic = new List<(string Path, double Duration)>();

        foreach (var musicFile in musicFiles)
        {
            var duration = await GetAudioDurationAsync(ffprobePath, musicFile, cancellationToken);
            if (duration >= trailerSeconds + EndMarginSeconds)
                eligibleMusic.Add((musicFile, duration));
        }

        if (eligibleMusic.Count == 0)
            throw new InvalidOperationException($"At least one MP3 must be longer than the {trailerSeconds}-second trailer.");

        var (selectedMusic, musicDuration) = eligibleMusic[random.Next(eligibleMusic.Count)];
        var latestMusicStart = musicDuration - trailerSeconds - EndMarginSeconds;
        var musicStart = (random.NextDouble() * latestMusicStart).ToString("0.###", CultureInfo.InvariantCulture);

        var filter = BuildVideoFilter(fragmentDurations, genre) + ";" + BuildAudioFilter(selectedVideos.Length, trailerSeconds);

        var fullTempDirectory = Path.GetFullPath(tempDirectory);
        var workDirectory = Path.Combine(fullTempDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        var fullOutputPath = Path.Combine(workDirectory, "trailer.mp4");

        try
        {
            var fontsWorkDirectory = Path.Combine(workDirectory, "fonts");
            Directory.CreateDirectory(fontsWorkDirectory);
            File.Copy(titleFontPath, Path.Combine(fontsWorkDirectory, Path.GetFileName(titleFontPath)));
            File.Copy(creditsFontPath, Path.Combine(fontsWorkDirectory, Path.GetFileName(creditsFontPath)));
            await File.WriteAllTextAsync(
                Path.Combine(workDirectory, "credits.ass"),
                BuildCreditsAss(movie.Title, movie.Director, directorLabel),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                WorkingDirectory = workDirectory,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var arguments = new List<string> { "-y", "-nostdin", "-loglevel", "error" };

            for (var i = 0; i < selectedVideos.Length; i++)
                arguments.AddRange(["-ss", startPositions[i], "-i", selectedVideos[i]]);

            arguments.AddRange(
            [
                "-ss", musicStart, "-i", selectedMusic,
                "-filter_complex", filter,
                "-map", "[video]", "-map", "[audio]",
                "-t", trailerSeconds.ToString(CultureInfo.InvariantCulture),
                "-c:v", "libx264", "-pix_fmt", "yuv420p",
                "-preset", "veryfast", "-crf", "23",
                "-c:a", "aac", "-b:a", "128k",
                "-movflags", "+faststart",
                fullOutputPath
            ]);

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to launch ffmpeg.");

            var errorsTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }

                throw;
            }

            var errors = await errorsTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg error {process.ExitCode}: {errors}");
            var videoBytes = await File.ReadAllBytesAsync(fullOutputPath, cancellationToken);

            return new MovieTrailer
            {
                VideoUrl = $"/api/movies/{movieSeed}/trailer",
                VideoBytes = videoBytes
            };
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    private static int[] ChooseFragmentDurations(Random random, IReadOnlyList<double> sourceDurations)
    {
        var availableSeconds = sourceDurations
            .Select(duration => (int)Math.Min(MaxFragmentSeconds, Math.Floor(duration)))
            .ToArray();
        var current = new int[sourceDurations.Count];
        var combinations = new List<int[]>();

        void AddCombinations(int index, int remainingSeconds)
        {
            if (index == current.Length)
            {
                if (remainingSeconds == 0 && current.Distinct().Count() > 1)
                    combinations.Add((int[])current.Clone());

                return;
            }

            var remainingFragments = current.Length - index - 1;

            for (var seconds = MinFragmentSeconds; seconds <= availableSeconds[index]; seconds++)
            {
                var rest = remainingSeconds - seconds;

                if (rest < remainingFragments * MinFragmentSeconds ||
                    rest > remainingFragments * MaxFragmentSeconds)
                    continue;

                current[index] = seconds;
                AddCombinations(index + 1, rest);
            }
        }

        AddCombinations(0, TrailerSeconds);

        if (combinations.Count == 0)
            throw new InvalidOperationException("Selected MP4 files cannot make a 10-second trailer with differently sized 1–3 second fragments.");

        return combinations[random.Next(combinations.Count)];
    }

    private static string BuildVideoFilter(IReadOnlyList<int> fragmentDurations, string genre)
    {
        var filter = new StringBuilder();

        for (var i = 0; i < fragmentDurations.Count; i++)
        {
            filter.Append($"[{i}:v]trim=duration={fragmentDurations[i]},setpts=PTS-STARTPTS,");
            filter.Append("scale=1280:720:force_original_aspect_ratio=increase,");
            filter.Append($"crop=1280:720,setsar=1,fps=25[v{i}];");
        }

        for (var i = 0; i < fragmentDurations.Count; i++)
            filter.Append($"[v{i}]");

        filter.Append($"concat=n={fragmentDurations.Count}:v=1:a=0,");
        filter.Append(GetColorFilter(genre));
        filter.Append(",ass=filename=credits.ass:fontsdir=fonts");
        filter.Append("[video]");

        return filter.ToString();
    }

    private static string BuildCreditsAss(string title, string director, string directorLabel)
    {
        var safeTitle = WrapTitle(SanitizeAssText(title));
        var safeDirector = SanitizeAssText(director);
        var safeLabel = SanitizeAssText(directorLabel);
        var longestTitleLine = safeTitle.Split(@"\N").Max(line => line.Length);
        var titleSize = longestTitleLine > 24 ? 60 : 76;
        var directorSize = safeDirector.Length > 28 ? 42 : 52;

        return $$"""
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 1280
            PlayResY: 720
            WrapStyle: 2
            ScaledBorderAndShadow: yes

            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Title,Noto Serif Display SemiBold,{{titleSize}},&H00F7F3E9,&H00F7F3E9,&H70000000,&H00000000,0,0,0,0,100,100,2,0,1,2,2,5,60,60,40,1
            Style: CreditLabel,Noto Sans Medium,26,&H00DBD7CF,&H00DBD7CF,&H70000000,&H00000000,0,0,0,0,100,100,3,0,1,1,1,5,60,60,40,1
            Style: Director,Noto Sans Medium,{{directorSize}},&H00F7F3E9,&H00F7F3E9,&H70000000,&H00000000,0,0,0,0,100,100,1,0,1,2,2,5,60,60,40,1

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.80,0:00:04.80,Title,,0,0,0,,{\move(640,372,640,355,0,4000)\fad(700,700)}{{safeTitle}}
            Dialogue: 0,0:00:05.45,0:00:09.65,CreditLabel,,0,0,0,,{\move(640,314,640,304,0,4200)\fad(700,850)}{{safeLabel}}
            Dialogue: 0,0:00:05.45,0:00:09.65,Director,,0,0,0,,{\move(640,382,640,370,0,4200)\fad(700,850)}{{safeDirector}}
            """;
    }

    private static string SanitizeAssText(string value) => value.Trim()
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Replace('{', '(')
        .Replace('}', ')')
        .Replace('\\', '/');

    private static string WrapTitle(string title)
    {
        if (title.Length <= 26)
            return title;

        var middle = title.Length / 2;
        var leftSpace = title.LastIndexOf(' ', middle);
        var rightSpace = title.IndexOf(' ', middle);
        var splitAt = leftSpace < 0 ? rightSpace :
            rightSpace < 0 || middle - leftSpace <= rightSpace - middle ? leftSpace : rightSpace;

        return splitAt < 0 ? title : title[..splitAt] + @"\N" + title[(splitAt + 1)..];
    }

    private static string BuildAudioFilter(int audioInputIndex, int trailerSeconds) =>
        $"[{audioInputIndex}:a]atrim=duration={trailerSeconds},asetpts=PTS-STARTPTS," +
        $"afade=t=in:st=0:d=0.4,afade=t=out:st={trailerSeconds - 1}:d=1[audio]";

    private static string GetColorFilter(string genre) => genre.Trim().ToLowerInvariant() switch
    {
        "action" or "боевик" => "eq=contrast=1.12:saturation=1.10:brightness=-0.01",
        "sci-fi" or "фантастика" => "eq=contrast=1.08:saturation=0.90,colorbalance=rs=-0.03:bs=0.04:bm=0.03",
        "comedy" or "комедия" => "eq=contrast=1.04:saturation=1.15:brightness=0.02",
        "drama" or "драма" => "eq=contrast=1.06:saturation=0.92",
        "horror" or "ужасы" => "eq=contrast=1.14:saturation=0.72:brightness=-0.04,colorbalance=rs=-0.02:bs=0.03",
        "thriller" or "триллер" => "eq=contrast=1.12:saturation=0.82:brightness=-0.02",
        "detective" or "детектив" => "eq=contrast=1.08:saturation=0.85,colorbalance=rs=-0.02:bs=0.02",
        _ => "eq=contrast=1.05:saturation=1.02"
    };

    private static string[] GetAssetFiles(string directory, string extension)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(directory);

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
    }

    private static Task<double> GetVideoDurationAsync(string ffprobePath, string videoPath, CancellationToken cancellationToken) =>
        GetMediaDurationAsync(ffprobePath, videoPath, cancellationToken);

    private static Task<double> GetAudioDurationAsync(string ffprobePath, string audioPath, CancellationToken cancellationToken) =>
        GetMediaDurationAsync(ffprobePath, audioPath, cancellationToken);

    private static async Task<double> GetMediaDurationAsync(string ffprobePath, string mediaPath, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        string[] arguments =
        [
            "-v", "error",
            "-show_entries", "format=duration",
            "-of", "default=noprint_wrappers=1:nokey=1",
            mediaPath
        ];

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) 
            ?? throw new InvalidOperationException("Failed to launch ffprobe.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorsTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            throw;
        }

        var output = (await outputTask).Trim();
        var errors = await errorsTask;

        if (process.ExitCode != 0 || !double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration) || !double.IsFinite(duration) || duration <= 0)
            throw new InvalidOperationException($"Failed to determine duration of {mediaPath}. ffprobe output: '{output}'. Error: {errors}");
        return duration;
    }
}
