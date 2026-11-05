namespace Gw2.Dat.Content;

public static class Gw2MapZoneModelFlags
{
    private static readonly (uint Packed, uint Runtime)[] PackedToRuntimeBits =
    [
        (0x1, 0x1),
        (0x2, 0x2),
        (0x200000, 0x4),
        (0x4, 0x8),
        (0x8, 0x10),
        (0x20000, 0x20),
        (0x10, 0x40),
        (0x20, 0x80),
        (0x200, 0x100),
        (0x400000, 0x200),
        (0x80, 0x400),
        (0x100, 0x800),
        (0x400, 0x2000),
        (0x800, 0x4000),
        (0x1000, 0x8000),
        (0x2000, 0x10000),
        (0x10000, 0x80000),
        (0x40000, 0x1000),
        (0x80000, 0x20000),
        (0x100000, 0x40000),
        (0x800000, 0x100000),
        (0x1000000, 0x200000),
        (0x2000000, 0x400000),
    ];

    public static uint FromPacked(uint packed)
    {
        var runtime = 0u;
        foreach (var (packedBit, runtimeBit) in PackedToRuntimeBits)
            if ((packed & packedBit) != 0) runtime |= runtimeBit;
        return runtime;
    }
}
