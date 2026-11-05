namespace Gw2.Dat.Content;

public static class Gw2MaterialTokens
{
    public const uint Diffuse = 1733499172;

    public const uint Normal = 404146670;

    public const uint DetailOverlay = 12618916;

    public const uint DetailMultiply = 25170413;

    public const uint DetailModulate = 3347059181;

    public const uint DetailNormal = 679716008;

    public const uint TintMask = 1683634484;

    public const uint DiffuseB = 0x0005CC2D;

    public const uint NormalB = 0x24FA4D24;

    public static bool IsDiffuse(ulong token) => (uint)token == Diffuse || (uint)token == DiffuseB;

    public static bool IsNormal(ulong token) => (uint)token == Normal || (uint)token == NormalB;

    public static string RoleOf(ulong token) => (uint)token switch
    {
        Diffuse or DiffuseB => "diffuse",
        Normal or NormalB => "normal",
        DetailOverlay => "detailOverlay",
        DetailMultiply => "detailMultiply",
        DetailModulate => "detailModulate",
        DetailNormal => "detailNormal",
        TintMask => "tintMask",
        _ => "unknown",
    };
}
