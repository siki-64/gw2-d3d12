using System.Buffers.Binary;
using System.Text;

namespace Gw2.Formats;

internal sealed class ExecutableImage
{
    private const int PeHeaderOffsetField = 0x3C;
    private const int SectionCountOffset = 6;
    private const int SizeOfOptionalHeaderOffset = 20;
    private const int OptionalHeaderOffset = 24;
    private const int ImageBaseOffset = OptionalHeaderOffset + 24;
    private const int SectionHeaderSize = 40;
    private const int MaxNameLength = 256;
    private const int ExceptionDirectoryOffset = OptionalHeaderOffset + 112 + 3 * 8;
    private const int TlsDirectoryOffset = OptionalHeaderOffset + 112 + 9 * 8;
    private const int TlsAddressOfIndexOffset = 16;
    private const int RuntimeFunctionSize = 12;
    private const byte ChainedUnwindFlag = 0x4;
    private const byte CallOpcode = 0xE8;
    private const byte LeaOpcode = 0x8D;
    private const byte RipRelativeModRmMask = 0xC7;
    private const byte RipRelativeModRm = 0x05;

    private static ExecutableImage? _current;

    private readonly byte[] _image;
    private readonly (uint Begin, uint End, uint UnwindInfo)[] _functions;
    private readonly Dictionary<string, (uint VirtualAddress, uint Size, uint RawAddress)> _sections = [];

