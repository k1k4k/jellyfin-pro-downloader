using System.Buffers.Binary;
using System.Text;

namespace Jellyfin.Plugin.ProDownloader.Services;

/// <summary>
/// Disposition d'un ZIP « stored » (sans compression) entièrement déterminée par la liste des fichiers
/// et leurs tailles : la taille totale est connue avant d'envoyer le premier octet, et n'importe quelle
/// plage d'octets peut être produite (reprise). Seuls les CRC-32 dépendent du contenu ; ils sont écrits
/// dans les descripteurs de données (bit 3) et le répertoire central, donc après les données.
/// Zip64 pour toute entrée de 4 Gio ou plus ou placée au-delà de 4 Gio, enregistrements de fin Zip64 si
/// nécessaire. Noms en UTF-8 (bit 11).
/// </summary>
public sealed class ZipLayout
{
    private const uint Max32 = 0xFFFFFFFF;
    private const ushort Flags = 0x0808; // bit 3 : descripteur de données ; bit 11 : noms UTF-8

    private ZipLayout(List<Entry> entries, long centralOffset, long centralSize, bool zip64End, long length)
    {
        Entries = entries;
        CentralOffset = centralOffset;
        CentralSize = centralSize;
        Zip64End = zip64End;
        Length = length;
    }

    public List<Entry> Entries { get; }

    public long CentralOffset { get; }

    public long CentralSize { get; }

    public bool Zip64End { get; }

    /// <summary>Taille totale du ZIP en octets.</summary>
    public long Length { get; }

    /// <summary>Début des enregistrements de fin (Zip64 puis classique).</summary>
    public long EndOffset => CentralOffset + CentralSize;

    public static ZipLayout Build(IReadOnlyList<JobFile> files)
    {
        var entries = new List<Entry>(files.Count);
        long offset = 0;
        foreach (var file in files)
        {
            var name = Encoding.UTF8.GetBytes(file.Path);
            var bigSize = file.Size >= Max32;
            var bigOffset = offset >= Max32;
            var (time, date) = DosTime(file.ModifiedUtc);
            var entry = new Entry(file, name, offset, bigSize, bigOffset, time, date);
            entries.Add(entry);
            offset = entry.DescriptorOffset + entry.DescriptorLength;
        }

        var centralOffset = offset;
        long centralSize = entries.Sum(e => (long)e.CentralLength);
        var zip64End = entries.Count >= 0xFFFF || centralOffset >= Max32 || centralSize >= Max32;
        var length = centralOffset + centralSize + (zip64End ? 56 + 20 : 0) + 22;
        return new ZipLayout(entries, centralOffset, centralSize, zip64End, length);
    }

