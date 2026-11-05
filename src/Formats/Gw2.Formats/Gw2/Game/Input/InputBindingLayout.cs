using System;
using System.Collections.Generic;

namespace Gw2.Formats;

public sealed record InputBindingLayout(
    uint ActionContentType,
    int ControlIdOffset,
    int NameTextIdOffset,
    int PrimaryBindOffset,
    int SecondaryBindOffset,
    IReadOnlyDictionary<byte, uint> ControlIdByPackedControl,
    IReadOnlyDictionary<byte, InputDevice> DeviceByPackedDevice,
    IReadOnlyList<(uint Raw, uint Packed)> ModifierPacking)
{
    private static readonly CodeAnchor ActionContentWalk = CodeAnchor.Site(
        "BA ?? 00 00 00 44 89 A4 24 ?? 00 00 00 4C 8B B8 ?? ?? 00 00 49 8B CF 49 8B 07 FF 50 48", 1);

    private static readonly CodeAnchor ControlIdRead = CodeAnchor.Site(
        "48 8B D6 48 8B C8 E8 ?? ?? ?? ?? 48 8B E8 EB 03 49 8B EC 8B 76 ??", 21);

    private static readonly CodeAnchor NameTextIdRead = CodeAnchor.Site(
        "8B 48 ?? 89 4C 24 30 48 8D 4C 24 30 E8 ?? ?? ?? ?? 48 83 C4 20 5B C3 CC CC CC CC CC CC CC CC CC CC CC CC 40 53 48 83 EC 20 48 83 79 08 00 48 8B D9 75 19 41 B8 ?? 00 00 00 48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 43 08 8B 48 ?? 89 4C 24 30", 82);

    private static readonly CodeAnchor DefaultBindReads = CodeAnchor.Site(
        "48 8B 52 ?? 4C 8B F1 48 83 C1 10 41 B8 01 00 00 00 E8 ?? ?? ?? ?? 48 8B 57 ??", 0);

    private static readonly CodeAnchor PackedControlMap = CodeAnchor.Reference(
        "48 83 EC 28 8B D1 4C 8D 44 24 38 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 85 C0 75 07 B0 DF", 14, 18);

    private static readonly CodeAnchor PackedDeviceMap = CodeAnchor.Reference(
        "48 83 EC 28 8B D1 4C 8D 44 24 38 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 85 C0 75 18 4C 8D 05 ?? ?? ?? ?? BA C6 03 00 00", 14, 18);

    private static readonly CodeAnchor ModifierPackingTable = CodeAnchor.Reference(
        "48 89 5C 24 08 57 48 83 EC 20 48 8B 05 ?? ?? ?? ?? 48 8B F9 44 8B 05 ?? ?? ?? ?? 33 DB 4A 8D 0C C0 48 3B C1 73 31 85 10 74 03 0B 58 04 48 83 C0 08 48 3B C1 72 F0 81 FB FF 00 00 00 76 19 41 B8 DC 03 00 00", 13, 17);

    private const int PrimaryBindDisplacement = 3;
    private const int SecondaryBindDisplacement = 25;
    private const int TableLeaLength = 7;
    private const int CountMoveLength = 5;
    private const int RexPrefixLength = 1;
    private const byte CountMoveOpcode = 0xBA;
    private const int PairSize = 8;

    private static InputBindingLayout? _current;

    public static InputBindingLayout Current => _current ??= FromExecutable(ExecutableImage.Current);

    public static InputBindingLayout FromExecutable(string executablePath) => FromExecutable(ExecutableImage.Open(executablePath));

    internal static InputBindingLayout FromExecutable(ExecutableImage executable)
    {
        var defaultBinds = DefaultBindReads.Locate(executable);
        var controls = new Dictionary<byte, uint>();
        foreach (var (control, packed) in RegisteredPairs(executable, PackedControlMap.Locate(executable))) controls[(byte)packed] = control;
        var devices = new Dictionary<byte, InputDevice>();
        foreach (var (device, packed) in RegisteredPairs(executable, PackedDeviceMap.Locate(executable))) devices[(byte)packed] = (InputDevice)device;
        var modifierTable = ModifierPackingTable.Locate(executable);
        return new InputBindingLayout(
            executable.Bytes(ActionContentWalk.Locate(executable), 1)[0],
            executable.Bytes(ControlIdRead.Locate(executable), 1)[0],
            executable.Bytes(NameTextIdRead.Locate(executable), 1)[0],
            executable.Bytes(defaultBinds + PrimaryBindDisplacement, 1)[0],
            executable.Bytes(defaultBinds + SecondaryBindDisplacement, 1)[0],
            controls,
            devices,
            Pairs(executable, executable.Qword(modifierTable), executable.Dword(modifierTable + 8)));
    }

    public byte PackModifiers(InputModifiers modifiers)
    {
        uint packed = 0;
        foreach (var (rawBit, packedBit) in ModifierPacking)
            if (((uint)modifiers & rawBit) != 0) packed |= packedBit;
        return (byte)packed;
    }

    public byte? PackedControlOf(uint controlId)
    {
        foreach (var (packed, control) in ControlIdByPackedControl)
            if (control == controlId) return packed;
        return null;
    }

    public byte PackedDeviceOf(InputDevice device)
    {
        foreach (var (packed, unpacked) in DeviceByPackedDevice)
            if (unpacked == device) return packed;
        return 0;
    }

    public InputModifiers UnpackModifiers(byte packed)
    {
        uint raw = 0;
        foreach (var (rawBit, packedBit) in ModifierPacking)
            if ((packed & packedBit) != 0) raw |= rawBit;
        return (InputModifiers)raw;
    }

    private static List<(uint, uint)> RegisteredPairs(ExecutableImage executable, ulong map)
    {
        foreach (var mapLea in executable.LeaReferences([map]))
        {
            var countMove = mapLea - RexPrefixLength - CountMoveLength;
            var tableLea = countMove - TableLeaLength;
            if (executable.Bytes(countMove, 1)[0] != CountMoveOpcode || executable.Bytes(tableLea, 3) is not [0x4C, 0x8D, 0x05]) continue;
            var table = tableLea + TableLeaLength + (ulong)(long)(int)executable.Dword(tableLea + 3);
            return Pairs(executable, table, executable.Dword(countMove + 1));
        }
        throw new InvalidOperationException("The packed input binding map has no registration.");
    }

    private static List<(uint, uint)> Pairs(ExecutableImage executable, ulong table, uint count)
    {
        var pairs = new List<(uint, uint)>((int)count);
        for (uint index = 0; index < count; index++)
            pairs.Add((executable.Dword(table + index * PairSize), executable.Dword(table + index * PairSize + 4)));
        return pairs;
    }
}