    internal ExecutableImage(byte[] image)
    {
        _image = image;
        int header = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(PeHeaderOffsetField));
        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(header + SectionCountOffset));
        int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(header + SizeOfOptionalHeaderOffset));
        ImageBase = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(header + ImageBaseOffset));
        for (int index = 0; index < sectionCount; index++)
        {
            var entry = image.AsSpan(header + OptionalHeaderOffset + optionalSize + SectionHeaderSize * index, SectionHeaderSize);
            string name = Encoding.ASCII.GetString(entry[..8]).TrimEnd('\0');
            uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
            uint rawAddress = BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]);
            _sections.TryAdd(name, (virtualAddress, Math.Min(virtualSize, rawSize), rawAddress));
        }
        uint tlsDirectory = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(header + TlsDirectoryOffset));
        TlsIndexAddress = tlsDirectory == 0 ? 0 : Qword(ImageBase + tlsDirectory + TlsAddressOfIndexOffset);
        _functions = ReadFunctionTable(BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(header + ExceptionDirectoryOffset)),
            BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(header + ExceptionDirectoryOffset + 4)));
    }

    internal ulong ImageBase { get; }

    internal ulong TlsIndexAddress { get; }

    internal static ExecutableImage Open(string path) => new(File.ReadAllBytes(path));

    internal static ExecutableImage Current => _current ??= Open(Gw2Installation.ExecutablePath() ?? Environment.ProcessPath!);

    internal (ulong Start, uint Size) Section(string name)
    {
        var (virtualAddress, size, _) = _sections[name];
        return (ImageBase + virtualAddress, size);
    }

    internal bool InSection(ulong address, string name)
    {
        if (!_sections.TryGetValue(name, out var section)) return false;
        ulong relative = address - ImageBase;
        return relative >= section.VirtualAddress && relative < section.VirtualAddress + section.Size;
    }

    internal int? Offset(ulong address)
    {
        ulong relative = address - ImageBase;
        foreach (var (virtualAddress, size, rawAddress) in _sections.Values)
            if (relative >= virtualAddress && relative < virtualAddress + size)
                return (int)(rawAddress + relative - virtualAddress);
        return null;
    }

    internal ReadOnlySpan<byte> Bytes(ulong address, int length) =>
        Offset(address) is { } offset && offset + length <= _image.Length ? _image.AsSpan(offset, length) : default;

    internal ulong Qword(ulong address) => Bytes(address, 8) is { Length: 8 } bytes ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : 0;

    internal string? AsciiName(ulong address)
    {
        if (Offset(address) is not { } start) return null;
        var window = _image.AsSpan(start, Math.Min(MaxNameLength, _image.Length - start));
        int end = window.IndexOf((byte)0);
        if (end < 0) return null;
        foreach (byte character in window[..end])
            if (character is < 0x20 or >= 0x7F) return null;
        return Encoding.ASCII.GetString(window[..end]);
    }

    internal ReadOnlySpan<byte> SectionBytes(string name)
    {
        var (start, size) = Section(name);
        return Bytes(start, (int)size);
    }

    internal uint Dword(ulong address) => Bytes(address, 4) is { Length: 4 } bytes ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : 0;

    internal float Single(ulong address) => BitConverter.Int32BitsToSingle((int)Dword(address));

    internal bool IsMapped(ulong address) => Offset(address) is not null;

    internal bool InCode(ulong address) => InSection(address, ".text");

    internal IEnumerable<ulong> FindAscii(string section, string text)
    {
        byte[] needle = [.. Encoding.ASCII.GetBytes(text), 0];
        var (start, _) = Section(section);
        var bytes = SectionBytes(section).ToArray();
        for (int index = bytes.AsSpan().IndexOf(needle); index >= 0;)
        {
            if (index == 0 || bytes[index - 1] == 0) yield return start + (ulong)index;
            int next = bytes.AsSpan(index + 1).IndexOf(needle);
            index = next < 0 ? -1 : index + 1 + next;
        }
    }

    internal List<ulong> LeaReferences(IReadOnlyCollection<ulong> targets)
    {
        var references = new List<ulong>();
        var (start, _) = Section(".text");
        var code = SectionBytes(".text");
        for (int index = 2; index + 4 <= code.Length; index++)
        {
            if ((code[index - 1] & RipRelativeModRmMask) != RipRelativeModRm || code[index - 2] != LeaOpcode) continue;
            ulong target = start + (ulong)index + 4 + (ulong)(long)BinaryPrimitives.ReadInt32LittleEndian(code[index..]);
            if (targets.Contains(target)) references.Add(start + (ulong)index - 2);
        }
        return references;
    }

    internal Dictionary<ulong, List<ulong>> CallSites(IReadOnlyCollection<ulong> targets)
    {
        var sites = targets.ToDictionary(target => target, _ => new List<ulong>());
        var (start, _) = Section(".text");
        var code = SectionBytes(".text");
        for (int index = 0; index + 5 <= code.Length; index++)
        {
            if (code[index] != CallOpcode) continue;
            ulong target = start + (ulong)index + 5 + (ulong)(long)BinaryPrimitives.ReadInt32LittleEndian(code[(index + 1)..]);
            if (sites.TryGetValue(target, out var list)) list.Add(start + (ulong)index);
        }
        return sites;
    }

    internal IEnumerable<ulong> CallTargets(ulong functionStart)
    {
        if (FunctionRange(functionStart) is not var (begin, end)) yield break;
        var code = Bytes(begin, (int)(end - begin)).ToArray();
        for (int index = 0; index + 5 <= code.Length; index++)
            if (code[index] == CallOpcode)
            {
                ulong target = begin + (ulong)index + 5 + (ulong)(long)BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(index + 1));
                if (FunctionStart(target) == target) yield return target;
            }
    }

    internal ulong? FunctionStart(ulong address) => FunctionRange(address)?.Begin;

    internal (ulong Begin, ulong End)? FunctionRange(ulong address)
    {
        if (address < ImageBase) return null;
        uint relative = (uint)(address - ImageBase);
        int low = 0, high = _functions.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            var function = _functions[middle];
            if (relative < function.Begin) high = middle - 1;
            else if (relative >= function.End) low = middle + 1;
            else return PrimaryFunction(function);
        }
        return null;
    }

    private (ulong Begin, ulong End) PrimaryFunction((uint Begin, uint End, uint UnwindInfo) function)
    {
        for (int depth = 0; depth < 32; depth++)
        {
            var unwind = Bytes(ImageBase + function.UnwindInfo, 4);
            if (unwind.Length < 4 || (unwind[0] >> 3 & ChainedUnwindFlag) == 0) break;
            int codeCount = unwind[2];
            ulong chained = ImageBase + function.UnwindInfo + 4 + (ulong)((codeCount + 1) & ~1) * 2;
            var entry = Bytes(chained, RuntimeFunctionSize);
            if (entry.Length < RuntimeFunctionSize) break;
            function = (BinaryPrimitives.ReadUInt32LittleEndian(entry), BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]), BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
        }
        return (ImageBase + function.Begin, ImageBase + function.End);
    }

    private (uint Begin, uint End, uint UnwindInfo)[] ReadFunctionTable(uint relativeAddress, uint size)
    {
        var table = Bytes(ImageBase + relativeAddress, (int)size);
        var functions = new (uint, uint, uint)[table.Length / RuntimeFunctionSize];
        for (int index = 0; index < functions.Length; index++)
        {
            var entry = table[(index * RuntimeFunctionSize)..];
            functions[index] = (BinaryPrimitives.ReadUInt32LittleEndian(entry), BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]), BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
        }
        Array.Sort(functions, (left, right) => left.Item1.CompareTo(right.Item1));
        return functions;
    }
}
