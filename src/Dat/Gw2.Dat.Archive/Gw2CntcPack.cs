using Gw2.Formats;
using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Archive;

public enum Gw2CntcArrayKind
{
    TypeInfos,
    Namespaces,
    FileReferences,
    IndexEntries,
    LocalFixups,
    ExternalFixups,
    FileIndexFixups,
    StringIndexFixups,
    TrackedReferences,
    Strings,
    Content,
}

public readonly record struct Gw2CntcArrayDescriptor(
    Gw2CntcArrayKind Kind,
    uint Count,
    uint Offset);

public readonly record struct Gw2CntcIndexEntry(
    uint Index,
    uint Type,
    uint Offset,
    uint NamespaceIndex,
    uint RootIndex);

public readonly record struct Gw2CntcObject(
    uint Index,
    uint Type,
    uint Offset,
    uint EndOffset,
    uint NamespaceIndex,
    uint RootIndex)
{
    public uint Length => EndOffset - Offset;
}

public readonly record struct Gw2CntcContentKey(ulong First, ulong Second)
{
    public static Gw2CntcContentKey Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16)
            throw new ArgumentException("a content key requires 16 bytes", nameof(bytes));

        return new(
            BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]));
    }

    public byte[] ToArray()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, First);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), Second);
        return bytes;
    }

    public override string ToString() => Gw2Binary.ToHexString(ToArray());
}

public readonly record struct Gw2CntcLocalFixup(
    uint RelocationOffset,
    uint TargetOffset);

public readonly record struct Gw2CntcExternalFixup(
    uint RelocationOffset,
    uint TargetFileIndex,
    uint TargetOffset);

public readonly record struct Gw2CntcPointer(
    uint PackIndex,
    uint TargetOffset,
    bool IsExternal);

public readonly record struct Gw2CntcTrackedReference(
    uint SourceOffset,
    uint TargetFileIndex,
    uint TargetOffset);

public sealed class Gw2CntcPack
{
    private static readonly Gw2CntcArrayKind[] ArrayKinds =
    [
        Gw2CntcArrayKind.TypeInfos,
        Gw2CntcArrayKind.Namespaces,
        Gw2CntcArrayKind.FileReferences,
        Gw2CntcArrayKind.IndexEntries,
        Gw2CntcArrayKind.LocalFixups,
        Gw2CntcArrayKind.ExternalFixups,
        Gw2CntcArrayKind.FileIndexFixups,
        Gw2CntcArrayKind.StringIndexFixups,
        Gw2CntcArrayKind.TrackedReferences,
        Gw2CntcArrayKind.Strings,
        Gw2CntcArrayKind.Content,
    ];

    private readonly ReadOnlyMemory<byte> _data;
    private readonly Gw2CntcIndexEntry[] _indexEntries;
    private readonly Gw2CntcObject[] _objects;
    private readonly Dictionary<uint, Gw2CntcObject> _objectsByOffset;
    private readonly uint[] _localFixupOffsets;
    private readonly Gw2CntcExternalFixup[] _externalFixups;
    private readonly uint[] _fileIndexFixupOffsets;
    private readonly uint[] _stringIndexFixupOffsets;
    private readonly Gw2CntcTrackedReference[] _trackedReferences;
    private readonly Gw2CntcArrayDescriptor[] _arrays;

    private Gw2CntcPack(
        ReadOnlyMemory<byte> data,
        uint mainOffset,
        Gw2CntcArrayDescriptor[] arrays,
        Gw2CntcIndexEntry[] indexEntries,
        Gw2CntcObject[] objects,
        Dictionary<uint, Gw2CntcObject> objectsByOffset,
        uint[] localFixupOffsets,
        Gw2CntcExternalFixup[] externalFixups,
        uint[] fileIndexFixupOffsets,
        uint[] stringIndexFixupOffsets,
        Gw2CntcTrackedReference[] trackedReferences)
    {
        _data = data;
        MainOffset = mainOffset;
        _arrays = arrays;
        _indexEntries = indexEntries;
        _objects = objects;
        _objectsByOffset = objectsByOffset;
        _localFixupOffsets = localFixupOffsets;
        _externalFixups = externalFixups;
        _fileIndexFixupOffsets = fileIndexFixupOffsets;
        _stringIndexFixupOffsets = stringIndexFixupOffsets;
        _trackedReferences = trackedReferences;
    }

