using System.Numerics;

namespace Gw2.Client.Graphics;

public static class GrShadowConstants
{
    public const int MaximumCascades = 4;

    public static readonly Vector4 NoCascades = Vector4.Zero;

    public static Vector4 Configuration(int cascadeCount, int atlasWidth, int atlasHeight) =>
        new(0f, cascadeCount, 1f / atlasWidth, 1f / atlasHeight);
}
