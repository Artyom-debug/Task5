using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;

namespace Task5.Utils;

public static class SeedGenerator
{
    private const ulong MaxUserSeed = ulong.MaxValue;

    public static ulong GenerateUserSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        Random.Shared.NextBytes(bytes);

        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    public static int GenerateMovieSeed(ulong seed, string locale, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        if(seed > MaxUserSeed)
            throw new ArgumentOutOfRangeException($"Seed can't be larger than {MaxUserSeed}", nameof(seed));
        if (index < 1)
            throw new ArgumentException("Incorrect index value", nameof(index));
        var normalizedLocale = locale.Trim().Replace('_', '-').ToLowerInvariant();
        var source = string.Join('|', seed.ToString(), normalizedLocale, index.ToString());
        return HashToInt(source);
    }

    public static int GenerateScopedSeed(int movieSeed, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var source = string.Join('|', movieSeed.ToString(), scope.Trim().ToLowerInvariant());
        return HashToInt(source);
    }

    private static int HashToInt(string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return BinaryPrimitives.ReadInt32LittleEndian(hash) & int.MaxValue;
    }
}
