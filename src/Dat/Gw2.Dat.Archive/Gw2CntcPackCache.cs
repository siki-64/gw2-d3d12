using Gw2.Formats;
using System.Buffers.Binary;

namespace Gw2.Dat.Archive;

public readonly record struct Gw2CntcPackStamp(uint FileId, uint Size, uint Counter, uint Crc);

public static class Gw2CntcPackCache
{
    public const uint Magic = 0x434B5043;
    public const uint FormatVersion = 1;
    private const int HeaderBytes = 12;
    private const int TableRowBytes = 20;

    public static string PathFor(string datPath) => Path.ChangeExtension(datPath, ".packs.bin");

    public static Gw2CntcPackStamp StampOf(uint fileId, MftEntry entry) => new(fileId, entry.Size, entry.Counter, entry.Crc);

    public static void Write(string path, IReadOnlyList<Gw2CntcPackStamp> stamps, IReadOnlyList<byte[]> decodedPacks)
    {
        ArgumentNullException.ThrowIfNull(stamps);
        ArgumentNullException.ThrowIfNull(decodedPacks);
        if (stamps.Count != decodedPacks.Count) throw new ArgumentException("one stamp is required per pack");
        using var writer = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None));
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(stamps.Count);
        for (var i = 0; i < stamps.Count; i++)
        {
            writer.Write(stamps[i].FileId);
            writer.Write(stamps[i].Size);
            writer.Write(stamps[i].Counter);
            writer.Write(stamps[i].Crc);
            writer.Write(decodedPacks[i].Length);
        }
        foreach (var pack in decodedPacks) writer.Write(pack);
    }

    public static bool TryRead(string path, IReadOnlyList<Gw2CntcPackStamp> expected, out IReadOnlyList<byte[]> decodedPacks)
    {
        ArgumentNullException.ThrowIfNull(expected);
        decodedPacks = [];
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            var header = new byte[HeaderBytes];
            stream.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic
                || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != FormatVersion
                || BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8)) != expected.Count)
                return false;

            var table = new byte[expected.Count * TableRowBytes];
            stream.ReadExactly(table);
            var lengths = new int[expected.Count];
            for (var i = 0; i < expected.Count; i++)
            {
                var row = table.AsSpan(i * TableRowBytes);
                var stamp = new Gw2CntcPackStamp(BinaryPrimitives.ReadUInt32LittleEndian(row), BinaryPrimitives.ReadUInt32LittleEndian(row[4..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(row[8..]), BinaryPrimitives.ReadUInt32LittleEndian(row[12..]));
                if (stamp != expected[i]) return false;
                lengths[i] = BinaryPrimitives.ReadInt32LittleEndian(row[16..]);
            }

            var packs = new byte[expected.Count][];
            for (var i = 0; i < packs.Length; i++)
            {
                packs[i] = new byte[lengths[i]];
                stream.ReadExactly(packs[i]);
            }
            decodedPacks = packs;
            return true;
        }
        catch (EndOfStreamException) { return false; }
        catch (IOException) { return false; }
    }
}
