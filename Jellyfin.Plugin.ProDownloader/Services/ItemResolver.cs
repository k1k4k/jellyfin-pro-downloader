using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.ProDownloader.Models;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using User = Jellyfin.Database.Implementations.Entities.User;

namespace Jellyfin.Plugin.ProDownloader.Services;

/// <summary>Un fichier physique téléchargeable rattaché à un élément (le média ou une annexe).</summary>
public sealed record SourceFile(string PhysicalPath, string Kind, long Size, DateTime ModifiedUtc);

/// <summary>
/// Transforme des éléments Jellyfin (série, saison, album, artiste, collection, playlist, dossier ou
/// élément seul) en arborescence de fichiers avec leurs chemins de destination, en ne gardant que ce
/// que l'utilisateur a le droit de voir et de télécharger.
/// </summary>
public class ItemResolver
{
    private static readonly string[] LyricExtensions = [".lrc", ".elrc"];
    private static readonly char[] Separators = ['/', '\\'];

    private readonly ILibraryManager _libraryManager;
    private readonly string[] _internalRoots;

    public ItemResolver(ILibraryManager libraryManager, IServerApplicationPaths applicationPaths)
    {
        _libraryManager = libraryManager;
        // Images que Jellyfin range dans ses propres dossiers (métadonnées, cache) : ce ne sont pas des fichiers « à côté du média ».
        _internalRoots = new[] { applicationPaths.ProgramDataPath, applicationPaths.InternalMetadataPath, applicationPaths.CachePath, applicationPaths.DataPath }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(NormalizeDir)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>L'utilisateur peut télécharger (droit du profil, compte actif).</summary>
    public static bool UserCanDownload(User? user)
    {
        return user is not null
            && !user.HasPermission(PermissionKind.IsDisabled)
            && user.HasPermission(PermissionKind.EnableContentDownloading);
    }

    /// <summary>Élément visible par l'utilisateur, ou null.</summary>
    public BaseItem? GetVisibleItem(Guid id, User user)
    {
        var item = _libraryManager.GetItemById(id);
        if (item is null)
        {
            return null;
        }

        return item.IsVisibleStandalone(user) ? item : null;
    }

    /// <summary>
    /// Fichiers d'un élément, dans un ordre stable : le média d'abord s'il y en a un (élément non dossier,
    /// fichier présent), puis les annexes triées par chemin. L'indice dans cette liste est le « Part »
    /// échangé avec le client.
    /// </summary>
    public IReadOnlyList<SourceFile> GetItemFiles(BaseItem item)
    {
        var files = new List<SourceFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path, string kind)
        {
            if (string.IsNullOrEmpty(path) || !seen.Add(path))
            {
                return;
            }

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return;
            }

            files.Add(new SourceFile(info.FullName, kind, info.Length, info.LastWriteTimeUtc));
        }

        var sidecars = new List<(string Path, string Kind)>();
        if (!item.IsFolder)
        {
            if (!IsDownloadableFile(item))
            {
                return files;
            }

            Add(item.Path, "media");
            var dir = Path.GetDirectoryName(item.Path)!;
            var baseName = Path.GetFileNameWithoutExtension(item.Path);

            foreach (var stream in SafeMediaStreams(item))
            {
                if (stream.IsExternal && !string.IsNullOrEmpty(stream.Path))
                {
                    sidecars.Add((stream.Path, stream.Type == MediaStreamType.Subtitle ? "subtitle" : "audio"));
                }
            }

            sidecars.Add((Path.Combine(dir, baseName + ".nfo"), "nfo"));
            if (item is Movie)
            {
                sidecars.Add((Path.Combine(dir, "movie.nfo"), "nfo"));
            }

            if (item is Audio)
            {
                foreach (var ext in LyricExtensions)
                {
                    sidecars.Add((Path.Combine(dir, baseName + ext), "lyrics"));
                }
            }

            AddLocalImages(item, dir, sidecars);
        }
        else
        {
            var dir = GetOwnFolder(item);
            if (dir is not null)
            {
                var nfo = item switch
                {
                    Series => "tvshow.nfo",
                    Season => "season.nfo",
                    MusicAlbum => "album.nfo",
                    MusicArtist => "artist.nfo",
                    _ => null
                };
                if (nfo is not null)
                {
                    sidecars.Add((Path.Combine(dir, nfo), "nfo"));
                }

                AddLocalImages(item, dir, sidecars);
            }

            // Affiches de saison rangées dans le dossier de la série (season01-poster.jpg).
            if (item is Season season && season.Series is { } series && GetOwnFolder(series) is { } seriesDir)
            {
                AddLocalImages(item, seriesDir, sidecars);
            }
        }

