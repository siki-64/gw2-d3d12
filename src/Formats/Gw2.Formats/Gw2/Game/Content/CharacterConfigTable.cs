using System;
using System.Collections.Generic;

namespace Gw2.Formats;

public sealed record CharacterConfigEntry(int Config, uint ValueContentType, Guid ContentKey);

public sealed class CharacterConfigTable
{
    public const int EnergyDefaultChangePerSecondConfig = 509;
    public const int EnergyMaximumConfig = 510;

    private const int EntrySize = 72;
    private const int ValueContentTypeOffset = 4;
    private const int ContentKeyOffset = 8;
    private const int GuidSize = 16;
    private const int ImmediateStoreLength = 10;
    private const byte ImmediateStoreOpcode = 0xC7;
    private const byte RipRelativeDword = 0x05;

    private static readonly CodeAnchor TableLookup = CodeAnchor.Reference("4C 8D 04 C0 48 8D 05 ?? ?? ?? ?? 42 39 0C C0", 7, 11);
    private static readonly CodeAnchor ConfigurationChecks = CodeAnchor.Site(
        "81 78 10 ?? ?? ?? ?? 74 ?? 48 8D 4B 08 BA 25 00 00 00 4C 8D 44 24 20 E8 ?? ?? ?? ?? 4C 8D 05 ?? ?? ?? ?? EB ?? " +
        "39 78 ?? 74 ?? 48 8D 4B 08 BA 25 00 00 00 4C 8D 44 24 20 E8 ?? ?? ?? ?? 4C 8D 05 ?? ?? ?? ?? EB ?? 48 83 C0 ??", 0);
    private const int ConfigurationTypeImmediate = 3;
    private const int ValueTypeDisplacement = 39;
    private const int ValueDisplacement = 73;
    private const int ConfigCountLimit = 4096;

    private static CharacterConfigTable? _current;

    private readonly IReadOnlyDictionary<int, CharacterConfigEntry> _entries;

    private CharacterConfigTable(uint configurationContentType, int valueTypeOffset, int valueOffset, IReadOnlyDictionary<int, CharacterConfigEntry> entries)
    {
        ConfigurationContentType = configurationContentType;
        ValueTypeOffset = valueTypeOffset;
        ValueOffset = valueOffset;
        _entries = entries;
    }

    public uint ConfigurationContentType { get; }

    public int ValueOffset { get; }

    public int ValueTypeOffset { get; }

    public int NumberOffset => ValueOffset + 8;

    public static CharacterConfigTable Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static CharacterConfigTable FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    public CharacterConfigEntry? Entry(int config) => _entries.TryGetValue(config, out var entry) ? entry : null;

    internal static CharacterConfigTable FromExecutable(ExecutableImage executable)
    {
        ulong table = TableLookup.Locate(executable);
        var stores = ImmediateStores(executable);
        var entries = new Dictionary<int, CharacterConfigEntry>();
        for (int config = 0; config < ConfigCountLimit; config++)
        {
            ulong entry = table + (ulong)(config * EntrySize);
            if (!stores.TryGetValue(entry, out var storedConfig) || storedConfig != config || !stores.TryGetValue(entry + ValueContentTypeOffset, out var valueType)) continue;
            var key = new byte[GuidSize];
            bool complete = true;
            for (int part = 0; part < GuidSize; part += 4)
            {
                if (!stores.TryGetValue(entry + ContentKeyOffset + (ulong)part, out var word)) { complete = false; break; }
                BitConverter.TryWriteBytes(key.AsSpan(part), word);
            }
            if (complete) entries[config] = new CharacterConfigEntry(config, valueType, new Guid(key));
        }
        ulong checks = ConfigurationChecks.Locate(executable);
        return new CharacterConfigTable(
            executable.Dword(checks + ConfigurationTypeImmediate),
            executable.Bytes(checks + ValueTypeDisplacement, 1)[0],
            executable.Bytes(checks + ValueDisplacement, 1)[0],
            entries);
    }

    private static Dictionary<ulong, uint> ImmediateStores(ExecutableImage executable)
    {
        var (start, _) = executable.Section(".text");
        var code = executable.SectionBytes(".text");
        var stores = new Dictionary<ulong, uint>();
        for (int at = 0; at + ImmediateStoreLength <= code.Length; at++)
        {
            if (code[at] != ImmediateStoreOpcode || code[at + 1] != RipRelativeDword) continue;
            ulong target = start + (ulong)(at + ImmediateStoreLength) + (ulong)(long)BitConverter.ToInt32(code.Slice(at + 2, 4));
            stores[target] = BitConverter.ToUInt32(code.Slice(at + 6, 4));
        }
        return stores;
    }
}
