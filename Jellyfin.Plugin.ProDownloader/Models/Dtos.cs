namespace Jellyfin.Plugin.ProDownloader.Models;

/// <summary>POST /ProDownloader/Resolve.</summary>
public sealed class ResolveRequest
{
    public Guid[] ItemIds { get; set; } = [];
}

/// <summary>Arborescence proposée dans la fenêtre de sélection : groupes (saison, album…) → éléments → fichiers.</summary>
public sealed class ResolveResult
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Annexes communes (affiche et .nfo de la série, de l'artiste…), jointes dès qu'un élément est choisi.</summary>
    public List<ResolvedFile> Extras { get; set; } = [];

    public List<ResolvedGroup> Groups { get; set; } = [];
}

public sealed class ResolvedGroup
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Nom proposé pour le ZIP de ce groupe (sans extension).</summary>
    public string ZipName { get; set; } = string.Empty;

    /// <summary>Annexes du groupe (affiche de saison, pochette d'album…), jointes dès qu'un élément du groupe est choisi.</summary>
    public List<ResolvedFile> Extras { get; set; } = [];

    public List<ResolvedEntry> Entries { get; set; } = [];
}

public sealed class ResolvedEntry
{
    public Guid ItemId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Repère court affiché devant le nom (S01E01, 01, 1-03…), facultatif.</summary>
    public string? Label { get; set; }

    /// <summary>Fichier absent, dossier (DVD / Blu-ray) ou téléchargement refusé : affiché mais non sélectionnable.</summary>
    public bool Unavailable { get; set; }

    /// <summary>Le média d'abord (Kind = media), puis ses annexes.</summary>
    public List<ResolvedFile> Files { get; set; } = [];
}

public sealed class ResolvedFile
{
    public Guid ItemId { get; set; }

    /// <summary>Rang du fichier dans la liste des fichiers de l'élément (voir ItemResolver.GetItemFiles).</summary>
    public int Part { get; set; }

    /// <summary>media, subtitle, audio, nfo, image, lyrics.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Chemin relatif de destination, séparateur « / ».</summary>
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }
}

/// <summary>POST /ProDownloader/Jobs.</summary>
public sealed class CreateJobRequest
{
    public string Name { get; set; } = string.Empty;

    public List<JobFileRequest> Files { get; set; } = [];

    /// <summary>ZIP à proposer : chacun liste des indices de <see cref="Files"/>. Vide = pas de ZIP.</summary>
    public List<ZipPartRequest> Zips { get; set; } = [];
}

public sealed class JobFileRequest
{
    public Guid ItemId { get; set; }

    public int Part { get; set; }

    public string Path { get; set; } = string.Empty;
}

public sealed class ZipPartRequest
{
    public string Name { get; set; } = string.Empty;

    public List<int> Files { get; set; } = [];
}

/// <summary>Réponse de POST /ProDownloader/Jobs et de GET /ProDownloader/Jobs/{id}.</summary>
public sealed class JobInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<JobFileInfo> Files { get; set; } = [];

    public List<ZipPartInfo> Zips { get; set; } = [];
}

public sealed class JobFileInfo
{
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>URL relative à la racine du serveur.</summary>
    public string Url { get; set; } = string.Empty;

    public TransferState State { get; set; } = new();
}

public sealed class ZipPartInfo
{
    public string Name { get; set; } = string.Empty;

    public long Size { get; set; }

    public int FileCount { get; set; }

    public string Url { get; set; } = string.Empty;

    public TransferState State { get; set; } = new();
}

/// <summary>Suivi côté serveur d'un fichier ou d'un ZIP : sert au mode File d'attente pour savoir quand lancer le suivant.</summary>
public sealed class TransferState
{
    /// <summary>idle, active, done, failed.</summary>
    public string Status { get; set; } = "idle";

    /// <summary>Octets envoyés par la requête en cours (ou la dernière).</summary>
    public long Sent { get; set; }

    /// <summary>Position de fin atteinte par la dernière requête (sert à afficher une progression après une reprise).</summary>
    public long Position { get; set; }

    public int Requests { get; set; }
}

/// <summary>GET /ProDownloader/Info.</summary>
public sealed class ClientInfo
{
    public string Version { get; set; } = string.Empty;

    public bool CanDownload { get; set; }

    public bool AllowFolderMode { get; set; }

    public bool AllowQueueMode { get; set; }

    public bool AllowZipMode { get; set; }

    public int MaxConcurrent { get; set; }
}
