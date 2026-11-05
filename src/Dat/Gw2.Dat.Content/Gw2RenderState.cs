namespace Gw2.Dat.Content;

public enum Gw2BlendMode
{
    Opaque,
    Alpha,
    Normal,
    Additive,
    Screen,
    Multiply,
    Modulate,
    Other,
}

public static class Gw2RenderState
{
    public const int BlendShift = 12;
    public const int AlphaRefShift = 40;
    public const ulong BlendMask = 0x000000000ffff000UL;
    public const ulong BlendEquationMask = 0x00000003f0000000UL;
    public const ulong DepthWriteFlag = 0x0000004000000000UL;
    public const ulong CullMask = 0x0000003000000000UL;
    public const ulong AlphaRefMask = 0x0000ff0000000000UL;

    public static int SrcRgb(ulong state) => (int)((state >> BlendShift) & 0xF);

    public static int DstRgb(ulong state) => (int)((state >> (BlendShift + 4)) & 0xF);

    public static int SrcAlpha(ulong state) => (int)((state >> (BlendShift + 8)) & 0xF);

    public static int DstAlpha(ulong state) => (int)((state >> (BlendShift + 12)) & 0xF);

    public static int AlphaRef(ulong state) => (int)((state >> AlphaRefShift) & 0xFF);

    public static bool WritesDepth(ulong state) => (state & DepthWriteFlag) != 0;

    public static bool Culls(ulong state) => (state & CullMask) != 0;

    public static bool Blends(ulong state) => (state & BlendMask) != 0;

    public static Gw2BlendMode BlendMode(ulong state)
    {
        if (!Blends(state)) return Gw2BlendMode.Opaque;
        return (SrcRgb(state), DstRgb(state), SrcAlpha(state), DstAlpha(state)) switch
        {
            (5, 6, 5, 6) => Gw2BlendMode.Alpha,
            (2, 6, 2, 6) => Gw2BlendMode.Normal,
            (5, 2, 5, 2) => Gw2BlendMode.Additive,
            (2, 4, 2, 4) => Gw2BlendMode.Screen,
            (9, 1, 9, 1) => Gw2BlendMode.Multiply,
            (9, 3, 9, 3) => Gw2BlendMode.Modulate,
            _ => Gw2BlendMode.Other,
        };
    }

    public static string SurfaceClass(ulong state, bool alphaTested) =>
        Blends(state) ? "Transparent" : alphaTested ? "AlphaTest" : "Opaque";
}