    public uint MainOffset { get; }
    public uint ContentOffset => GetArray(Gw2CntcArrayKind.Content).Offset;
    public uint ContentByteCount => GetArray(Gw2CntcArrayKind.Content).Count;
    public ReadOnlyMemory<byte> Data => _data;
    public IReadOnlyList<Gw2CntcArrayDescriptor> Arrays => _arrays;
    public IReadOnlyList<Gw2CntcIndexEntry> IndexEntries => _indexEntries;
    public IReadOnlyList<Gw2CntcObject> Objects => _objects;
    public ReadOnlySpan<uint> LocalFixupOffsets => _localFixupOffsets;
    public ReadOnlySpan<Gw2CntcExternalFixup> ExternalFixups => _externalFixups;
    public ReadOnlySpan<uint> FileIndexFixupOffsets => _fileIndexFixupOffsets;
    public ReadOnlySpan<uint> StringIndexFixupOffsets => _stringIndexFixupOffsets;

    public const uint EncryptedFlag = 1;

    private static byte[] Decrypt(ReadOnlySpan<byte> span, int flagsOffset, uint flags, Gw2CntcArrayDescriptor content, byte[] contentKey)
    {
        var decrypted = span.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(decrypted.AsSpan(flagsOffset), flags & ~EncryptedFlag);
        var contentRange = decrypted.AsSpan(checked((int)content.Offset), checked((int)content.Count));
        CptRc4.KeySchedule(CptRc4.DeriveKey(contentKey)).Process(contentRange);
        return decrypted;
    }

