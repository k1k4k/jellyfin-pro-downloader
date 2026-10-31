using System.Buffers.Binary;
using System.Runtime.Intrinsics.Arm;

namespace Jellyfin.Plugin.ProDownloader.Services;

/// <summary>
/// CRC-32 (IEEE 802.3, celui du format ZIP). Instruction matérielle sur ARM64 (NAS, Raspberry Pi),
/// sinon table « slicing-by-8 » (plusieurs Go/s sur un PC, largement au-dessus d'une liaison réseau).
/// System.IO.Hashing n'est pas livré avec Jellyfin : on évite d'embarquer une dépendance.
/// </summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    /// <summary>Continue un CRC (0 pour commencer) avec de nouvelles données.</summary>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        var state = ~crc;
        if (System.Runtime.Intrinsics.Arm.Crc32.Arm64.IsSupported)
        {
            while (data.Length >= 8)
            {
                state = System.Runtime.Intrinsics.Arm.Crc32.Arm64.ComputeCrc32(state, BinaryPrimitives.ReadUInt64LittleEndian(data));
                data = data[8..];
            }

            foreach (var b in data)
            {
                state = System.Runtime.Intrinsics.Arm.Crc32.ComputeCrc32(state, b);
            }

            return ~state;
        }

        var t = Table;
        while (data.Length >= 8)
        {
            var one = BinaryPrimitives.ReadUInt32LittleEndian(data) ^ state;
            var two = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            state = t[(7 * 256) + (one & 0xFF)]
                ^ t[(6 * 256) + ((one >> 8) & 0xFF)]
                ^ t[(5 * 256) + ((one >> 16) & 0xFF)]
                ^ t[(4 * 256) + (one >> 24)]
                ^ t[(3 * 256) + (two & 0xFF)]
                ^ t[(2 * 256) + ((two >> 8) & 0xFF)]
                ^ t[256 + ((two >> 16) & 0xFF)]
                ^ t[two >> 24];
            data = data[8..];
        }

        foreach (var b in data)
        {
            state = (state >> 8) ^ t[(state ^ b) & 0xFF];
        }

        return ~state;
    }

    private static uint[] BuildTable()
    {
        var t = new uint[8 * 256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            t[i] = c;
        }

        for (var i = 0; i < 256; i++)
        {
            for (var k = 1; k < 8; k++)
            {
                var prev = t[((k - 1) * 256) + i];
                t[(k * 256) + i] = (prev >> 8) ^ t[prev & 0xFF];
            }
        }

        return t;
    }
}
