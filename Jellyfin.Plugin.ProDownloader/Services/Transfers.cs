using System.Buffers;
using System.Globalization;
using Jellyfin.Plugin.ProDownloader.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.ProDownloader.Services;

/// <summary>Ce qui accompagne un envoi : utilisateur (limites), suivi, annulation.</summary>
public sealed class CopyContext
{
    public CopyContext(Guid userId, TransferLimiter limiter, TransferState state, CancellationToken token)
    {
        UserId = userId;
        Limiter = limiter;
        State = state;
        Token = token;
    }

    public Guid UserId { get; }

    public TransferLimiter Limiter { get; }

    public TransferState State { get; }

    public CancellationToken Token { get; }
}

/// <summary>Contenu servi par plages d'octets : un fichier ou un ZIP.</summary>
public interface IRangeSource
{
    long Length { get; }

    string ETag { get; }

    DateTime LastModifiedUtc { get; }

    /// <summary>Écrit les octets [start, end[.</summary>
    Task WriteAsync(Stream body, long start, long end, CopyContext ctx);
}

/// <summary>Envoi HTTP avec reprise (Range / If-Range, une seule plage).</summary>
public static class Transfers
{
    private const int BufferSize = 256 * 1024;

    public static async Task ServeAsync(HttpContext http, IRangeSource source, string fileName, string contentType, CopyContext ctx)
    {
        var request = http.Request;
        var response = http.Response;
        var length = source.Length;

        response.Headers[HeaderNames.AcceptRanges] = "bytes";
        response.Headers[HeaderNames.ETag] = source.ETag;
        response.Headers[HeaderNames.LastModified] = source.LastModifiedUtc.ToString("R", CultureInfo.InvariantCulture);
        response.Headers[HeaderNames.CacheControl] = "private, no-transform";
        // Déjà encodé : la compression de réponse de Jellyfin ne doit pas s'en mêler (et casserait les plages).
        response.Headers[HeaderNames.ContentEncoding] = "identity";
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(fileName);
        response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
        response.ContentType = contentType;

        long start = 0, end = length;
        var partial = false;
        var rangeHeader = request.Headers[HeaderNames.Range].ToString();
        var ifRange = request.Headers[HeaderNames.IfRange].ToString();
        if (!string.IsNullOrEmpty(rangeHeader) && (string.IsNullOrEmpty(ifRange) || IfRangeMatches(ifRange, source)))
        {
            var parsed = ParseRange(rangeHeader, length);
            if (parsed is null)
            {
                response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
                response.Headers[HeaderNames.ContentRange] = "bytes */" + length.ToString(CultureInfo.InvariantCulture);
                return;
            }

            if (parsed.Value.Start != 0 || parsed.Value.End != length)
            {
                (start, end) = parsed.Value;
                partial = true;
            }
        }

        response.StatusCode = partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
        response.ContentLength = end - start;
        if (partial)
        {
            response.Headers[HeaderNames.ContentRange] = "bytes " + start.ToString(CultureInfo.InvariantCulture) + "-"
                + (end - 1).ToString(CultureInfo.InvariantCulture) + "/" + length.ToString(CultureInfo.InvariantCulture);
        }

        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        lock (ctx.State)
        {
            ctx.State.Status = "active";
            ctx.State.Sent = 0;
            ctx.State.Position = start;
            ctx.State.Requests++;
        }

        if (HttpMethods.IsHead(request.Method) || end == start)
        {
            FinishOk(ctx.State, end, length);
            return;
        }

        await response.StartAsync(ctx.Token).ConfigureAwait(false);
        await source.WriteAsync(response.Body, start, end, ctx).ConfigureAwait(false);
        await response.Body.FlushAsync(ctx.Token).ConfigureAwait(false);
        FinishOk(ctx.State, end, length);
    }

    public static void MarkFailed(TransferState state)
    {
        lock (state)
        {
            state.Status = "failed";
        }
    }

    public static void MarkRejected(TransferState state)
    {
        lock (state)
        {
            state.Status = "rejected";
            state.Requests++;
        }
    }