    public static bool LooksLike(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && data[..2].SequenceEqual("PF"u8) && data[8..12].SequenceEqual("cntc"u8);

    public static Gw2CntcPack Parse(byte[] data, byte[]? contentKey = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Parse((ReadOnlyMemory<byte>)data, contentKey);
    }

    public static Gw2CntcPack Parse(ReadOnlyMemory<byte> data, byte[]? contentKey = null)
    {
        if (!LooksLike(data.Span))
            throw new InvalidDataException("decoded payload is not a PF/cntc content store");

        var span = data.Span;
        uint mainOffset = ReadUInt16(span, 6);
        while ((ulong)mainOffset + 8 <= (ulong)span.Length && !span.Slice(checked((int)mainOffset), 4).SequenceEqual("Main"u8))
        {
            var chunkSize = ReadUInt32(span, checked((int)mainOffset + 4));
            var next = checked((ulong)mainOffset + 8u + chunkSize);
            if (next <= mainOffset || next > (ulong)span.Length)
                throw new InvalidDataException("PF chunk extends beyond the decoded payload");
            mainOffset = checked((uint)next);
        }

        if ((ulong)mainOffset + 16 > (ulong)span.Length || !span.Slice(checked((int)mainOffset), 4).SequenceEqual("Main"u8))
            throw new InvalidDataException("PF/cntc payload has no Main chunk");

        var baseOffset = checked((int)mainOffset + 16);
        var arrays = new Gw2CntcArrayDescriptor[ArrayKinds.Length];
        for (var i = 0; i < ArrayKinds.Length; i++)
        {
            var descriptorOffset = checked(baseOffset + 4 + i * 12);
            var count = ReadUInt32(span, descriptorOffset);
            var relative = ReadInt64(span, descriptorOffset + 4);
            var target = checked((long)descriptorOffset + 4 + relative);
            if (target < 0 || target > span.Length)
                throw new InvalidDataException($"cntc {ArrayKinds[i]} array points outside the decoded payload");
            arrays[i] = new(ArrayKinds[i], count, checked((uint)target));
        }

        var content = GetArray(arrays, Gw2CntcArrayKind.Content);
        ValidateArray(span, content, 1);
        var flags = ReadUInt32(span, baseOffset);
        if ((flags & EncryptedFlag) != 0)
            return contentKey is null
                ? throw new InvalidDataException("cntc pack is encrypted and no content key was supplied")
                : Parse(Decrypt(span, baseOffset, flags, content, contentKey));
        if (content.Count == 0)
            throw new InvalidDataException("cntc content array is empty");

        var indexDescriptor = GetArray(arrays, Gw2CntcArrayKind.IndexEntries);
        ValidateArray(span, indexDescriptor, 16);
        var indexEntries = new Gw2CntcIndexEntry[checked((int)indexDescriptor.Count)];
        for (var i = 0; i < indexEntries.Length; i++)
        {
            var offset = checked((int)indexDescriptor.Offset + i * 16);
            indexEntries[i] = new(
                (uint)i,
                ReadUInt32(span, offset),
                ReadUInt32(span, offset + 4),
                ReadUInt32(span, offset + 8),
                ReadUInt32(span, offset + 12));
        }

        var sortedEntries = indexEntries
            .OrderBy(static entry => entry.Offset)
            .ThenBy(static entry => entry.Index)
            .ToArray();
        var objects = new List<Gw2CntcObject>(sortedEntries.Length);
        var objectsByOffset = new Dictionary<uint, Gw2CntcObject>(sortedEntries.Length);
        for (var i = 0; i < sortedEntries.Length; i++)
        {
            var entry = sortedEntries[i];
            if (entry.Offset >= content.Count)
                throw new InvalidDataException($"cntc object offset 0x{entry.Offset:X} is outside content");
            if (objectsByOffset.ContainsKey(entry.Offset))
                continue;

            var nextIndex = i + 1;
            while (nextIndex < sortedEntries.Length && sortedEntries[nextIndex].Offset == entry.Offset)
                nextIndex++;
            var end = nextIndex < sortedEntries.Length
                ? sortedEntries[nextIndex].Offset
                : content.Count;
            if (end <= entry.Offset || end > content.Count)
                throw new InvalidDataException($"cntc object range at 0x{entry.Offset:X} is invalid");

            var obj = new Gw2CntcObject(
                entry.Index,
                entry.Type,
                entry.Offset,
                end,
                entry.NamespaceIndex,
                entry.RootIndex);
            objects.Add(obj);
            objectsByOffset.Add(obj.Offset, obj);
        }

        var local = ReadUInt32Array(span, GetArray(arrays, Gw2CntcArrayKind.LocalFixups), 4);
        Array.Sort(local);

        var externalDescriptor = GetArray(arrays, Gw2CntcArrayKind.ExternalFixups);
        ValidateArray(span, externalDescriptor, 8);
        var external = new Gw2CntcExternalFixup[checked((int)externalDescriptor.Count)];
        for (var i = 0; i < external.Length; i++)
        {
            var offset = checked((int)externalDescriptor.Offset + i * 8);
            var relocation = ReadUInt32(span, offset);
            external[i] = new(relocation, ReadUInt32(span, offset + 4), 0);
        }

        Array.Sort(external, static (left, right) => left.RelocationOffset.CompareTo(right.RelocationOffset));
        for (var i = 0; i < external.Length; i++)
            external[i] = external[i] with { TargetOffset = ReadContentUInt32(span, content, external[i].RelocationOffset) };

        var fileIndex = ReadUInt32Array(span, GetArray(arrays, Gw2CntcArrayKind.FileIndexFixups), 4);
        Array.Sort(fileIndex);
        var stringIndex = ReadUInt32Array(span, GetArray(arrays, Gw2CntcArrayKind.StringIndexFixups), 4);
        Array.Sort(stringIndex);

        var trackedDescriptor = GetArray(arrays, Gw2CntcArrayKind.TrackedReferences);
        ValidateArray(span, trackedDescriptor, 12);
        var tracked = new Gw2CntcTrackedReference[checked((int)trackedDescriptor.Count)];
        for (var i = 0; i < tracked.Length; i++)
        {
            var offset = checked((int)trackedDescriptor.Offset + i * 12);
            tracked[i] = new(
                ReadUInt32(span, offset),
                ReadUInt32(span, offset + 4),
                ReadUInt32(span, offset + 8));
        }
        Array.Sort(tracked, static (left, right) => left.SourceOffset.CompareTo(right.SourceOffset));

        var objectArray = objects.ToArray();
        var objectMap = objectsByOffset.ToDictionary(static pair => pair.Key, static pair => pair.Value);
        for (var i = 0; i < objectArray.Length; i++)
        {
            var current = objectArray[i];
            var end = i + 1 < objectArray.Length ? objectArray[i + 1].Offset : content.Count;
            objectArray[i] = current with { EndOffset = end };
            objectMap[current.Offset] = objectArray[i];
        }

        return new(
            data,
            mainOffset,
            arrays,
            indexEntries,
            objectArray,
            objectMap,
            local,
            external,
            fileIndex,
            stringIndex,
            tracked);
    }

    public Gw2CntcArrayDescriptor GetArray(Gw2CntcArrayKind kind) => GetArray(_arrays, kind);

    public IEnumerable<Gw2CntcObject> EnumerateObjects(uint? type = null)
    {
        foreach (var obj in _objects)
        {
            if (type is null || obj.Type == type.Value)
                yield return obj;
        }
    }

    public bool TryGetObject(uint offset, out Gw2CntcObject obj) => _objectsByOffset.TryGetValue(offset, out obj);

    public bool TryFindOwningObject(uint offset, out Gw2CntcObject obj)
    {
        if (_objectsByOffset.TryGetValue(offset, out obj)) return true;

        var index = LowerBound(_objects, offset);
        if (index == 0)
        {
            obj = default;
            return false;
        }

        obj = _objects[index - 1];
        return offset < obj.EndOffset;
    }

    public Gw2CntcContentKey ReadContentKey(Gw2CntcObject obj) =>
        Gw2CntcContentKey.Read(ReadContent(obj.Offset, 16).Span);

    public bool TryReadContentUInt32(Gw2CntcObject obj, int relativeOffset, out uint value)
    {
        if (relativeOffset < 0 || obj.Length < 4 || (uint)relativeOffset > obj.Length - 4)
        {
            value = 0;
            return false;
        }

        value = ReadContentUInt32(checked(obj.Offset + (uint)relativeOffset));
        Gw2CntcContentAccess.Note(this, obj.Offset + (uint)relativeOffset, 4);
        return true;
    }

    public uint ReadContentUInt32(Gw2CntcObject obj, int relativeOffset)
    {
        if (!TryReadContentUInt32(obj, relativeOffset, out var value))
            throw new ArgumentOutOfRangeException(nameof(relativeOffset));
        return value;
    }

    public ReadOnlyMemory<byte> ReadContent(uint offset, int length)
    {
        if (length < 0 || (ulong)offset + (uint)length > ContentByteCount)
            throw new ArgumentOutOfRangeException(nameof(length));

        Gw2CntcContentAccess.Note(this, offset, length);
        return _data.Slice(checked((int)(ContentOffset + offset)), length);
    }

    public Gw2CntcRow ReadRow(uint offset, int length)
    {
        if (length < 0 || (ulong)offset + (uint)length > ContentByteCount)
            throw new ArgumentOutOfRangeException(nameof(length));
        return new Gw2CntcRow(this, offset, length);
    }

    public bool TryResolvePointer(uint relocationOffset, uint ownPackIndex, out Gw2CntcPointer pointer)
    {
        Gw2CntcContentAccess.Note(this, relocationOffset, Gw2CntcContentAccess.PointerSize);
        var local = LowerBound(_localFixupOffsets, relocationOffset);
        if (local < _localFixupOffsets.Length && _localFixupOffsets[local] == relocationOffset)
        {
            pointer = new(ownPackIndex, ReadContentUInt32(relocationOffset), false);
            return true;
        }

        var external = LowerBound(_externalFixups, relocationOffset);
        if (external < _externalFixups.Length && _externalFixups[external].RelocationOffset == relocationOffset)
        {
            var fixup = _externalFixups[external];
            pointer = new(fixup.TargetFileIndex, fixup.TargetOffset, true);
            return true;
        }

        pointer = default;
        return false;
    }

    public IEnumerable<Gw2CntcLocalFixup> EnumerateLocalFixups(Gw2CntcObject obj)
    {
        var start = LowerBound(_localFixupOffsets, obj.Offset);
        for (var i = start; i < _localFixupOffsets.Length; i++)
        {
            var relocation = _localFixupOffsets[i];
            if (relocation >= obj.EndOffset)
                yield break;
            yield return new(relocation, ReadContentUInt32(relocation));
        }
    }

    public IEnumerable<Gw2CntcExternalFixup> EnumerateExternalFixups(Gw2CntcObject obj)
    {
        var start = LowerBound(_externalFixups, obj.Offset);
        for (var i = start; i < _externalFixups.Length; i++)
        {
            var fixup = _externalFixups[i];
            if (fixup.RelocationOffset >= obj.EndOffset)
                yield break;
            yield return fixup;
        }
    }

    public IEnumerable<uint> EnumerateFileIndexFixups(Gw2CntcObject obj) =>
        EnumerateRelocations(_fileIndexFixupOffsets, obj);

    public IEnumerable<uint> EnumerateStringIndexFixups(Gw2CntcObject obj) =>
        EnumerateRelocations(_stringIndexFixupOffsets, obj);

    public IEnumerable<Gw2CntcTrackedReference> EnumerateTrackedReferences(Gw2CntcObject obj)
    {
        var start = LowerBound(_trackedReferences, obj.Offset);
        for (var i = start; i < _trackedReferences.Length; i++)
        {
            var reference = _trackedReferences[i];
            if (reference.SourceOffset >= obj.EndOffset)
                yield break;
            yield return reference;
        }
    }

    public IEnumerable<uint> EnumerateObjectFileIds(Gw2CntcObject obj)
    {
        foreach (var relocation in EnumerateFileIndexFixups(obj))
            yield return ReadContentUInt32(relocation);
    }

    public IEnumerable<string> EnumerateObjectStrings(Gw2CntcObject obj)
    {
        foreach (var relocation in EnumerateStringIndexFixups(obj))
        {
            var index = ReadContentUInt32(relocation);
            if (TryReadString(index, out var value))
                yield return value;
        }
    }

    public bool TryReadFileReferenceAt(uint relocationOffset, IReadOnlyList<Gw2CntcPack> contentStore, out uint fileId)
    {
        Gw2CntcContentAccess.Note(this, relocationOffset, Gw2CntcContentAccess.PointerSize);
        fileId = 0;
        var index = LowerBound(_fileIndexFixupOffsets, relocationOffset);
        if (index >= _fileIndexFixupOffsets.Length || _fileIndexFixupOffsets[index] != relocationOffset) return false;
        return contentStore.Count > 0 && contentStore[0].TryReadFileReference(ReadContentUInt32(relocationOffset), out fileId);
    }

    public bool TryReadFileReference(uint index, out uint fileId)
    {
        fileId = 0;
        var references = GetArray(Gw2CntcArrayKind.FileReferences);
        if (index >= references.Count) return false;

        var entry = checked(references.Offset + index * 8);
        var relative = BinaryPrimitives.ReadInt64LittleEndian(_data.Span.Slice(checked((int)entry), 8));
        var referenceOffset = checked((long)entry + relative);
        if (referenceOffset < 0 || referenceOffset + 6 > _data.Length) return false;

        return PackFileReference.Parse(_data.Span.Slice((int)referenceOffset, 6)).TryGetFileId(out fileId);
    }

    public bool TryReadString(uint index, out string value)
    {
        var strings = GetArray(Gw2CntcArrayKind.Strings);
        if (index >= strings.Count)
        {
            value = string.Empty;
            return false;
        }

        var entry = checked(strings.Offset + index * 8);
        var relative = BinaryPrimitives.ReadInt64LittleEndian(_data.Span.Slice(checked((int)entry + 0), 8));
        var stringOffset = checked((long)entry + relative);
        if (stringOffset < 0 || stringOffset + 2 > _data.Length)
        {
            value = string.Empty;
            return false;
        }

        var bytes = _data.Span[(int)stringOffset..];
        var length = 0;
        while (length + 1 < bytes.Length && (bytes[length] != 0 || bytes[length + 1] != 0))
            length += 2;
        value = Encoding.Unicode.GetString(bytes[..length]);
        return true;
    }

    public IReadOnlyList<Gw2CntcObject> TraverseLocalObjectGraph(Gw2CntcObject root, int maxObjects = 4096)
    {
        if (maxObjects <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxObjects));

        var result = new List<Gw2CntcObject>();
        var pending = new Queue<Gw2CntcObject>();
        var seen = new HashSet<uint>();
        pending.Enqueue(root);
        while (pending.Count != 0 && result.Count < maxObjects)
        {
            var current = pending.Dequeue();
            if (!seen.Add(current.Offset))
                continue;

            result.Add(current);
            foreach (var fixup in EnumerateLocalFixups(current))
            {
                if (TryGetObject(fixup.TargetOffset, out var child) && !seen.Contains(child.Offset))
                    pending.Enqueue(child);
            }
        }

        return result;
    }

