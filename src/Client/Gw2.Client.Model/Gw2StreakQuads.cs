using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public static class Gw2StreakQuads
{
    private const uint AgeBlendedColourFlag = 0x4;

    public static void Build(Gw2StreakSimulator simulator, IReadOnlyList<Gw2StreakAnchor> anchors, Gw2ParticleCamera camera, List<Gw2ParticleCorner> corners)
    {
        var rows = simulator.Rows;
        for (var row = 1; row < rows.Count; row++)
        {
            var older = rows[row - 1];
            var newer = rows[row];
            var anchorCount = Math.Min(Math.Min(older.Positions.Length, newer.Positions.Length), anchors.Count);
            for (var anchor = 1; anchor < anchorCount; anchor += 2)
            {
                var centre = (older.Positions[anchor - 1] + older.Positions[anchor] + newer.Positions[anchor - 1] + newer.Positions[anchor]) * 0.25f;
                AddCorner(corners, centre, older.Positions[anchor - 1], ColourOf(anchors[anchor - 1], older), new(older.TextureU, anchors[anchor - 1].TexV), camera);
                AddCorner(corners, centre, newer.Positions[anchor - 1], ColourOf(anchors[anchor - 1], newer), new(newer.TextureU, anchors[anchor - 1].TexV), camera);
                AddCorner(corners, centre, newer.Positions[anchor], ColourOf(anchors[anchor], newer), new(newer.TextureU, anchors[anchor].TexV), camera);
                AddCorner(corners, centre, older.Positions[anchor], ColourOf(anchors[anchor], older), new(older.TextureU, anchors[anchor].TexV), camera);
            }
        }
    }

    public static Vector4 ColourOf(Gw2StreakAnchor anchor, Gw2StreakRow row)
    {
        var start = UnpackArgb(anchor.ColorStart);
        if (anchor.LifetimeSeconds <= 0f || row.AgeSeconds >= anchor.LifetimeSeconds) return start with { W = 0f };
        if ((anchor.Flags & AgeBlendedColourFlag) == 0) return start;
        return Vector4.Lerp(start, UnpackArgb(anchor.ColorEnd), row.AgeSeconds / anchor.LifetimeSeconds);
    }

    public static Vector4 UnpackArgb(uint packed) =>
        new(((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f, (packed >> 24) / 255f);

    private static void AddCorner(List<Gw2ParticleCorner> corners, Vector3 centre, Vector3 corner, Vector4 colour, Vector2 texCoord, Gw2ParticleCamera camera) =>
        corners.Add(new(corner, Gw2ParticleQuads.BentNormal(centre, corner, camera.Forward), colour, texCoord));
}
