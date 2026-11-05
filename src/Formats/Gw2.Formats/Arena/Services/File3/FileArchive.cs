using System.Buffers.Binary;

namespace Gw2.Formats;

public sealed class FileArchive : IDisposable
{
    private const int DatHeaderSize = 40;
    private const int MftHeaderSize = 24;
    private const int MftEntrySize = 24;
    private const uint CrcBlockSize = 65536;
    private static ReadOnlySpan<byte> DatMagic => "AN\x1A"u8;
    private static ReadOnlySpan<byte> MftMagic => "Mft\x1A"u8;

    private readonly Stream _stream;
    private readonly bool _verifyChecksums;
    private readonly Lazy<System.Collections.ObjectModel.ReadOnlyDictionary<uint, uint>> _fileIndex;

    private FileArchive(Stream stream, bool verifyChecksums)
    {
        _stream = stream;
        _verifyChecksums = verifyChecksums;
        _fileIndex = new(() => new(ReadAtoc()));

        Span<byte> header = stackalloc byte[DatHeaderSize];
        ReadExactly(header);
        var version = header[0];
        if (!header[1..4].SequenceEqual(DatMagic))
            throw new InvalidDataException("not a GW2 .dat file (bad magic)");
        if (verifyChecksums && !ArchiveCrc.Matches(header[..16], BinaryPrimitives.ReadUInt32LittleEndian(header[16..])))
            throw new InvalidDataException("GW2 archive header CRC mismatch");

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var mftOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[24..]);
        var mftSize = BinaryPrimitives.ReadUInt32LittleEndian(header[32..]);
        if (headerSize < DatHeaderSize)
            throw new InvalidDataException($"unexpected .dat header size {headerSize}");

        ValidateRange(mftOffset, mftSize);
        Span<byte> mftHeader = stackalloc byte[MftHeaderSize];
        ReadAt(mftOffset, mftHeader);
        if (!mftHeader[..4].SequenceEqual(MftMagic))
            throw new InvalidDataException("bad MFT magic");

        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(mftHeader[12..]);
        if (entryCount == 0 || mftSize < MftHeaderSize)
            throw new InvalidDataException("invalid MFT header");
        if ((ulong)entryCount * MftEntrySize > mftSize)
            throw new InvalidDataException("MFT entries exceed the declared table size");

