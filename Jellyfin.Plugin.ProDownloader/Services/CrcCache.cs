using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;

namespace Jellyfin.Plugin.ProDownloader.Services;

/// <summary>
/// CRC-32 des fichiers déjà envoyés dans un ZIP (clé : chemin, taille, date de modification).
/// Un ZIP repris en cours de route (requête Range) a besoin des CRC de fichiers qu'il ne renvoie pas :
/// ils viennent d'ici, ou sont recalculés en relisant le fichier, un à la fois pour ménager les disques.
/// </summary>
public class CrcCache
{
    private const int MaxEntries = 200_000;

    private readonly ConcurrentDictionary<string, uint> _known = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<uint>> _running = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _diskGate = new(1, 1);

    public bool TryGet(string path, long size, DateTime modifiedUtc, out uint crc)
    {
        return _known.TryGetValue(Key(path, size, modifiedUtc), out crc);
    }

    public void Set(string path, long size, DateTime modifiedUtc, uint crc)
    {
        if (_known.Count >= MaxEntries)
        {
            _known.Clear();
        }

        _known[Key(path, size, modifiedUtc)] = crc;
    }

    /// <summary>CRC connu, ou calcul en arrière-plan (partagé entre requêtes, indépendant de leur annulation).</summary>
    public Task<uint> GetOrComputeAsync(string path, long size, DateTime modifiedUtc)
    {
        var key = Key(path, size, modifiedUtc);
        if (_known.TryGetValue(key, out var crc))
        {
            return Task.FromResult(crc);
        }

        return _running.GetOrAdd(key, _ => Task.Run(async () =>
        {
            try
            {
                await _diskGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var value = await ComputeAsync(path, size).ConfigureAwait(false);
                    Set(path, size, modifiedUtc, value);
                    return value;
                }
                finally
                {
                    _diskGate.Release();
                }
            }
            finally
            {
                _running.TryRemove(key, out Task<uint>? _);
            }
        }));
    }

    private static async Task<uint> ComputeAsync(string path, long size)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (fs.Length != size)
            {
                throw new IOException("File changed since the download was prepared: " + path);
            }

            uint crc = 0;
            int read;
            while ((read = await fs.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
            {
                crc = Crc32.Append(crc, buffer.AsSpan(0, read));
            }

            return crc;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string Key(string path, long size, DateTime modifiedUtc)
    {
        return path + "|" + size.ToString(CultureInfo.InvariantCulture) + "|" + modifiedUtc.Ticks.ToString(CultureInfo.InvariantCulture);
    }
}