        foreach (var (path, kind) in sidecars.OrderBy(s => s.Path, StringComparer.Ordinal))
        {
            Add(path, kind);
        }

        return files;
    }

    /// <summary>Arborescence de la fenêtre de sélection pour un ou plusieurs éléments.</summary>
    public ResolveResult Resolve(User user, IReadOnlyList<Guid> ids)
    {
        var builder = new TreeBuilder(this);
        var roots = new List<BaseItem>();
        foreach (var id in ids.Distinct())
        {
            var item = GetVisibleItem(id, user);
            if (item is null)
            {
                continue;
            }

            roots.Add(item);
            Expand(item, user, builder, string.Empty);
        }

        var result = builder.Build();
        result.Title = roots.Count == 1 ? roots[0].Name ?? string.Empty : DescribeMany(roots);
        return result;
    }

    /// <summary>Le fichier du média existe et peut être servi tel quel (pas un dossier DVD / Blu-ray, pas une URL).</summary>
    internal static bool IsDownloadableFile(BaseItem item)
    {
        return !item.IsFolder
            && !item.IsVirtualItem
            && item.IsFileProtocol
            && !string.IsNullOrEmpty(item.Path)
            && File.Exists(item.Path);
    }

    private static string DescribeMany(List<BaseItem> roots)
    {
        if (roots.Count == 0)
        {
            return string.Empty;
        }

        // Plusieurs éléments d'une même série / d'un même album : on garde le nom commun.
        var seriesNames = roots.Select(r => (r as Episode)?.SeriesName ?? (r as Season)?.SeriesName).Distinct().ToList();
        if (seriesNames.Count == 1 && seriesNames[0] is { Length: > 0 } seriesName)
        {
            return seriesName;
        }

        var albums = roots.Select(r => (r as Audio)?.Album).Distinct().ToList();
        if (albums.Count == 1 && albums[0] is { Length: > 0 } album)
        {
            return album;
        }

        return roots[0].Name + " (+" + (roots.Count - 1).ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static IEnumerable<MediaStream> SafeMediaStreams(BaseItem item)
    {
        try
        {
            return item.GetMediaStreams() ?? (IEnumerable<MediaStream>)Array.Empty<MediaStream>();
        }
        catch (Exception)
        {
            return Array.Empty<MediaStream>();
        }
    }

    private static string NormalizeDir(string path)
    {
        var full = Path.GetFullPath(path);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }

    private static bool IsUnder(string path, string dir)
    {
        return Path.GetFullPath(path).StartsWith(NormalizeDir(dir), StringComparison.OrdinalIgnoreCase);
    }

    private static Guid[] Ids(params Guid[] ids) => ids;

    private bool IsInternal(string path)
    {
        var full = Path.GetFullPath(path);
        return _internalRoots.Any(r => full.StartsWith(r, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Dossier physique propre à un élément dossier (série, saison, album…), hors dossiers internes de Jellyfin.</summary>
    private string? GetOwnFolder(BaseItem item)
    {
        if (!item.IsFileProtocol || string.IsNullOrEmpty(item.Path) || !Directory.Exists(item.Path) || IsInternal(item.Path))
        {
            return null;
        }

        return item.Path;
    }

    private void AddLocalImages(BaseItem item, string dir, List<(string Path, string Kind)> sidecars)
    {
        foreach (var image in item.ImageInfos ?? [])
        {
            if (image.IsLocalFile && !string.IsNullOrEmpty(image.Path) && IsUnder(image.Path, dir) && !IsInternal(image.Path))
            {
                sidecars.Add((image.Path, "image"));
            }
        }
    }

    private List<BaseItem> Query(InternalItemsQuery query)
    {
        return _libraryManager.GetItemList(query).ToList();
    }

    private void Expand(BaseItem item, User user, TreeBuilder builder, string prefix)
    {
        switch (item)
        {
            case Series series:
                ExpandSeries(series, null, user, builder, prefix);
                break;
            case Season season:
                if (season.Series is { } parentSeries)
                {
                    ExpandSeries(parentSeries, season, user, builder, prefix);
                }

                break;
            case MusicAlbum album:
                ExpandAlbum(album, user, builder, Names.Join(prefix, Names.AlbumFolder(album)));
                break;
            case MusicArtist artist:
                ExpandArtist(artist, user, builder, prefix);
                break;
            case Playlist playlist:
                ExpandPlaylist(playlist, user, builder, prefix);
                break;
            case BoxSet boxSet:
                ExpandBoxSet(boxSet, user, builder, prefix);
                break;
            case Folder folder when folder is not ICollectionFolder && folder is not UserView:
                ExpandFolder(folder, user, builder, prefix);
                break;
            case Folder:
                break;
            default:
                AddLoose(item, user, builder, prefix);
                break;
        }
    }

    private void ExpandSeries(Series series, Season? onlySeason, User user, TreeBuilder builder, string prefix)
    {
        var seriesOut = Names.Join(prefix, Names.ItemFolder(series));
        var seriesDir = GetOwnFolder(series);
        builder.AddTreeExtras(series, user, seriesOut, seriesDir);

        var dto = new DtoOptions(false);
        IEnumerable<BaseItem> episodes = onlySeason is null
            ? series.GetEpisodes(user, dto, false)
            : onlySeason.GetEpisodes(user, dto, false);

        var seasons = Query(new InternalItemsQuery(user)
        {
            AncestorIds = Ids(series.Id),
            IncludeItemTypes = [BaseItemKind.Season],
            Recursive = true
        }).OfType<Season>().ToList();

        foreach (var episode in episodes.OfType<Episode>()
                     .Where(e => !e.IsVirtualItem)
                     .OrderBy(e => e.ParentIndexNumber ?? int.MaxValue)
                     .ThenBy(e => e.IndexNumber ?? int.MaxValue)
                     .ThenBy(e => e.SortName, StringComparer.OrdinalIgnoreCase))
        {
            var number = episode.ParentIndexNumber ?? episode.Season?.IndexNumber;
            var season = (onlySeason is not null ? onlySeason : null)
                ?? seasons.FirstOrDefault(s => s.Id == episode.SeasonId)
                ?? seasons.FirstOrDefault(s => s.IndexNumber == number);
            var seasonOut = Names.Join(seriesOut, Names.SeasonFolder(number));
            var groupKey = series.Id.ToString("N") + ":" + (number?.ToString(CultureInfo.InvariantCulture) ?? "x");
            var groupName = season?.Name ?? (number is null ? series.Name : "Season " + number.Value.ToString(CultureInfo.InvariantCulture));
            var zipName = series.Name + " - " + Names.SeasonFolder(number);
            var group = builder.Group(groupKey, groupName, zipName);
            if (season is not null && group.Extras.Count == 0)
            {
                builder.AddGroupExtras(group, season, user, [(GetOwnFolder(season), seasonOut), (seriesDir, seriesOut)]);
            }

            builder.AddEntry(group, episode, user, seasonOut, Names.EpisodeLabel(episode), null);
        }
    }

    private void ExpandAlbum(MusicAlbum album, User user, TreeBuilder builder, string albumOut)
    {
        var albumDir = GetOwnFolder(album);
        var group = builder.Group(album.Id.ToString("N"), Names.AlbumTitle(album), Names.AlbumTitle(album));
        builder.AddGroupExtras(group, album, user, [(albumDir, albumOut)]);

        var tracks = Query(new InternalItemsQuery(user)
        {
            AncestorIds = Ids(album.Id),
            IncludeItemTypes = [BaseItemKind.Audio],
            Recursive = true,
            IsVirtualItem = false
        }).OfType<Audio>()
            .OrderBy(t => t.ParentIndexNumber ?? 0)
            .ThenBy(t => t.IndexNumber ?? int.MaxValue)
            .ThenBy(t => t.SortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var multiDisc = tracks.Select(t => t.ParentIndexNumber ?? 0).Distinct().Count() > 1;

        foreach (var track in tracks)
        {
            // Sous-dossiers de disque (CD1/, CD2/) conservés.
            var outDir = albumOut;
            if (albumDir is not null && track.Path is { } trackPath && IsUnder(trackPath, albumDir))
            {
                var rel = Path.GetRelativePath(albumDir, Path.GetDirectoryName(trackPath)!);
                if (rel != ".")
                {
                    outDir = Names.Join(albumOut, Names.SanitizeRelative(rel));
                }
            }

            builder.AddEntry(group, track, user, outDir, Names.TrackLabel(track, multiDisc), null);
        }
    }

    private void ExpandArtist(MusicArtist artist, User user, TreeBuilder builder, string prefix)
    {
        var artistOut = Names.Join(prefix, Names.Sanitize(artist.Name));
        builder.AddTreeExtras(artist, user, artistOut, GetOwnFolder(artist));

        // Par l'artiste de l'album (tags) et, pour un artiste qui est un dossier, par les albums rangés dedans
        // (fichiers sans tag « artiste de l'album »).
        var albums = Query(new InternalItemsQuery(user)
        {
            AlbumArtistIds = Ids(artist.Id),
            IncludeItemTypes = [BaseItemKind.MusicAlbum],
            Recursive = true
        });
        if (GetOwnFolder(artist) is not null)
        {
            albums.AddRange(Query(new InternalItemsQuery(user)
            {
                AncestorIds = Ids(artist.Id),
                IncludeItemTypes = [BaseItemKind.MusicAlbum],
                Recursive = true
            }));
        }

        var ordered = albums.OfType<MusicAlbum>()
            .DistinctBy(a => a.Id)
            .OrderBy(a => a.ProductionYear ?? int.MaxValue)
            .ThenBy(a => a.SortName, StringComparer.OrdinalIgnoreCase);

        foreach (var album in ordered)
        {
            ExpandAlbum(album, user, builder, Names.Join(artistOut, Names.Sanitize(album.Name)));
        }
    }

    private void ExpandPlaylist(Playlist playlist, User user, TreeBuilder builder, string prefix)
    {
        var outDir = Names.Join(prefix, Names.Sanitize(playlist.Name));
        var group = builder.Group(playlist.Id.ToString("N"), playlist.Name ?? string.Empty, playlist.Name ?? "Playlist");
        var children = playlist.GetChildren(user, true, new InternalItemsQuery(user)).Where(c => !c.IsFolder).ToList();
        var digits = Math.Max(2, children.Count.ToString(CultureInfo.InvariantCulture).Length);
        var index = 0;
        foreach (var child in children)
        {
            index++;
            var number = index.ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            builder.AddEntry(group, child, user, outDir, number, number + " - ");
        }
    }

    private void ExpandBoxSet(BoxSet boxSet, User user, TreeBuilder builder, string prefix)
    {
        var outDir = Names.Join(prefix, Names.Sanitize(boxSet.Name));
        TreeGroup? loose = null;
        foreach (var child in boxSet.GetChildren(user, true, new InternalItemsQuery(user))
                     .OrderBy(c => c.ProductionYear ?? int.MaxValue)
                     .ThenBy(c => c.SortName, StringComparer.OrdinalIgnoreCase))
        {
            if (child.IsFolder)
            {
                Expand(child, user, builder, outDir);
                continue;
            }

            loose ??= builder.Group(boxSet.Id.ToString("N"), boxSet.Name ?? string.Empty, boxSet.Name ?? "Collection");
            builder.AddEntry(loose, child, user, Names.Join(outDir, Names.ItemFolder(child)), child.ProductionYear?.ToString(CultureInfo.InvariantCulture), null);
        }
    }

    private void ExpandFolder(Folder folder, User user, TreeBuilder builder, string prefix)
    {
        var rootOut = Names.Join(prefix, Names.Sanitize(folder.Name));
        var rootDir = GetOwnFolder(folder);
        var items = Query(new InternalItemsQuery(user)
        {
            AncestorIds = Ids(folder.Id),
            IsFolder = false,
            Recursive = true,
            IsVirtualItem = false
        }).Where(i => !string.IsNullOrEmpty(i.Path))
            .OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var rel = rootDir is not null && IsUnder(item.Path, rootDir)
                ? Path.GetRelativePath(rootDir, Path.GetDirectoryName(item.Path)!)
                : ".";
            var outDir = rel == "." ? rootOut : Names.Join(rootOut, Names.SanitizeRelative(rel));
            var key = folder.Id.ToString("N") + ":" + rel;
            var name = rel == "." ? folder.Name ?? string.Empty : rel.Replace('\\', '/');
            var group = builder.Group(key, name, rel == "." ? folder.Name ?? "Dossier" : folder.Name + " - " + rel.Replace('\\', ' ').Replace('/', ' '));
            builder.AddEntry(group, item, user, outDir, null, null);
        }
    }

    /// <summary>Élément isolé (sélection multiple, repli) : rangé comme dans sa série / son album.</summary>
    private void AddLoose(BaseItem item, User user, TreeBuilder builder, string prefix)
    {
        switch (item)
        {
            case Episode episode when episode.Series is { } series:
            {
                var number = episode.ParentIndexNumber ?? episode.Season?.IndexNumber;
                var seasonOut = Names.Join(prefix, Names.ItemFolder(series), Names.SeasonFolder(number));
                var group = builder.Group(
                    series.Id.ToString("N") + ":" + (number?.ToString(CultureInfo.InvariantCulture) ?? "x"),
                    episode.SeasonName ?? series.Name ?? string.Empty,
                    series.Name + " - " + Names.SeasonFolder(number));
                builder.AddEntry(group, episode, user, seasonOut, Names.EpisodeLabel(episode), null);
                break;
            }

            case Audio audio:
            {
                var albumOut = Names.Join(prefix, Names.Sanitize(audio.AlbumArtists?.FirstOrDefault() ?? audio.Artists?.FirstOrDefault() ?? "Unknown"), Names.Sanitize(audio.Album ?? "Unknown"));
                var group = builder.Group("album:" + (audio.AlbumEntity?.Id.ToString("N") ?? audio.Album ?? string.Empty), audio.Album ?? audio.Name ?? string.Empty, audio.Album ?? audio.Name ?? "Audio");
                builder.AddEntry(group, audio, user, albumOut, Names.TrackLabel(audio, false), null);
                break;
            }

            case Movie or Video:
            {
                var group = builder.Group("loose:videos", item.GetType().Name == nameof(Movie) ? "Films" : "Videos", "Videos");
                builder.AddEntry(group, item, user, Names.Join(prefix, Names.ItemFolder(item)), item.ProductionYear?.ToString(CultureInfo.InvariantCulture), null);
                break;
            }

            default:
            {
                var group = builder.Group("loose:files", "Files", "Files");
                builder.AddEntry(group, item, user, prefix, null, null);
                break;
            }
        }
    }

    /// <summary>Accumule groupes et éléments, puis rend les chemins uniques.</summary>
    private sealed class TreeBuilder
    {
        private readonly ItemResolver _resolver;
        private readonly ResolveResult _result = new();
        private readonly Dictionary<string, TreeGroup> _groups = new(StringComparer.Ordinal);
        private readonly HashSet<Guid> _entries = [];
        private readonly HashSet<Guid> _extrasOf = [];

        public TreeBuilder(ItemResolver resolver)
        {
            _resolver = resolver;
        }

        public TreeGroup Group(string key, string name, string zipName)
        {
            if (!_groups.TryGetValue(key, out var group))
            {
                group = new TreeGroup(new ResolvedGroup { Key = key, Name = name, ZipName = Names.Sanitize(zipName) });
                _groups[key] = group;
                _result.Groups.Add(group.Dto);
            }

            return group;
        }

        public void AddTreeExtras(BaseItem container, User user, string outDir, string? physicalDir)
        {
            if (_extrasOf.Add(container.Id))
            {
                _result.Extras.AddRange(MapExtras(container, user, [(physicalDir, outDir)], outDir));
            }
        }

        public void AddGroupExtras(TreeGroup group, BaseItem container, User user, (string? Dir, string Out)[] roots)
        {
            if (_extrasOf.Add(container.Id))
            {
                group.Extras.AddRange(MapExtras(container, user, roots, roots[0].Out));
            }
        }

        public void AddEntry(TreeGroup group, BaseItem item, User user, string outDir, string? label, string? namePrefix)
        {
            if (!_entries.Add(item.Id))
            {
                return;
            }

            var entry = new ResolvedEntry { ItemId = item.Id, Name = item.Name ?? string.Empty, Label = label };
            var files = item.CanDownload(user) ? _resolver.GetItemFiles(item) : [];
            if (files.Count == 0 || files[0].Kind != "media")
            {
                entry.Unavailable = true;
            }
            else
            {
                var mediaDir = Path.GetDirectoryName(files[0].PhysicalPath)!;
                for (var part = 0; part < files.Count; part++)
                {
                    var file = files[part];
                    var rel = IsUnder(file.PhysicalPath, mediaDir)
                        ? Path.GetRelativePath(mediaDir, file.PhysicalPath)
                        : Path.GetFileName(file.PhysicalPath);
                    entry.Files.Add(new ResolvedFile
                    {
                        ItemId = item.Id,
                        Part = part,
                        Kind = file.Kind,
                        Path = Names.Join(outDir, Names.SanitizeRelative(rel, namePrefix)),
                        Size = file.Size
                    });
                }
            }

            group.Dto.Entries.Add(entry);
        }

        public ResolveResult Build()
        {
            // Chemins uniques (insensible à la casse, comme Windows et macOS) : « nom (2).ext ».
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Dedupe(ResolvedFile f)
            {
                var slash = f.Path.LastIndexOf('/');
                var dir = slash >= 0 ? f.Path[..(slash + 1)] : string.Empty;
                var fileName = f.Path[dir.Length..];
                var ext = Path.GetExtension(fileName);
                var stem = fileName[..^ext.Length];
                var path = f.Path;
                for (var n = 2; !used.Add(path); n++)
                {
                    path = dir + stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + ext;
                }

                f.Path = path;
            }

            foreach (var f in _result.Extras)
            {
                Dedupe(f);
            }

            foreach (var g in _result.Groups)
            {
                foreach (var f in g.Extras)
                {
                    Dedupe(f);
                }

                foreach (var e in g.Entries)
                {
                    foreach (var f in e.Files)
                    {
                        Dedupe(f);
                    }
                }
            }

            _result.Groups.RemoveAll(g => g.Entries.Count == 0);
            return _result;
        }

        private List<ResolvedFile> MapExtras(BaseItem container, User user, (string? Dir, string Out)[] roots, string fallbackOut)
        {
            var list = new List<ResolvedFile>();
            if (!ItemResolver.UserCanDownload(user))
            {
                return list;
            }

            var files = _resolver.GetItemFiles(container);
            for (var part = 0; part < files.Count; part++)
            {
                var file = files[part];
                var outPath = Names.Join(fallbackOut, Names.Sanitize(Path.GetFileName(file.PhysicalPath)));
                foreach (var (dir, outDir) in roots)
                {
                    if (dir is not null && IsUnder(file.PhysicalPath, dir))
                    {
                        outPath = Names.Join(outDir, Names.SanitizeRelative(Path.GetRelativePath(dir, file.PhysicalPath)));
                        break;
                    }
                }

                list.Add(new ResolvedFile { ItemId = container.Id, Part = part, Kind = file.Kind, Path = outPath, Size = file.Size });
            }

            return list;
        }
    }

    private sealed class TreeGroup
    {
        public TreeGroup(ResolvedGroup dto)
        {
            Dto = dto;
        }

        public ResolvedGroup Dto { get; }

        public List<ResolvedFile> Extras => Dto.Extras;
    }

    /// <summary>Noms de dossiers et de fichiers de destination.</summary>
    internal static class Names
    {
        private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        /// <summary>Un segment de chemin valide sous Windows, macOS et Linux.</summary>
        public static string Sanitize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "_";
            }

            // « Série : titre » / « Série: titre » → « Série - titre ».
            name = name.Replace(" : ", " - ", StringComparison.Ordinal).Replace(": ", " - ", StringComparison.Ordinal);
            var chars = name.Select(c => c switch
            {
                ':' => '-',
                '<' or '>' or '"' or '/' or '\\' or '|' or '?' or '*' => '_',
                _ when char.IsControl(c) => '_',
                _ => c
            }).ToArray();
            var s = new string(chars).Trim().TrimEnd('.', ' ');
            if (s.Length == 0 || s == "." || s == "..")
            {
                s = "_";
            }

            if (Reserved.Contains(Path.GetFileNameWithoutExtension(s)))
            {
                s = "_" + s;
            }

            // Marge pour « (2) » et le chemin complet : 150 caractères par segment.
            if (s.Length > 150)
            {
                var ext = Path.GetExtension(s);
                s = ext.Length is > 0 and < 12 ? s[..(150 - ext.Length)].TrimEnd() + ext : s[..150].TrimEnd();
            }

            return s;
        }

        /// <summary>Chemin relatif assaini segment par segment ; « .. » et segments vides retirés.</summary>
        public static string SanitizeRelative(string relative, string? fileNamePrefix = null)
        {
            var parts = relative.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p != "." && p != "..")
                .ToList();
            if (parts.Count == 0)
            {
                return "_";
            }

            if (!string.IsNullOrEmpty(fileNamePrefix))
            {
                parts[^1] = fileNamePrefix + parts[^1];
            }

            return string.Join('/', parts.Select(Sanitize));
        }

        public static string Join(params string[] parts)
        {
            return string.Join('/', parts.Where(p => !string.IsNullOrEmpty(p)));
        }

        /// <summary>« Nom (Année) » : dossier de série, de film…</summary>
        public static string ItemFolder(BaseItem item)
        {
            var name = item.Name ?? "_";
            if (item.ProductionYear is { } year && !name.Contains(year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                name += " (" + year.ToString(CultureInfo.InvariantCulture) + ")";
            }

            return Sanitize(name);
        }

        /// <summary>« Season 01 », « Specials » : noms reconnus par Jellyfin, Plex, Kodi.</summary>
        public static string SeasonFolder(int? number)
        {
            return number switch
            {
                null => "Season Unknown",
                0 => "Specials",
                _ => "Season " + number.Value.ToString("D2", CultureInfo.InvariantCulture)
            };
        }

        public static string AlbumTitle(MusicAlbum album)
        {
            var artist = album.AlbumArtists?.FirstOrDefault();
            return string.IsNullOrEmpty(artist) ? album.Name ?? "_" : artist + " - " + album.Name;
        }

        public static string AlbumFolder(MusicAlbum album)
        {
            var artist = album.AlbumArtists?.FirstOrDefault() ?? album.Artists?.FirstOrDefault();
            return string.IsNullOrEmpty(artist) ? Sanitize(album.Name) : Join(Sanitize(artist), Sanitize(album.Name));
        }

        public static string? EpisodeLabel(Episode episode)
        {
            if (episode.IndexNumber is not { } index)
            {
                return null;
            }

            var label = "S" + (episode.ParentIndexNumber ?? 0).ToString("D2", CultureInfo.InvariantCulture) + "E" + index.ToString("D2", CultureInfo.InvariantCulture);
            if (episode.IndexNumberEnd is { } end && end > index)
            {
                label += "-E" + end.ToString("D2", CultureInfo.InvariantCulture);
            }

            return label;
        }

        public static string? TrackLabel(BaseItem track, bool multiDisc)
        {
            if (track.IndexNumber is not { } index)
            {
                return null;
            }

            var n = index.ToString("D2", CultureInfo.InvariantCulture);
            return multiDisc && track.ParentIndexNumber is { } disc ? disc.ToString(CultureInfo.InvariantCulture) + "-" + n : n;
        }
    }
}
