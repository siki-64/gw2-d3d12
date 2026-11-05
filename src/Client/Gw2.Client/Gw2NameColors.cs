using System.Numerics;

namespace Gw2.Client;

public static class Gw2NameColors
{
    public static Vector4 Of(Gw2VisibleAttitude visibleAttitude) => FromArgb(visibleAttitude switch
    {
        Gw2VisibleAttitude.Controlled or Gw2VisibleAttitude.IndifferentWhiteName => 0xFFF0F0F0,
        Gw2VisibleAttitude.Player => 0xFF06D8AE,
        Gw2VisibleAttitude.Friendly or Gw2VisibleAttitude.Neutral => 0xFF50E050,
        Gw2VisibleAttitude.Hostile => 0xFFE95050,
        Gw2VisibleAttitude.Indifferent => 0xFFF1E630,
        _ => 0xFFF0F0F0,
    });

    private static Vector4 FromArgb(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f,
        ((argb >> 8) & 0xFF) / 255f,
        (argb & 0xFF) / 255f,
        (argb >> 24) / 255f);
}
