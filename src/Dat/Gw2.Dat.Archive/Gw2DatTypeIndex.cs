using Gw2.Formats;
using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Archive;

public readonly record struct Gw2DatRecordStamp(uint Size, uint Counter, uint Crc)
{
    public static Gw2DatRecordStamp Of(MftEntry entry) => new(entry.Size, entry.Counter, entry.Crc);
}

public readonly record struct Gw2DatRecordType(Gw2DatRecordStamp Stamp, uint Tag);

public sealed class Gw2DatTypeIndex
{
    public const uint Magic = 0x58445954;
    public const uint FormatVersion = 1;
    private const int HeaderBytes = 12;
    private const int RowBytes = 20;
    private const int PrefixBytes = 12;
    private const int PackTagOffset = 8;
    private const int TagBytes = 4;

    private readonly Dictionary<uint, Gw2DatRecordType> _records;
    private readonly IReadOnlyDictionary<uint, uint> _fileIndex;

    private Gw2DatTypeIndex(Dictionary<uint, Gw2DatRecordType> records, IReadOnlyDictionary<uint, uint> fileIndex, int refreshedRecords)
    {
        _records = records;
        _fileIndex = fileIndex;
        RefreshedRecords = refreshedRecords;
    }

    public int RefreshedRecords { get; }

    public int RecordCount => _records.Count;

    public static string PathFor(string datPath) => Path.ChangeExtension(datPath, ".types.bin");

    public static Gw2DatTypeIndex Load(FileArchive archive, string datPath)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var path = PathFor(datPath);
        var cached = TryRead(path);
        var records = new Dictionary<uint, Gw2DatRecordType>();
        var refreshed = 0;
        foreach (var recordIndex in archive.FileIndex.Values.Distinct().Order())
        {
            var stamp = Gw2DatRecordStamp.Of(archive.ReadMftEntry(recordIndex));
            if (cached.TryGetValue(recordIndex, out var known) && known.Stamp == stamp)
            {
                records[recordIndex] = known;
                continue;
            }
            records[recordIndex] = new Gw2DatRecordType(stamp, ReadTag(archive, recordIndex));
            refreshed++;
        }
        if (refreshed > 0 || records.Count != cached.Count) TryWrite(path, records);
        return new Gw2DatTypeIndex(records, archive.FileIndex, refreshed);
    }

    public static uint TagOf(string fourCc)
    {
        if (fourCc.Length != TagBytes) throw new ArgumentException("a type tag is four ASCII characters", nameof(fourCc));
        return BinaryPrimitives.ReadUInt32LittleEndian(Encoding.ASCII.GetBytes(fourCc));
    }

    public static uint TagOfPrefix(ReadOnlySpan<byte> prefix)
    {
        var isPack = prefix.Length >= PackTagOffset + TagBytes && prefix[..2].SequenceEqual("PF"u8);
        var tag = isPack ? prefix.Slice(PackTagOffset, TagBytes) : prefix[..Math.Min(TagBytes, prefix.Length)];
        Span<byte> padded = stackalloc byte[TagBytes];
        tag.CopyTo(padded);
        return BinaryPrimitives.ReadUInt32LittleEndian(padded);
    }

    public uint? TagOfRecord(uint recordIndex) => _records.TryGetValue(recordIndex, out var record) ? record.Tag : null;

    public uint? TagOfFile(uint fileId) => _fileIndex.TryGetValue(fileId, out var recordIndex) ? TagOfRecord(recordIndex) : null;

    public IEnumerable<uint> FileIdsOfType(string fourCc)
    {
        var tag = TagOf(fourCc);
        return _fileIndex.Where(file => TagOfRecord(file.Value) == tag).Select(file => file.Key).Order();
    }

    private static uint ReadTag(FileArchive archive, uint recordIndex)
    {
        try { return TagOfPrefix(archive.ReadDecodedPrefix(recordIndex, PrefixBytes)); }
        catch (InvalidDataException) { return 0; }
        catch (NotSupportedException) { return 0; }
    }

    private static void TryWrite(string path, Dictionary<uint, Gw2DatRecordType> records)
    {
        try { Write(path, records); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Write(string path, Dictionary<uint, Gw2DatRecordType> records)
    {
        using var writer = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16));
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(records.Count);
        foreach (var (recordIndex, record) in records)
        {
            writer.Write(recordIndex);
            writer.Write(record.Stamp.Size);
            writer.Write(record.Stamp.Counter);
            writer.Write(record.Stamp.Crc);
            writer.Write(record.Tag);
        }
    }

    private static Dictionary<uint, Gw2DatRecordType> TryRead(string path)
    {
        var records = new Dictionary<uint, Gw2DatRecordType>();
        if (!File.Exists(path)) return records;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < HeaderBytes
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != FormatVersion)
                return records;
            var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
            if (count < 0 || bytes.Length != HeaderBytes + (long)count * RowBytes) return records;
            for (var i = 0; i < count; i++)
            {
                var row = bytes.AsSpan(HeaderBytes + i * RowBytes, RowBytes);
                var stamp = new Gw2DatRecordStamp(BinaryPrimitives.ReadUInt32LittleEndian(row[4..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(row[8..]), BinaryPrimitives.ReadUInt32LittleEndian(row[12..]));
                records[BinaryPrimitives.ReadUInt32LittleEndian(row)] = new Gw2DatRecordType(stamp, BinaryPrimitives.ReadUInt32LittleEndian(row[16..]));
            }
            return records;
        }
        catch (IOException) { return []; }
    }
}