        Header = new ArchiveHeader(version, chunkSize, mftOffset, mftSize, entryCount - 1);
        if (verifyChecksums)
            VerifyMft();
    }

    public ArchiveHeader Header { get; }

    public byte Version => Header.Version;
    public uint ChunkSize => Header.ChunkSize;
    public ulong MftOffset => Header.MftOffset;
    public uint MftSize => Header.MftSize;
    public uint MftEntryCount => Header.MftEntryCount;

    public IReadOnlyDictionary<uint, uint> FileIndex => _fileIndex.Value;

    public void DecodeFileId(uint fileId, Stream destination, ReadOnlySpan<byte> oldSource = default)
    {
        if (!FileIndex.TryGetValue(fileId, out var record))
            throw new KeyNotFoundException($"GW2 file ID {fileId} is absent from the archive");
        DecodeEntry(record, destination, oldSource);
    }

    public static FileArchive Open(string path, bool verifyChecksums = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Stream stream = SplitStore.IsSplitArchive(path)
            ? SplitStore.Open(path)
            : new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                options: FileOptions.RandomAccess);
        return Open(stream, verifyChecksums);
    }

    public static FileArchive Open(Stream stream, bool verifyChecksums = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("GW2 archive stream must be readable and seekable", nameof(stream));

        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            return new FileArchive(stream, verifyChecksums);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public MftEntry ReadMftEntry(uint recordIndex)
    {
        if (recordIndex >= MftEntryCount)
            throw new ArgumentOutOfRangeException(nameof(recordIndex));

        Span<byte> entry = stackalloc byte[MftEntrySize];
        ReadAt(MftOffset + MftHeaderSize + (ulong)recordIndex * MftEntrySize, entry);
        return new MftEntry(
            BinaryPrimitives.ReadUInt64LittleEndian(entry),
            BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(entry[14..]),
            BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]));
    }

    public Dictionary<uint, uint> ReadAtoc()
    {
        var entry = ReadMftEntry(1);
        if (entry.Offset == 0 || entry.Size == 0 || entry.Size % 8 != 0)
            throw new InvalidDataException("ATOC record is missing or has an invalid size");
        if (entry.Size > int.MaxValue)
            throw new InvalidDataException("ATOC record is too large for the current reader");

        var data = new byte[(int)entry.Size];
        ReadAt(entry.Offset, data);
        if (_verifyChecksums && !ArchiveCrc.Matches(data, entry.Crc))
            throw new InvalidDataException("GW2 ATOC CRC mismatch");
        var result = new Dictionary<uint, uint>(data.Length / 8);
        for (var offset = 0; offset < data.Length; offset += 8)
        {
            var fileId = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            var mftIndex = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset + 4));
            if (fileId != 0 && mftIndex > 0 && mftIndex <= MftEntryCount)
                result[fileId] = mftIndex - 1;
        }

        return result;
    }

    public byte[] ReadRawEntry(uint recordIndex)
    {
        var entry = ReadMftEntry(recordIndex);
        if (entry.Size > int.MaxValue)
            throw new InvalidDataException($"MFT record {recordIndex} is too large for the current reader");

        var data = new byte[(int)entry.Size];
        ReadAt(entry.Offset, data);
        return data;
    }

    public void CopyRawEntry(uint recordIndex, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var entry = ReadMftEntry(recordIndex);
        CopyAt(entry.Offset, entry.Size, destination);
    }

    public ulong ReadDecodedSize(uint recordIndex)
    {
        var entry = ReadMftEntry(recordIndex);
        if (recordIndex < 15) return entry.Size;
        if (entry.Compression == 0)
            return entry.Size - 4ul * ((entry.Size + CrcBlockSize - 1) / CrcBlockSize);

        Span<byte> header = stackalloc byte[8];
        ReadAt(entry.Offset, header);
        return BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
    }

    public void DecodeEntry(uint recordIndex, Stream destination)
        => DecodeEntry(recordIndex, destination, []);

    public void DecodeEntry(uint recordIndex, Stream destination, ReadOnlySpan<byte> oldSource)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var entry = ReadMftEntry(recordIndex);

        if (recordIndex < 15)
        {
            CopyAt(entry.Offset, entry.Size, destination);
            return;
        }

        if (entry.Compression == 0)
        {
            CopyUncompressed(recordIndex, entry, destination);
            return;
        }

        var raw = ReadRawEntry(recordIndex);
        if (_verifyChecksums)
        {
            if (!ArchiveCrc.Matches(raw, entry.Crc))
                throw new InvalidDataException($"GW2 record {recordIndex} CRC mismatch");
            ArchiveCrc.VerifyBlocks(raw);
        }
        ArchiveEntry.DecodeEntry(raw, entry.Compression, destination, oldSource);
    }

    public void VerifyEntry(uint recordIndex)
    {
        var entry = ReadMftEntry(recordIndex);
        if (recordIndex == 0) return;
        if (recordIndex == 2) { VerifyMft(); return; }
        var (ieee, castagnoli) = ComputeRangeCrc(entry.Offset, entry.Size);
        if (entry.Crc != ieee && entry.Crc != castagnoli)
            throw new InvalidDataException($"GW2 record {recordIndex} CRC mismatch");
        if (recordIndex >= 15)
        {
            var buffer = new byte[65536];
            for (ulong offset = 0; offset < entry.Size;)
            {
                var count = (int)Math.Min((ulong)buffer.Length, entry.Size - offset);
                ReadAt(entry.Offset + offset, buffer.AsSpan(0, count));
                ArchiveCrc.VerifyBlocks(buffer.AsSpan(0, count));
                offset += (uint)count;
            }
        }
    }

    private void CopyUncompressed(uint recordIndex, MftEntry entry, Stream destination)
    {
        if (_verifyChecksums) VerifyEntry(recordIndex);
        var buffer = new byte[65536];
        for (ulong offset = 0; offset < entry.Size;)
        {
            var count = (int)Math.Min((ulong)buffer.Length, entry.Size - offset);
            if (count < 4) throw new InvalidDataException("Incomplete archive CRC word");
            ReadAt(entry.Offset + offset, buffer.AsSpan(0, count));
            destination.Write(buffer.AsSpan(0, count - 4));
            offset += (uint)count;
        }
    }

    private void VerifyMft()
    {
        if (MftEntryCount < 3) throw new InvalidDataException("Missing MFT self entry");
        var expected = ReadMftEntry(2).Crc;
        var first = ComputeRangeCrc(MftOffset, 72);
        var crc = ComputeRangeCrc(MftOffset + 96, MftSize - 96, first.Ieee, first.Castagnoli);
        if (expected != crc.Ieee && expected != crc.Castagnoli)
            throw new InvalidDataException("GW2 MFT CRC mismatch");
    }

    private (uint Ieee, uint Castagnoli) ComputeRangeCrc(ulong offset, uint length, uint ieee = 0, uint castagnoli = 0)
    {
        ValidateRange(offset, length);
        var buffer = new byte[65536];
        while (length > 0)
        {
            var count = (int)Math.Min((uint)buffer.Length, length);
            ReadAt(offset, buffer.AsSpan(0, count));
            ieee = ArchiveCrc.Compute(buffer.AsSpan(0, count), ieee);
            castagnoli = ArchiveCrc.ComputeCrc32C(buffer.AsSpan(0, count), castagnoli);
            offset += (uint)count;
            length -= (uint)count;
        }
        return (ieee, castagnoli);
    }

    public void Dispose() => _stream.Dispose();

    public byte[] ReadDecodedPrefix(uint recordIndex, int length = 12)
    {
        if (length is < 1 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(length));
        if (TryReadDecodedPrefixFromFirstBlock(recordIndex, length, out var prefix)) return prefix;
        using var capture = new PrefixStream(length);
        try { DecodeEntry(recordIndex, capture); }
        catch (PrefixCompleteException) { }
        return capture.Result;
    }

    private bool TryReadDecodedPrefixFromFirstBlock(uint recordIndex, int length, out byte[] prefix)
    {
        prefix = [];
        if (recordIndex < 15 || _verifyChecksums || length > CrcBlockSize / 2) return false;
        var entry = ReadMftEntry(recordIndex);
        if (entry.Size < 4) return false;

        var firstBlock = new byte[Math.Min(entry.Size, CrcBlockSize)];
        ReadAt(entry.Offset, firstBlock);
        var payload = firstBlock.Length < CrcBlockSize
            ? ArchiveEntry.StripCrc32(firstBlock).AsSpan()
            : firstBlock.AsSpan(0, (int)CrcBlockSize - 4);
        if (entry.Compression == 0)
        {
            prefix = payload[..Math.Min(length, payload.Length)].ToArray();
            return true;
        }
        if (entry.Compression != 8 || payload.Length < 8) return false;

        var decodedSize = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
        var wanted = (uint)Math.Min(length, decodedSize);
        var compressedSize = entry.Size - 4ul * ((entry.Size + CrcBlockSize - 1) / CrcBlockSize) - 8;
        if (compressedSize == decodedSize)
        {
            prefix = payload.Slice(8, (int)Math.Min(wanted, (uint)payload.Length - 8)).ToArray();
            return prefix.Length == wanted;
        }
        var decoded = new byte[wanted];
        try
        {
            if (!ArchiveEntry.TryDecodeCompressedPrefix(payload[8..], decoded)) return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or IndexOutOfRangeException)
        {
            return false;
        }
        prefix = decoded;
        return true;
    }

    private sealed class PrefixCompleteException : Exception;
    private sealed class PrefixStream(int capacity) : Stream
    {
        private readonly byte[] _bytes = new byte[capacity];
        private int _count;
        public byte[] Result => _bytes[.._count];
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _count;
        public override long Position { get => _count; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int take = Math.Min(buffer.Length, _bytes.Length - _count);
            buffer[..take].CopyTo(_bytes.AsSpan(_count));
            _count += take;
            if (_count == _bytes.Length) throw new PrefixCompleteException();
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private void ReadAt(ulong offset, Span<byte> destination)
    {
        ValidateRange(offset, (ulong)destination.Length);
        var position = checked((long)offset);
        while (!destination.IsEmpty)
        {
            int read;
            lock (_stream)
            {
                _stream.Seek(position, SeekOrigin.Begin);
                read = _stream.Read(destination);
            }
            if (read == 0)
                throw new EndOfStreamException("unexpected end of GW2 .dat file");

            position += read;
            destination = destination[read..];
        }
    }

    private void CopyAt(ulong offset, uint size, Stream destination)
    {
        ValidateRange(offset, size);
        var position = checked((long)offset);
        var remaining = (long)size;
        var buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            ReadAt((ulong)position, buffer.AsSpan(0, count));
            destination.Write(buffer, 0, count);
            position += count;
            remaining -= count;
        }
    }

    private void ValidateRange(ulong offset, ulong size)
    {
        var length = (ulong)_stream.Length;
        if (offset > length || size > length - offset)
            throw new InvalidDataException("GW2 archive range extends beyond the file");
    }

    private void ReadExactly(Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            var read = _stream.Read(destination);
            if (read == 0)
                throw new EndOfStreamException("unexpected end of GW2 .dat file");
            destination = destination[read..];
        }
    }
}
