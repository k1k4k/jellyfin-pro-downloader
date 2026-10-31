using System.Diagnostics;

namespace Jellyfin.Plugin.ProDownloader.Services;

/// <summary>
/// Limites appliquées par le serveur, quel que soit le client : nombre de fichiers envoyés en même temps
/// (par utilisateur et au total) et débit par utilisateur (seau à jetons partagé par ses téléchargements).
/// </summary>
public class TransferLimiter
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, int> _perUser = [];
    private readonly Dictionary<Guid, Bucket> _buckets = [];
    private int _total;

    /// <summary>Téléchargements en cours (page de configuration).</summary>
    public int ActiveTotal
    {
        get
        {
            lock (_lock)
            {
                return _total;
            }
        }
    }

    /// <summary>Réserve une place, ou null si une limite est atteinte (le client réessaiera).</summary>
    public IDisposable? TryAcquire(Guid userId)
    {
        var config = Plugin.Instance?.Configuration;
        var maxPerUser = Math.Clamp(config?.MaxConcurrentPerUser ?? 2, 1, 10);
        var maxTotal = Math.Max(0, config?.MaxConcurrentTotal ?? 0);
        lock (_lock)
        {
            _perUser.TryGetValue(userId, out var mine);
            if (mine >= maxPerUser || (maxTotal > 0 && _total >= maxTotal))
            {
                return null;
            }

            _perUser[userId] = mine + 1;
            _total++;
        }

        return new Lease(this, userId);
    }

    /// <summary>Attend si l'utilisateur dépasse son débit maximum.</summary>
    public ValueTask ThrottleAsync(Guid userId, int bytes, CancellationToken cancellationToken)
    {
        var mbps = Plugin.Instance?.Configuration.MaxSpeedPerUserMbps ?? 0;
        if (mbps <= 0)
        {
            return ValueTask.CompletedTask;
        }

        var rate = mbps * 125_000d; // octets par seconde
        double wait;
        lock (_lock)
        {
            if (!_buckets.TryGetValue(userId, out var bucket))
            {
                bucket = new Bucket { Tokens = rate / 4, Last = Stopwatch.GetTimestamp() };
                _buckets[userId] = bucket;
            }

            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - bucket.Last) / (double)Stopwatch.Frequency;
            bucket.Last = now;
            bucket.Tokens = Math.Min(rate / 4, bucket.Tokens + (elapsed * rate)) - bytes;
            wait = bucket.Tokens < 0 ? -bucket.Tokens / rate : 0;
        }

        return wait > 0
            ? new ValueTask(Task.Delay(TimeSpan.FromSeconds(Math.Min(wait, 30)), cancellationToken))
            : ValueTask.CompletedTask;
    }

    private void Release(Guid userId)
    {
        lock (_lock)
        {
            if (_perUser.TryGetValue(userId, out var mine))
            {
                if (mine <= 1)
                {
                    _perUser.Remove(userId);
                    _buckets.Remove(userId);
                }
                else
                {
                    _perUser[userId] = mine - 1;
                }
            }

            _total = Math.Max(0, _total - 1);
        }
    }

    private sealed class Bucket
    {
        public double Tokens { get; set; }

        public long Last { get; set; }
    }

    private sealed class Lease : IDisposable
    {
        private TransferLimiter? _owner;
        private readonly Guid _userId;

        public Lease(TransferLimiter owner, Guid userId)
        {
            _owner = owner;
            _userId = userId;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_userId);
        }
    }
}
