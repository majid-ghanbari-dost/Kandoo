using System.Security.Cryptography;
using System.Text;

namespace Kandoo.Cloud;

public static class PublicTokenSecurity
{
    public static byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>Hashes both inputs first, so length and content are compared with the fixed-time primitive.</summary>
    public static bool FixedTimeEquals(string suppliedToken, string storedToken)
    {
        var suppliedHash = Hash(suppliedToken);
        var storedHash = Hash(storedToken);
        return CryptographicOperations.FixedTimeEquals(suppliedHash, storedHash);
    }

    public static bool FixedTimeHashEquals(ReadOnlySpan<byte> suppliedHash, ReadOnlySpan<byte> storedHash)
    {
        if (suppliedHash.Length != 32 || storedHash.Length != 32)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(suppliedHash, storedHash);
    }
}

public sealed record PublicRateLimitDecision(bool Allowed, int RetryAfterSeconds, int Remaining);

/// <summary>Small process-local fixed-window limiter; distributed deployment still needs an edge limiter.</summary>
public sealed class PublicInvoiceRateLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly IClock _clock;
    private readonly int _limit;

    public PublicInvoiceRateLimiter(IClock clock, int limitPerMinute = 60)
    {
        if (limitPerMinute <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limitPerMinute));
        }

        _clock = clock;
        _limit = limitPerMinute;
    }

    public PublicRateLimitDecision Check(string? clientAddress)
    {
        var key = string.IsNullOrWhiteSpace(clientAddress) ? "unknown" : clientAddress.Trim();
        var now = _clock.UtcNow;
        var windowStart = now.UtcDateTime.Ticks - now.UtcDateTime.Ticks % TimeSpan.TicksPerMinute;
        lock (_gate)
        {
            if (!_buckets.TryGetValue(key, out var bucket) || bucket.WindowStartTicks != windowStart)
            {
                bucket = new Bucket(windowStart, 0);
                _buckets[key] = bucket;
            }

            var retryAfter = Math.Max(1, (int)Math.Ceiling((new DateTimeOffset(windowStart, TimeSpan.Zero).AddMinutes(1) - now).TotalSeconds));
            if (bucket.Count >= _limit)
            {
                return new PublicRateLimitDecision(false, retryAfter, 0);
            }

            bucket.Count++;
            return new PublicRateLimitDecision(true, retryAfter, _limit - bucket.Count);
        }
    }

    private sealed class Bucket(long windowStartTicks, int count)
    {
        public long WindowStartTicks { get; } = windowStartTicks;
        public int Count { get; set; } = count;
    }
}
