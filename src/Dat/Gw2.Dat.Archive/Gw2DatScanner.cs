using Gw2.Formats;
namespace Gw2.Dat.Archive;

public enum Gw2DatRecordKind
{
    Unknown,
    Atoc,
    PackFile,
    ContentStore,
    Model,
    Material,
    Composite,
    EmoteAnimations,
    AnimSequences,
    Map,
    Texture,
}

public readonly record struct Gw2DatScanOptions(
    uint StartRecord = 0,
    uint? RecordCount = null,
    int PrefixBytes = 64,
    bool ContinueOnError = true)
{
    public void Validate(uint archiveEntryCount)
    {
        if (StartRecord >= archiveEntryCount && archiveEntryCount != 0)
            throw new ArgumentOutOfRangeException(nameof(StartRecord));
        if (PrefixBytes is < 0 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(PrefixBytes));
    }
}

public sealed record Gw2DatRecordScan(
    uint RecordIndex,
    MftEntry Entry,
    Gw2DatRecordKind Kind,
    long DecodedSize,
    byte[] Prefix,
    Exception? Error,
    string PackType = "");

public sealed class Gw2DatScanner
{
    private readonly FileArchive _archive;

    public Gw2DatScanner(FileArchive archive)
    {
        _archive = archive ?? throw new ArgumentNullException(nameof(archive));
    }

    public IEnumerable<Gw2DatRecordScan> ScanRecords() =>
        ScanRecords(new Gw2DatScanOptions(PrefixBytes: 64, ContinueOnError: true));

    public IEnumerable<Gw2DatRecordScan> ScanRecords(Gw2DatScanOptions options)
    {
        options.Validate(_archive.MftEntryCount);

        var start = options.StartRecord;
        var end = options.RecordCount is null
            ? _archive.MftEntryCount
            : Math.Min(
                (long)_archive.MftEntryCount,
                (long)start + options.RecordCount.Value);

        for (var record = (long)start; record < end; record++)
        {
            var recordIndex = checked((uint)record);
            var entry = _archive.ReadMftEntry(recordIndex);
            using var capture = new PrefixCaptureStream(options.PrefixBytes);
            Gw2DatRecordScan result;

            try
            {
                _archive.DecodeEntry(recordIndex, capture);
                var packType = Gw2FourCc.PackType(capture.Prefix);
                result = new(
                    recordIndex,
                    entry,
                    Classify(recordIndex, capture.Prefix, packType),
                    capture.DecodedSize,
                    capture.Prefix,
                    null,
                    packType);
            }
            catch (Exception ex) when (options.ContinueOnError)
            {
                result = new(recordIndex, entry, Gw2DatRecordKind.Unknown, capture.DecodedSize, capture.Prefix, ex);
            }

            yield return result;
        }
    }

    public void DecodeRecord(uint recordIndex, Stream destination) =>
        _archive.DecodeEntry(recordIndex, destination);

    public byte[] ReadDecodedRecord(uint recordIndex)
    {
        using var output = new MemoryStream();
        DecodeRecord(recordIndex, output);
        return output.ToArray();
    }

    private static Gw2DatRecordKind Classify(uint recordIndex, ReadOnlySpan<byte> prefix, string packType)
    {
        if (recordIndex == 1)
            return Gw2DatRecordKind.Atoc;
        if (packType.Length == 0)
            return Gw2Texture.LooksLike(prefix) ? Gw2DatRecordKind.Texture : Gw2DatRecordKind.Unknown;

        return packType switch
        {
            Gw2FourCc.Model => Gw2DatRecordKind.Model,
            Gw2FourCc.Material => Gw2DatRecordKind.Material,
            Gw2FourCc.ContentStore => Gw2DatRecordKind.ContentStore,
            Gw2FourCc.Composite => Gw2DatRecordKind.Composite,
            Gw2FourCc.EmoteAnimations => Gw2DatRecordKind.EmoteAnimations,
            Gw2FourCc.AnimSequences => Gw2DatRecordKind.AnimSequences,
            Gw2FourCc.Map or Gw2FourCc.MapShadow => Gw2DatRecordKind.Map,
            _ => Gw2DatRecordKind.PackFile,
        };
    }

    private sealed class PrefixCaptureStream : Stream
    {
        private readonly byte[] _prefix;
        private int _prefixLength;

        public PrefixCaptureStream(int prefixBytes) => _prefix = new byte[prefixBytes];

        public long DecodedSize { get; private set; }
        public byte[] Prefix => _prefix[.._prefixLength];

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => DecodedSize;
        public override long Position
        {
            get => DecodedSize;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            DecodedSize = checked(DecodedSize + buffer.Length);
            var count = Math.Min(buffer.Length, _prefix.Length - _prefixLength);
            if (count == 0)
                return;

            buffer[..count].CopyTo(_prefix.AsSpan(_prefixLength));
            _prefixLength += count;
        }
    }
}
