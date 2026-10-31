using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.ProDownloader.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ProDownloader.Services;

/// <summary>Un fichier d'une tâche : sa destination (relative) et le fichier physique validé à la création.</summary>
public sealed class JobFile
{
    public Guid ItemId { get; set; }

    public int Part { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string PhysicalPath { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTime ModifiedUtc { get; set; }

    [JsonIgnore]
    public TransferState State { get; } = new();
}

public sealed class JobZip
{
    public string Name { get; set; } = string.Empty;

    public List<int> Files { get; set; } = [];

    [JsonIgnore]
    public TransferState State { get; } = new();

    [JsonIgnore]
    public ZipLayout? Layout { get; set; }
}

/// <summary>
/// Une tâche de téléchargement. Son identifiant (128 bits aléatoires) sert de clé d'accès aux liens de
/// téléchargement, que le navigateur ouvre sans en-tête d'authentification (téléchargements natifs).
/// </summary>
public sealed class Job
{
    public string Id { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime LastActivityUtc { get; set; }

    public List<JobFile> Files { get; set; } = [];

    public List<JobZip> Zips { get; set; } = [];

    /// <summary>Annulée à la suppression de la tâche : coupe les envois en cours.</summary>
    [JsonIgnore]
    public CancellationTokenSource Cancellation { get; } = new();
}

/// <summary>Tâches en mémoire, enregistrées en JSON pour survivre à un redémarrage (reprise d'un ZIP).</summary>
public class JobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly TimeSpan SaveActivityEvery = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _savedActivity = new(StringComparer.Ordinal);
    private readonly ILogger<JobStore> _logger;
    private readonly string _dir;
    private int _loaded;

    public JobStore(IApplicationPaths applicationPaths, ILogger<JobStore> logger)
    {
        _logger = logger;
        _dir = System.IO.Path.Combine(applicationPaths.DataPath, "pro-downloader", "jobs");
    }

    private static TimeSpan Expiration => TimeSpan.FromHours(Math.Clamp(Plugin.Instance?.Configuration.JobExpirationHours ?? 72, 1, 24 * 60));

    public int Count
    {
        get
        {
            EnsureLoaded();
            return _jobs.Count;
        }
    }

    public Job Add(Job job)
    {
        EnsureLoaded();
        job.Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        job.CreatedUtc = job.LastActivityUtc = DateTime.UtcNow;
        _jobs[job.Id] = job;
        Save(job);
        Purge();
        return job;
    }

    /// <summary>La tâche, si elle existe et n'a pas expiré. Marque une activité.</summary>
    public Job? Get(string id)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(id) || !_jobs.TryGetValue(id, out var job))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        if (now - job.LastActivityUtc > Expiration)
        {
            Remove(id);
            return null;
        }

        job.LastActivityUtc = now;
        if (!_savedActivity.TryGetValue(id, out var saved) || now - saved > SaveActivityEvery)
        {
            Save(job);
        }

        return job;
    }

    public void Remove(string id)
    {
        if (_jobs.TryRemove(id, out var job))
        {
            job.Cancellation.Cancel();
            _savedActivity.TryRemove(id, out _);
            try
            {
                File.Delete(FilePath(id));
            }
            catch (IOException)
            {
                // déjà supprimé
            }
        }
    }

    private void Purge()
    {
        var limit = DateTime.UtcNow - Expiration;
        foreach (var job in _jobs.Values.Where(j => j.LastActivityUtc < limit).ToList())
        {
            Remove(job.Id);
        }
    }

    private string FilePath(string id) => System.IO.Path.Combine(_dir, id + ".json");

    private void Save(Job job)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var tmp = FilePath(job.Id) + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(job, JsonOptions));
            File.Move(tmp, FilePath(job.Id), true);
            _savedActivity[job.Id] = job.LastActivityUtc;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jellyfin Pro Downloader: could not save job {Id}", job.Id);
        }
    }

    private void EnsureLoaded()
    {
        if (Interlocked.Exchange(ref _loaded, 1) != 0)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(_dir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
            {
                try
                {
                    var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(file), JsonOptions);
                    if (job is not null && !string.IsNullOrEmpty(job.Id))
                    {
                        _jobs[job.Id] = job;
                        _savedActivity[job.Id] = job.LastActivityUtc;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Jellyfin Pro Downloader: ignoring unreadable job file {File}", file);
                    File.Delete(file);
                }
            }

            Purge();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Jellyfin Pro Downloader: could not load saved jobs");
        }
    }
}