    private IEnumerable<uint> EnumerateRelocations(uint[] relocations, Gw2CntcObject obj)
    {
        var start = LowerBound(relocations, obj.Offset);
        for (var i = start; i < relocations.Length; i++)
        {
            var relocation = relocations[i];
            if (relocation >= obj.EndOffset)
                yield break;
            yield return relocation;
        }
    }

    private uint ReadContentUInt32(uint offset) =>
        ReadUInt32(_data.Span, checked((int)(ContentOffset + offset)));

    private static Gw2CntcArrayDescriptor GetArray(
        Gw2CntcArrayDescriptor[] arrays,
        Gw2CntcArrayKind kind) => arrays[(int)kind];

    private static uint[] ReadUInt32Array(
        ReadOnlySpan<byte> data,
        Gw2CntcArrayDescriptor descriptor,
        int elementSize)
    {
        ValidateArray(data, descriptor, elementSize);
        var values = new uint[checked((int)descriptor.Count)];
        for (var i = 0; i < values.Length; i++)
            values[i] = ReadUInt32(data, checked((int)descriptor.Offset + i * elementSize));
        return values;
    }

    private static void ValidateArray(
        ReadOnlySpan<byte> data,
        Gw2CntcArrayDescriptor descriptor,
        int elementSize)
    {
        if (descriptor.Count > int.MaxValue)
            throw new InvalidDataException($"cntc {descriptor.Kind} array is too large");
        if (descriptor.Count == 0)
            return;

        var length = checked((ulong)descriptor.Count * (uint)elementSize);
        if (descriptor.Offset > data.Length || length > (ulong)data.Length - descriptor.Offset)
            throw new InvalidDataException($"cntc {descriptor.Kind} array extends beyond the decoded payload");
    }

