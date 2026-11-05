using System.Runtime.InteropServices;

namespace Gw2.Formats;

internal sealed unsafe class Packfile : IDisposable
{
    private readonly byte* _bytes;
    private readonly Dictionary<uint, PackfileChunk> _chunks = [];
    private readonly List<uint> _chunkOrder = [];
    private readonly Dictionary<uint, PackfileChunk> _currentChunks = [];
    private readonly List<nint> _conversionAllocations = [];

    private Packfile(byte* bytes, PackfileHeader* header)
    {
        _bytes = bytes;
        Flags = header->Flags;
        FileType = header->FileType;
    }

    internal ushort Flags { get; }
    internal uint FileType { get; }
    internal IReadOnlyList<uint> ChunkTypes => _chunkOrder;

    internal static Packfile? Open(FileArchive archive, uint fileId)
    {
        using var decoded = new MemoryStream();
        archive.DecodeFileId(fileId, decoded);
        return Open(decoded.GetBuffer().AsSpan(0, (int)decoded.Length));
    }

    internal static Packfile? Open(ReadOnlySpan<byte> decoded)
    {
        if (decoded.Length < sizeof(PackfileHeader)) return null;
        var bytes = (byte*)NativeMemory.Alloc((nuint)decoded.Length);
        decoded.CopyTo(new Span<byte>(bytes, decoded.Length));
        var header = (PackfileHeader*)bytes;
        if (header->Signature != PackfileHeader.PackfileSignature || header->Endian != 0
            || header->HeaderSize > PackfileHeader.MaximumHeaderSize || header->FileType == 0)
        {
            NativeMemory.Free(bytes);
            return null;
        }
        var packfile = new Packfile(bytes, header);
        packfile.WalkChunks(bytes + header->HeaderSize, bytes + decoded.Length);
        return packfile;
    }

    internal PackfileChunk? FileChunk(uint chunkType) => _chunks.TryGetValue(chunkType, out var chunk) ? chunk : null;

    internal PackfileChunk? Chunk(uint chunkType)
    {
        if (_currentChunks.TryGetValue(chunkType, out var current)) return current;
        if (!_chunks.TryGetValue(chunkType, out var fileChunk)) return null;
        current = UpgradeToCurrentVersion(fileChunk);
        _currentChunks[chunkType] = current;
        return current;
    }

    internal PackType? ChunkLayout(uint chunkType) =>
        Chunk(chunkType) is { } chunk ? PackfileSchema.Default.Type(chunkType, chunk.Version, FileType) : null;

    internal PackObject? ChunkObject(uint chunkType) =>
        Chunk(chunkType) is { } chunk && ChunkLayout(chunkType) is { } layout ? PackObjectReader.Read(chunk.Data, layout) : null;

    internal T* ChunkData<T>(uint chunkType) where T : unmanaged =>
        Chunk(chunkType) is { } chunk ? (T*)chunk.Data : null;

    public void Dispose()
    {
        foreach (nint allocation in _conversionAllocations) NativeMemory.Free((void*)allocation);
        NativeMemory.Free(_bytes);
    }

    private PackfileChunk UpgradeToCurrentVersion(PackfileChunk fileChunk)
    {
        if (PackfileSchema.Default.Registration(fileChunk.ChunkType, fileChunk.Version, FileType) is not { } registration) return fileChunk;
        ushort latestVersion = registration.Versions.Keys.Max();
        if (latestVersion == fileChunk.Version) return fileChunk;
        var allocator = new PackfileUpgradeAllocator(_conversionAllocations);
        bool singleStep = fileChunk.Version == latestVersion - 1;
        ushort currentVersion = fileChunk.Version;
        byte* data = fileChunk.Data;
        for (int version = fileChunk.Version + 1; version <= latestVersion; version++)
        {
            if (!registration.Versions.TryGetValue((ushort)version, out var stepType)) continue;
            byte* previous = data;
            if (!registration.SkippedVersions.Contains((ushort)version) || singleStep)
                data = PackfileUpgrade.Convert(data, registration.Versions[currentVersion], stepType, _conversionAllocations);
            currentVersion = (ushort)version;
            PackfileUpgradeRoutines.For(stepType)?.Invoke(previous, data, allocator);
        }
        return new PackfileChunk(fileChunk.ChunkType, latestVersion, data, (uint)registration.Versions[latestVersion].Size);
    }

    private void WalkChunks(byte* chunkAddress, byte* end)
    {
        while (chunkAddress + PackfileChunkHeader.DistanceOrigin <= end)
        {
            var header = (PackfileChunkHeader*)chunkAddress;
            byte* next = chunkAddress + PackfileChunkHeader.DistanceOrigin + header->NextChunkDistance;
            if (next > end) break;
            uint dataSize = header->NextChunkDistance + PackfileChunkHeader.DistanceOrigin - header->HeaderSize;
            uint relocationTableOffset = header->HeaderSize <= PackfileChunkHeader.SizeWithoutRelocationTable ? 0 : header->RelocationTableOffset;
            byte* data = chunkAddress + header->HeaderSize;
            if (dataSize != 0)
            {
                if ((Flags & PackfileHeader.FlagPointers64) == 0)
                    data = PackfilePointerWidening.Widen(data, ref dataSize, ref relocationTableOffset, _conversionAllocations);
                if (relocationTableOffset != 0 && !ConvertOffsets(dataSize, data, (uint*)(data + relocationTableOffset)))
                    throw new InvalidDataException("Packfile offset-conversion failed.");
                if (!_chunks.ContainsKey(header->ChunkType)) _chunkOrder.Add(header->ChunkType);
                _chunks[header->ChunkType] = new PackfileChunk(header->ChunkType, header->Version, data, dataSize);
            }
            chunkAddress = next;
        }
    }

    private static bool ConvertOffsets(uint dataSize, byte* data, uint* relocationTable)
    {
        uint slotCount = *relocationTable;
        uint* slotOffsets = relocationTable + 1;
        bool failed = false;
        for (uint index = 0; index < slotCount; index++)
        {
            uint slotOffset = slotOffsets[index];
            var slot = (long*)(data + slotOffset);
            long distance = *slot;
            if ((uint)((int)distance + (int)slotOffset) < dataSize)
            {
                if (!failed) *slot = distance == 0 ? 0 : (long)(data + slotOffset + distance);
            }
            else failed = true;
        }
        return !failed;
    }
}
