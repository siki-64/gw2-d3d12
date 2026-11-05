using System.Buffers.Binary;

namespace Gw2.Dat.Content;

public readonly record struct Gw2CrcManifestRecord(uint BaseId, uint FileId, uint Size, uint Crc);

public sealed class Gw2CrcManifest
{
    public const string PackType = "CRCM";
    private const int RecordSize = 16;
    private const int RecordPointerOffset = 4;

    private Gw2CrcManifest(IReadOnlyList<Gw2CrcManifestRecord> records) => Records = records;

    public IReadOnlyList<Gw2CrcManifestRecord> Records { get; }

    public static Gw2CrcManifest Parse(ReadOnlyMemory<byte> data)
    {
        var pack = Gw2PackFile.Parse(data);
        if (pack.Type != PackType) throw new InvalidDataException($"Expected {PackType} pack, found {pack.Type}");
        var chunk = pack.Chunks.FirstOrDefault(candidate => candidate.Type == PackType)
            ?? throw new InvalidDataException($"Missing {PackType} chunk");
        var payload = chunk.Payload.Span;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        var recordsOffset = RecordPointerOffset + (long)BinaryPrimitives.ReadUInt64LittleEndian(payload[RecordPointerOffset..]);
        if (recordsOffset + (long)count * RecordSize > payload.Length)
            throw new InvalidDataException("CRC manifest records run past the chunk");
        var records = new Gw2CrcManifestRecord[count];
        var rows = payload[(int)recordsOffset..];
        for (var index = 0; index < records.Length; index++)
        {
            var row = rows[(index * RecordSize)..];
            records[index] = new(
                BinaryPrimitives.ReadUInt32LittleEndian(row),
                BinaryPrimitives.ReadUInt32LittleEndian(row[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(row[8..]),
                BinaryPrimitives.ReadUInt32LittleEndian(row[12..]));
        }
        return new(records);
    }
}