    private static uint ReadContentUInt32(
        ReadOnlySpan<byte> data,
        Gw2CntcArrayDescriptor content,
        uint offset) =>
        ReadUInt32(data, checked((int)(content.Offset + offset)));

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));

    private static long ReadInt64(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));

    private static int LowerBound(uint[] values, uint value)
    {
        var left = 0;
        var right = values.Length;
        while (left < right)
        {
            var middle = left + ((right - left) >> 1);
            if (values[middle] < value)
                left = middle + 1;
            else
                right = middle;
        }

        return left;
    }

    private static int LowerBound(Gw2CntcExternalFixup[] values, uint value)
    {
        var left = 0;
        var right = values.Length;
        while (left < right)
        {
            var middle = left + ((right - left) >> 1);
            if (values[middle].RelocationOffset < value)
                left = middle + 1;
            else
                right = middle;
        }

        return left;
    }

    private static int LowerBound(Gw2CntcObject[] values, uint value)
    {
        var left = 0;
        var right = values.Length;
        while (left < right)
        {
            var middle = left + ((right - left) >> 1);
            if (values[middle].Offset < value)
                left = middle + 1;
            else
                right = middle;
        }

        return left;
    }

    private static int LowerBound(Gw2CntcTrackedReference[] values, uint value)
    {
        var left = 0;
        var right = values.Length;
        while (left < right)
        {
            var middle = left + ((right - left) >> 1);
            if (values[middle].SourceOffset < value)
                left = middle + 1;
            else
                right = middle;
        }

        return left;
    }
}