    public static byte[] LocalHeader(Entry e)
    {
        var b = new byte[e.HeaderLength];
        var s = b.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, 0x04034b50);
        BinaryPrimitives.WriteUInt16LittleEndian(s[4..], e.NeedsZip64 ? (ushort)45 : (ushort)20);
        BinaryPrimitives.WriteUInt16LittleEndian(s[6..], Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(s[8..], 0); // stored
        BinaryPrimitives.WriteUInt16LittleEndian(s[10..], e.Time);
        BinaryPrimitives.WriteUInt16LittleEndian(s[12..], e.Date);
        // CRC et tailles à zéro : ils suivent dans le descripteur (bit 3).
        BinaryPrimitives.WriteUInt16LittleEndian(s[26..], (ushort)e.Name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(s[28..], 0);
        e.Name.CopyTo(s[30..]);
        return b;
    }

    public static byte[] Descriptor(Entry e, uint crc)
    {
        var b = new byte[e.DescriptorLength];
        var s = b.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, 0x08074b50);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], crc);
        if (e.BigSize)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(s[8..], (ulong)e.File.Size);
            BinaryPrimitives.WriteUInt64LittleEndian(s[16..], (ulong)e.File.Size);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(s[8..], (uint)e.File.Size);
            BinaryPrimitives.WriteUInt32LittleEndian(s[12..], (uint)e.File.Size);
        }

        return b;
    }

    /// <summary>Répertoire central + enregistrements de fin, à partir des CRC de toutes les entrées.</summary>
    public byte[] Tail(IReadOnlyList<uint> crcs)
    {
        var b = new byte[Length - CentralOffset];
        var s = b.AsSpan();
        var p = 0;
        for (var i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            var h = s[p..];
            BinaryPrimitives.WriteUInt32LittleEndian(h, 0x02014b50);
            var version = e.NeedsZip64 ? (ushort)45 : (ushort)20;
            BinaryPrimitives.WriteUInt16LittleEndian(h[4..], version); // créé par : MS-DOS, spec 2.0 / 4.5
            BinaryPrimitives.WriteUInt16LittleEndian(h[6..], version);
            BinaryPrimitives.WriteUInt16LittleEndian(h[8..], Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(h[10..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(h[12..], e.Time);
            BinaryPrimitives.WriteUInt16LittleEndian(h[14..], e.Date);
            BinaryPrimitives.WriteUInt32LittleEndian(h[16..], crcs[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(h[20..], e.BigSize ? Max32 : (uint)e.File.Size);
            BinaryPrimitives.WriteUInt32LittleEndian(h[24..], e.BigSize ? Max32 : (uint)e.File.Size);
            BinaryPrimitives.WriteUInt16LittleEndian(h[28..], (ushort)e.Name.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(h[30..], (ushort)e.ExtraLength);
            // commentaire, disque, attributs : 0
            BinaryPrimitives.WriteUInt32LittleEndian(h[42..], e.BigOffset ? Max32 : (uint)e.HeaderOffset);
            e.Name.CopyTo(h[46..]);
            if (e.ExtraLength > 0)
            {
                var x = h[(46 + e.Name.Length)..];
                BinaryPrimitives.WriteUInt16LittleEndian(x, 0x0001);
                BinaryPrimitives.WriteUInt16LittleEndian(x[2..], (ushort)(e.ExtraLength - 4));
                var q = 4;
                if (e.BigSize)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(x[q..], (ulong)e.File.Size);
                    BinaryPrimitives.WriteUInt64LittleEndian(x[(q + 8)..], (ulong)e.File.Size);
                    q += 16;
                }

                if (e.BigOffset)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(x[q..], (ulong)e.HeaderOffset);
                }
            }

            p += e.CentralLength;
        }

        var count = Entries.Count;
        if (Zip64End)
        {
            var z = s[p..];
            BinaryPrimitives.WriteUInt32LittleEndian(z, 0x06064b50);
            BinaryPrimitives.WriteUInt64LittleEndian(z[4..], 44);
            BinaryPrimitives.WriteUInt16LittleEndian(z[12..], 45);
            BinaryPrimitives.WriteUInt16LittleEndian(z[14..], 45);
            BinaryPrimitives.WriteUInt64LittleEndian(z[24..], (ulong)count);
            BinaryPrimitives.WriteUInt64LittleEndian(z[32..], (ulong)count);
            BinaryPrimitives.WriteUInt64LittleEndian(z[40..], (ulong)CentralSize);
            BinaryPrimitives.WriteUInt64LittleEndian(z[48..], (ulong)CentralOffset);
            p += 56;

            var l = s[p..];
            BinaryPrimitives.WriteUInt32LittleEndian(l, 0x07064b50);
            BinaryPrimitives.WriteUInt64LittleEndian(l[8..], (ulong)EndOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(l[16..], 1);
            p += 20;
        }

        var end = s[p..];
        BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054b50);
        var count16 = count >= 0xFFFF ? (ushort)0xFFFF : (ushort)count;
        BinaryPrimitives.WriteUInt16LittleEndian(end[8..], count16);
        BinaryPrimitives.WriteUInt16LittleEndian(end[10..], count16);
        BinaryPrimitives.WriteUInt32LittleEndian(end[12..], CentralSize >= Max32 ? Max32 : (uint)CentralSize);
        BinaryPrimitives.WriteUInt32LittleEndian(end[16..], CentralOffset >= Max32 ? Max32 : (uint)CentralOffset);
        return b;
    }

    private static (ushort Time, ushort Date) DosTime(DateTime utc)
    {
        var t = utc.ToLocalTime();
        if (t.Year < 1980)
        {
            t = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);
        }

        if (t.Year > 2107)
        {
            t = new DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Local);
        }

        var time = (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
        var date = (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
        return (time, date);
    }

    public sealed class Entry
    {
        public Entry(JobFile file, byte[] name, long headerOffset, bool bigSize, bool bigOffset, ushort time, ushort date)
        {
            File = file;
            Name = name;
            HeaderOffset = headerOffset;
            BigSize = bigSize;
            BigOffset = bigOffset;
            Time = time;
            Date = date;
        }

        public JobFile File { get; }

        public byte[] Name { get; }

        public long HeaderOffset { get; }

        /// <summary>Taille ≥ 4 Gio : tailles 64 bits (descripteur, extra Zip64).</summary>
        public bool BigSize { get; }

        /// <summary>En-tête local au-delà de 4 Gio : offset dans l'extra Zip64.</summary>
        public bool BigOffset { get; }

        public ushort Time { get; }

        public ushort Date { get; }

        public bool NeedsZip64 => BigSize || BigOffset;

        public int HeaderLength => 30 + Name.Length;

        public long DataOffset => HeaderOffset + HeaderLength;

        public long DescriptorOffset => DataOffset + File.Size;

        public int DescriptorLength => BigSize ? 24 : 16;

        public int ExtraLength => (BigSize ? 16 : 0) + (BigOffset ? 8 : 0) + (NeedsZip64 ? 4 : 0);

        public int CentralLength => 46 + Name.Length + ExtraLength;
    }
}
