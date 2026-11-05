using System.Runtime.InteropServices;

namespace Gw2.Formats;

internal sealed unsafe class PackfileUpgrade
{
    private readonly List<nint> _allocations;
    private readonly Dictionary<nint, nint> _convertedBySource = [];
    private readonly Queue<(nint Source, nint Destination, PackType SourceType, PackType DestinationType, uint Count)> _pendingStructs = [];

    private PackfileUpgrade(List<nint> allocations) => _allocations = allocations;

    internal static byte* Convert(byte* source, PackType sourceType, PackType destinationType, List<nint> allocations)
    {
        var upgrade = new PackfileUpgrade(allocations);
        byte* destination = upgrade.Allocate(destinationType.Size);
        upgrade.ConvertStruct(source, sourceType, destination, destinationType);
        while (upgrade._pendingStructs.TryDequeue(out var pending))
        {
            int sourceSize = pending.SourceType.Size;
            int destinationSize = pending.DestinationType.Size;
            for (uint index = 0; index < pending.Count; index++)
                upgrade.ConvertStruct((byte*)pending.Source + index * sourceSize, pending.SourceType, (byte*)pending.Destination + index * destinationSize, pending.DestinationType);
        }
        return destination;
    }

    private byte* Allocate(int size)
    {
        var memory = (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(size, 1));
        _allocations.Add((nint)memory);
        return memory;
    }

    private void ConvertStruct(byte* source, PackType sourceType, byte* destination, PackType destinationType)
    {
        int destinationOffset = 0;
        for (int fieldIndex = 0; fieldIndex < destinationType.Fields.Length; fieldIndex++)
        {
            PackField destinationField = destinationType.Fields[fieldIndex];
            int destinationSize = destinationField.Size;
            var match = ReferenceEquals(sourceType, destinationType)
                ? (destinationField, destinationOffset)
                : FindMatch(destinationField, sourceType);
            if (match is { } found)
                ConvertField(source + found.Offset, found.Field, destination + destinationOffset, destinationField);
            destinationOffset += destinationSize;
        }
    }

    internal static (PackField Field, int Offset)? FindMatch(PackField destinationField, PackType sourceType)
    {
        int offset = 0;
        foreach (PackField sourceField in sourceType.Fields)
        {
            if (sourceField.Name == destinationField.Name && KindsMatch(sourceField.Kind, destinationField.Kind)
                && (sourceField.Reference is null) == (destinationField.Reference is null))
                return (sourceField, offset);
            offset += sourceField.Size;
        }
        return null;
    }

    private static bool KindsMatch(PackFieldKind source, PackFieldKind destination) =>
        source == destination
        || IsQword(source) && IsQword(destination)
        || IsDword(source) && IsDword(destination);

    private static bool IsQword(PackFieldKind kind) => kind is PackFieldKind.Qword or PackFieldKind.QwordToken;

    private static bool IsDword(PackFieldKind kind) => kind is PackFieldKind.Dword or PackFieldKind.DwordToken;

    private void ConvertField(byte* source, PackField sourceField, byte* destination, PackField destinationField)
    {
        switch (destinationField.Kind)
        {
            case PackFieldKind.FixedArray:
                int sourceElementSize = sourceField.Reference!.Size;
                int destinationElementSize = destinationField.Reference!.Size;
                for (int index = 0; index < destinationField.Count && index < sourceField.Count; index++)
                    ConvertStruct(source + index * sourceElementSize, sourceField.Reference, destination + index * destinationElementSize, destinationField.Reference);
                break;
            case PackFieldKind.Array:
            case PackFieldKind.WordArray:
            case PackFieldKind.ByteArray:
            {
                int countSize = CountSize(destinationField.Kind);
                uint count = ReadCount(source, countSize);
                WriteCount(destination, countSize, count);
                *(byte**)(destination + countSize) = ConvertedStructs(*(byte**)(source + countSize), count, sourceField.Reference!, destinationField.Reference!);
                break;
            }
            case PackFieldKind.PointerArray:
            case PackFieldKind.WordPointerArray:
            case PackFieldKind.BytePointerArray:
            {
                int countSize = CountSize(destinationField.Kind);
                uint count = ReadCount(source, countSize);
                WriteCount(destination, countSize, count);
                *(byte**)(destination + countSize) = ConvertedPointers(*(byte**)(source + countSize), count, sourceField.Reference!, destinationField.Reference!);
                break;
            }
            case PackFieldKind.Variant:
            {
                uint variant = *(uint*)source;
                if (variant >= sourceField.Variants.Length) throw new InvalidDataException("Packfile variant index is out of range.");
                *(uint*)destination = variant;
                *(byte**)(destination + 4) = ConvertedStructs(*(byte**)(source + 4), 1, sourceField.Variants[variant], destinationField.Variants[variant]);
                break;
            }
            case PackFieldKind.Pointer:
                *(byte**)destination = ConvertedStructs(*(byte**)source, 1, sourceField.Reference!, destinationField.Reference!);
                break;
            case PackFieldKind.Struct:
            case PackFieldKind.StructBase:
                ConvertStruct(source, sourceField.Reference!, destination, destinationField.Reference!);
                break;
            case PackFieldKind.TypedArray:
            case PackFieldKind.WordTypedArray:
            case PackFieldKind.ByteTypedArray:
                throw new NotSupportedException("Typed packfile arrays are not used by any registered chunk.");
            default:
                Buffer.MemoryCopy(source, destination, destinationField.Size, destinationField.Size);
                break;
        }
    }

    private static int CountSize(PackFieldKind kind) => kind switch
    {
        PackFieldKind.WordArray or PackFieldKind.WordPointerArray => 2,
        PackFieldKind.ByteArray or PackFieldKind.BytePointerArray => 1,
        _ => 4,
    };

    private static uint ReadCount(byte* source, int countSize) => countSize switch
    {
        1 => *source,
        2 => *(ushort*)source,
        _ => *(uint*)source,
    };

    private static void WriteCount(byte* destination, int countSize, uint count)
    {
        switch (countSize)
        {
            case 1: *destination = (byte)count; break;
            case 2: *(ushort*)destination = (ushort)count; break;
            default: *(uint*)destination = count; break;
        }
    }

    private byte* ConvertedStructs(byte* source, uint count, PackType sourceType, PackType destinationType)
    {
        if (source == null || count == 0) return null;
        if (_convertedBySource.TryGetValue((nint)source, out nint existing)) return (byte*)existing;
        byte* destination = Allocate(checked((int)count * destinationType.Size));
        _convertedBySource[(nint)source] = (nint)destination;
        _pendingStructs.Enqueue(((nint)source, (nint)destination, sourceType, destinationType, count));
        return destination;
    }

    private byte* ConvertedPointers(byte* source, uint count, PackType sourceType, PackType destinationType)
    {
        if (source == null || count == 0) return null;
        if (_convertedBySource.TryGetValue((nint)source, out nint existing)) return (byte*)existing;
        var destination = (byte**)Allocate(checked((int)count * sizeof(nint)));
        _convertedBySource[(nint)source] = (nint)destination;
        var sourcePointers = (byte**)source;
        for (uint index = 0; index < count; index++)
            destination[index] = ConvertedStructs(sourcePointers[index], 1, sourceType, destinationType);
        return (byte*)destination;
    }
}