    /// <summary>Copie [offset, offset+count[ d'un fichier vers la réponse, avec limite de débit, suivi et CRC facultatif.</summary>
    public static async Task CopyFileAsync(JobFile file, long offset, long count, Stream body, CopyContext ctx, CrcAccumulator? crc)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using var fs = new FileStream(file.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (fs.Length != file.Size)
            {
                throw new IOException("File changed since the download was prepared: " + file.Path);
            }

            fs.Seek(offset, SeekOrigin.Begin);
            var remaining = count;
            while (remaining > 0)
            {
                var want = (int)Math.Min(buffer.Length, remaining);
                var read = await fs.ReadAsync(buffer.AsMemory(0, want), ctx.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new IOException("Unexpected end of file: " + file.Path);
                }

                crc?.Add(buffer.AsSpan(0, read));
                await ctx.Limiter.ThrottleAsync(ctx.UserId, read, ctx.Token).ConfigureAwait(false);
                await body.WriteAsync(buffer.AsMemory(0, read), ctx.Token).ConfigureAwait(false);
                remaining -= read;
                AddSent(ctx.State, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task WriteBytesAsync(byte[] bytes, long segmentOffset, long start, long end, Stream body, CopyContext ctx)
    {
        var from = Math.Max(start, segmentOffset);
        var to = Math.Min(end, segmentOffset + bytes.Length);
        if (to <= from)
        {
            return;
        }

        var count = (int)(to - from);
        await ctx.Limiter.ThrottleAsync(ctx.UserId, count, ctx.Token).ConfigureAwait(false);
        await body.WriteAsync(bytes.AsMemory((int)(from - segmentOffset), count), ctx.Token).ConfigureAwait(false);
        AddSent(ctx.State, count);
    }

    /// <summary>« bytes=a-b », « bytes=a- », « bytes=-n » ; plusieurs plages : on sert tout (null = invalide).</summary>
    internal static (long Start, long End)? ParseRange(string header, long length)
    {
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return (0, length);
        }

        var spec = header[6..].Trim();
        if (spec.Contains(',', StringComparison.Ordinal))
        {
            return (0, length);
        }

        var dash = spec.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return (0, length);
        }

        var first = spec[..dash].Trim();
        var last = spec[(dash + 1)..].Trim();
        if (first.Length == 0)
        {
            if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0)
            {
                return null;
            }

            return (Math.Max(0, length - suffix), length);
        }

        if (!long.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var s) || s >= length)
        {
            return null;
        }

        var e = length;
        if (last.Length > 0)
        {
            if (!long.TryParse(last, NumberStyles.None, CultureInfo.InvariantCulture, out var l) || l < s)
            {
                return null;
            }

            e = Math.Min(length, l + 1);
        }

        return (s, e);
    }

    private static bool IfRangeMatches(string ifRange, IRangeSource source)
    {
        if (ifRange.StartsWith('"') || ifRange.StartsWith("W/", StringComparison.Ordinal))
        {
            return string.Equals(ifRange, source.ETag, StringComparison.Ordinal);
        }

        return DateTime.TryParse(ifRange, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
            && Math.Abs((date - source.LastModifiedUtc).TotalSeconds) < 1;
    }

    private static void AddSent(TransferState state, long n)
    {
        lock (state)
        {
            state.Sent += n;
            state.Position += n;
        }
    }

    private static void FinishOk(TransferState state, long end, long length)
    {
        lock (state)
        {
            state.Position = end;
            state.Status = end >= length ? "done" : "idle";
        }
    }
}

/// <summary>CRC-32 calculé au fil de l'envoi.</summary>
public sealed class CrcAccumulator
{
    public uint Value { get; private set; }

    public void Add(ReadOnlySpan<byte> data) => Value = Crc32.Append(Value, data);
}

/// <summary>Un fichier d'une tâche.</summary>
public sealed class FileRangeSource : IRangeSource
{
    private readonly JobFile _file;

    public FileRangeSource(JobFile file)
    {
        _file = file;
    }

    public long Length => _file.Size;

    public DateTime LastModifiedUtc => _file.ModifiedUtc;

    public string ETag => "\"" + _file.Size.ToString("x", CultureInfo.InvariantCulture) + "-" + _file.ModifiedUtc.Ticks.ToString("x", CultureInfo.InvariantCulture) + "\"";

