using System.Globalization;
using System.Reflection;
using Jellyfin.Plugin.ProDownloader.Models;
using Jellyfin.Plugin.ProDownloader.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using User = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.ProDownloader.Controllers;

/// <summary>API du plugin : script client, sélection, tâches et liens de téléchargement.</summary>
[ApiController]
[Route(Plugin.RoutePrefix)]
public class ProDownloaderController : ControllerBase
{
    private const string ResourceName = "Jellyfin.Plugin.ProDownloader." + Plugin.ScriptFileName;
    private const int MaxFilesPerJob = 50_000;

    private readonly IUserManager _userManager;
    private readonly ItemResolver _resolver;
    private readonly JobStore _jobs;
    private readonly TransferLimiter _limiter;
    private readonly CrcCache _crcs;
    private readonly ILogger<ProDownloaderController> _logger;

    public ProDownloaderController(
        IUserManager userManager,
        ItemResolver resolver,
        JobStore jobs,
        TransferLimiter limiter,
        CrcCache crcs,
        ILogger<ProDownloaderController> logger)
    {
        _userManager = userManager;
        _resolver = resolver;
        _jobs = jobs;
        _limiter = limiter;
        _crcs = crcs;
        _logger = logger;
    }

    /// <summary>GET /ProDownloader/jellyfin-pro-downloader.js. Anonyme : chargé par index.html avant toute connexion.</summary>
    [HttpGet(Plugin.ScriptFileName)]
    [AllowAnonymous]
    public ActionResult GetScript()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return NotFound();
        }

        // L'URL porte la version du plugin (?v=) : on peut cacher longtemps.
        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return new FileStreamResult(stream, "text/javascript; charset=utf-8");
    }

    /// <summary>GET /ProDownloader/Info — réglages utiles au client pour l'utilisateur connecté.</summary>
    [HttpGet("Info")]
    [Authorize]
    public ActionResult<ClientInfo> GetInfo()
    {
        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        return new ClientInfo
        {
            Version = Plugin.ScriptVersion,
            CanDownload = ItemResolver.UserCanDownload(CurrentUser()),
            AllowFolderMode = config.AllowFolderMode,
            AllowQueueMode = config.AllowQueueMode || (!config.AllowFolderMode && !config.AllowZipMode),
            AllowZipMode = config.AllowZipMode,
            MaxConcurrent = Math.Clamp(config.MaxConcurrentPerUser, 1, 10)
        };
    }

    /// <summary>POST /ProDownloader/Resolve — arborescence de la fenêtre de sélection.</summary>
    [HttpPost("Resolve")]
    [Authorize]
    public ActionResult<ResolveResult> Resolve([FromBody] ResolveRequest request)
    {
        var user = CurrentUser();
        if (!ItemResolver.UserCanDownload(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request.ItemIds.Length == 0 || request.ItemIds.Length > 10_000)
        {
            return BadRequest();
        }

        return _resolver.Resolve(user!, request.ItemIds);
    }

    /// <summary>POST /ProDownloader/Jobs — crée une tâche à partir de fichiers choisis dans la fenêtre de sélection.</summary>
    [HttpPost("Jobs")]
    [Authorize]
    public ActionResult<JobInfo> CreateJob([FromBody] CreateJobRequest request)
    {
        var user = CurrentUser();
        if (!ItemResolver.UserCanDownload(user))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request.Files.Count == 0 || request.Files.Count > MaxFilesPerJob)
        {
            return BadRequest("Invalid file count");
        }

        var job = new Job { UserId = user!.Id, Name = ItemResolver.Names.Sanitize(request.Name) };
        var items = new Dictionary<Guid, (MediaBrowser.Controller.Entities.BaseItem? Item, IReadOnlyList<SourceFile> Files)>();
        foreach (var f in request.Files)
        {
            if (!items.TryGetValue(f.ItemId, out var entry))
            {
                var item = _resolver.GetVisibleItem(f.ItemId, user);
                entry = (item, item is null ? [] : _resolver.GetItemFiles(item));
                items[f.ItemId] = entry;
            }

            if (entry.Item is null || f.Part < 0 || f.Part >= entry.Files.Count)
            {
                return BadRequest("Unknown file " + f.ItemId.ToString("N") + "/" + f.Part.ToString(CultureInfo.InvariantCulture));
            }

            var source = entry.Files[f.Part];
            if (source.Kind == "media" && !entry.Item.CanDownload(user))
            {
                return StatusCode(StatusCodes.Status403Forbidden);
            }

            job.Files.Add(new JobFile
            {
                ItemId = f.ItemId,
                Part = f.Part,
                Kind = source.Kind,
                Path = ItemResolver.Names.SanitizeRelative(f.Path),
                PhysicalPath = source.PhysicalPath,
                Size = source.Size,
                ModifiedUtc = source.ModifiedUtc
            });
        }

        foreach (var zip in request.Zips)
        {
            var indexes = zip.Files.Distinct().ToList();
            if (indexes.Count == 0 || indexes.Any(i => i < 0 || i >= job.Files.Count))
            {
                return BadRequest("Invalid zip");
            }

            job.Zips.Add(new JobZip { Name = ItemResolver.Names.Sanitize(zip.Name), Files = indexes });
        }

        _jobs.Add(job);
        return ToInfo(job);
    }

    /// <summary>GET /ProDownloader/Jobs/{id} — état de la tâche (octets envoyés, fichiers terminés).</summary>
    [HttpGet("Jobs/{id}")]
    [Authorize]
    public ActionResult<JobInfo> GetJob([FromRoute] string id)
    {
        var job = _jobs.Get(id);
        var user = CurrentUser();
        if (job is null || user is null || job.UserId != user.Id)
        {
            return NotFound();
        }

        return ToInfo(job);
    }

    /// <summary>DELETE /ProDownloader/Jobs/{id}.</summary>
    [HttpDelete("Jobs/{id}")]
    [Authorize]
    public ActionResult DeleteJob([FromRoute] string id)
    {
        var job = _jobs.Get(id);
        var user = CurrentUser();
        if (job is not null && user is not null && job.UserId == user.Id)
        {
            _jobs.Remove(id);
        }

        return NoContent();
    }

    /// <summary>GET /ProDownloader/Jobs/{id}/Files/{index}/{name} — un fichier (anonyme : l'identifiant de tâche fait office de clé).</summary>
    [HttpGet("Jobs/{id}/Files/{index:int}/{**name}")]
    [AllowAnonymous]
    public async Task<ActionResult> DownloadFile([FromRoute] string id, [FromRoute] int index)
    {
        var job = _jobs.Get(id);
        if (job is null || index < 0 || index >= job.Files.Count)
        {
            return NotFound();
        }

        var file = job.Files[index];
        var user = _userManager.GetUserById(job.UserId);
        if (!StillAllowed(user, file))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        using var lease = _limiter.TryAcquire(job.UserId);
        if (lease is null)
        {
            Transfers.MarkRejected(file.State);
            return TooBusy();
        }

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, job.Cancellation.Token);
        var ctx = new CopyContext(job.UserId, _limiter, file.State, cancel.Token);
        try
        {
            await Transfers.ServeAsync(HttpContext, new FileRangeSource(file), file.Path.Split('/')[^1], MimeTypes.GetMimeType(file.PhysicalPath), ctx).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            Transfers.MarkFailed(file.State);
            LogInterrupted(ex, job, file.Path);
        }

        return new EmptyResult();
    }

    /// <summary>GET /ProDownloader/Jobs/{id}/Zip/{index}/{name} — un ZIP de la tâche, avec reprise.</summary>
    [HttpGet("Jobs/{id}/Zip/{index:int}/{**name}")]
    [AllowAnonymous]
    public async Task<ActionResult> DownloadZip([FromRoute] string id, [FromRoute] int index)
    {
        var job = _jobs.Get(id);
        if (job is null || index < 0 || index >= job.Zips.Count)
        {
            return NotFound();
        }

        var zip = job.Zips[index];
        var files = zip.Files.Select(i => job.Files[i]).ToList();
        var user = _userManager.GetUserById(job.UserId);
        if (files.Any(f => !StillAllowed(user, f)))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        using var lease = _limiter.TryAcquire(job.UserId);
        if (lease is null)
        {
            Transfers.MarkRejected(zip.State);
            return TooBusy();
        }

        zip.Layout ??= ZipLayout.Build(DedupeForZip(files));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, job.Cancellation.Token);
        var ctx = new CopyContext(job.UserId, _limiter, zip.State, cancel.Token);
        try
        {
            await Transfers.ServeAsync(HttpContext, new ZipRangeSource(zip.Layout, _crcs, job.Id, index), zip.Name + ".zip", "application/zip", ctx).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            Transfers.MarkFailed(zip.State);
            LogInterrupted(ex, job, zip.Name + ".zip");
        }

        return new EmptyResult();
    }

    /// <summary>GET /ProDownloader/Status — pour la page de configuration.</summary>
    [HttpGet("Status")]
    [Authorize(Policy = "RequiresElevation")]
    public ActionResult<object> GetStatus()
    {
        return new
        {
            version = Plugin.ScriptVersion,
            enabled = Plugin.Instance?.Configuration.Enabled ?? false,
            activeTransfers = _limiter.ActiveTotal,
            jobs = _jobs.Count
        };
    }

    /// <summary>Noms uniques à l'intérieur d'un même ZIP (le client peut avoir mélangé plusieurs sélections).</summary>
    private static List<JobFile> DedupeForZip(List<JobFile> files)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<JobFile>(files.Count);
        foreach (var f in files)
        {
            var path = f.Path;
            var ext = Path.GetExtension(path);
            for (var n = 2; !used.Add(path); n++)
            {
                path = f.Path[..^ext.Length] + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + ext;
            }

            result.Add(path == f.Path ? f : new JobFile
            {
                ItemId = f.ItemId,
                Part = f.Part,
                Kind = f.Kind,
                Path = path,
                PhysicalPath = f.PhysicalPath,
                Size = f.Size,
                ModifiedUtc = f.ModifiedUtc
            });
        }

        return result;
    }

    private static JobInfo ToInfo(Job job)
    {
        var info = new JobInfo { Id = job.Id, Name = job.Name };
        for (var i = 0; i < job.Files.Count; i++)
        {
            var f = job.Files[i];
            info.Files.Add(new JobFileInfo
            {
                Path = f.Path,
                Size = f.Size,
                Url = Plugin.RoutePrefix + "/Jobs/" + job.Id + "/Files/" + i.ToString(CultureInfo.InvariantCulture) + "/" + Uri.EscapeDataString(f.Path.Split('/')[^1]),
                State = Snapshot(f.State)
            });
        }

        for (var i = 0; i < job.Zips.Count; i++)
        {
            var z = job.Zips[i];
            z.Layout ??= ZipLayout.Build(DedupeForZip(z.Files.Select(n => job.Files[n]).ToList()));
            info.Zips.Add(new ZipPartInfo
            {
                Name = z.Name + ".zip",
                Size = z.Layout.Length,
                FileCount = z.Files.Count,
                Url = Plugin.RoutePrefix + "/Jobs/" + job.Id + "/Zip/" + i.ToString(CultureInfo.InvariantCulture) + "/" + Uri.EscapeDataString(z.Name + ".zip"),
                State = Snapshot(z.State)
            });
        }

        return info;
    }

    private static TransferState Snapshot(TransferState s)
    {
        lock (s)
        {
            return new TransferState { Status = s.Status, Sent = s.Sent, Position = s.Position, Requests = s.Requests };
        }
    }

    private ObjectResult TooBusy()
    {
        Response.Headers["Retry-After"] = "5";
        return StatusCode(StatusCodes.Status429TooManyRequests, "Too many simultaneous downloads");
    }

    /// <summary>Compte toujours actif et autorisé, élément toujours visible (droits revus depuis la création de la tâche).</summary>
    private bool StillAllowed(User? user, JobFile file)
    {
        if (!ItemResolver.UserCanDownload(user))
        {
            return false;
        }

        var item = _resolver.GetVisibleItem(file.ItemId, user!);
        return item is not null && (file.Kind != "media" || item.CanDownload(user!));
    }

    private User? CurrentUser()
    {
        var claim = User.Claims.FirstOrDefault(c => c.Type == "Jellyfin-UserId")?.Value;
        return Guid.TryParse(claim, out var id) && id != Guid.Empty ? _userManager.GetUserById(id) : null;
    }

    private void LogInterrupted(Exception ex, Job job, string what)
    {
        if (ex is OperationCanceledException)
        {
            _logger.LogDebug("Jellyfin Pro Downloader: download interrupted by the client ({What}, job {Id})", what, job.Id);
        }
        else
        {
            _logger.LogWarning(ex, "Jellyfin Pro Downloader: download failed ({What}, job {Id})", what, job.Id);
        }
    }
}