    public Task WriteAsync(Stream body, long start, long end, CopyContext ctx)
    {
        return Transfers.CopyFileAsync(_file, start, end - start, body, ctx, null);
    }
}

/// <summary>Un ZIP d'une tâche, produit à la volée pour n'importe quelle plage.</summary>
public sealed class ZipRangeSource : IRangeSource
{
    private readonly ZipLayout _layout;
    private readonly CrcCache _crcs;
    private readonly string _etag;

    public ZipRangeSource(ZipLayout layout, CrcCache crcs, string jobId, int index)
    {
        _layout = layout;
        _crcs = crcs;
        // La disposition ne change pas tant que la tâche existe (tailles figées, fichiers revérifiés à l'envoi).
        _etag = "\"zip-" + jobId + "-" + index.ToString(CultureInfo.InvariantCulture) + "-" + layout.Length.ToString("x", CultureInfo.InvariantCulture) + "\"";
        LastModifiedUtc = layout.Entries.Count == 0 ? DateTime.UnixEpoch : layout.Entries.Max(e => e.File.ModifiedUtc);
    }

    public long Length => _layout.Length;

    public string ETag => _etag;

    public DateTime LastModifiedUtc { get; }

    public async Task WriteAsync(Stream body, long start, long end, CopyContext ctx)
    {
        var entries = _layout.Entries;
        var crcs = new Task<uint>?[entries.Count];
        var tailNeeded = end > _layout.CentralOffset;

        // CRC à relire sur disque : entrées dont le descripteur (ou le répertoire central) est dans la plage
        // mais dont les données ne sont pas envoyées en entier par cette requête.
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var f = e.File;
            if (_crcs.TryGet(f.PhysicalPath, f.Size, f.ModifiedUtc, out var known))
            {
                crcs[i] = Task.FromResult(known);
                continue;
            }

            var fullyStreamed = start <= e.DataOffset && end >= e.DescriptorOffset;
            var descriptorInRange = start < e.DescriptorOffset + e.DescriptorLength && end > e.DescriptorOffset;
            if (!fullyStreamed && (descriptorInRange || tailNeeded))
            {
                crcs[i] = _crcs.GetOrComputeAsync(f.PhysicalPath, f.Size, f.ModifiedUtc);
            }
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.DescriptorOffset + e.DescriptorLength <= start)
            {
                continue;
            }

            if (e.HeaderOffset >= end)
            {
                break;
            }

            await Transfers.WriteBytesAsync(ZipLayout.LocalHeader(e), e.HeaderOffset, start, end, body, ctx).ConfigureAwait(false);

            var from = Math.Max(start, e.DataOffset);
            var to = Math.Min(end, e.DescriptorOffset);
            if (to > from || e.File.Size == 0)
            {
                CrcAccumulator? acc = crcs[i] is null && from == e.DataOffset && to == e.DescriptorOffset ? new CrcAccumulator() : null;
                if (to > from)
                {
                    await Transfers.CopyFileAsync(e.File, from - e.DataOffset, to - from, body, ctx, acc).ConfigureAwait(false);
                }

                if (acc is not null)
                {
                    crcs[i] = Task.FromResult(acc.Value);
                    _crcs.Set(e.File.PhysicalPath, e.File.Size, e.File.ModifiedUtc, acc.Value);
                }
            }

            if (end > e.DescriptorOffset)
            {
                var crc = await (crcs[i] ?? _crcs.GetOrComputeAsync(e.File.PhysicalPath, e.File.Size, e.File.ModifiedUtc)).WaitAsync(ctx.Token).ConfigureAwait(false);
                crcs[i] = Task.FromResult(crc);
                await Transfers.WriteBytesAsync(ZipLayout.Descriptor(e, crc), e.DescriptorOffset, start, end, body, ctx).ConfigureAwait(false);
            }
        }

        if (tailNeeded)
        {
            var values = new uint[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var f = entries[i].File;
                values[i] = await (crcs[i] ?? _crcs.GetOrComputeAsync(f.PhysicalPath, f.Size, f.ModifiedUtc)).WaitAsync(ctx.Token).ConfigureAwait(false);
            }

            await Transfers.WriteBytesAsync(_layout.Tail(values), _layout.CentralOffset, start, end, body, ctx).ConfigureAwait(false);
        }
    }
}
