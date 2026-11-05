using System.Numerics;

namespace Gw2.Client.Hud;

public sealed class HudWeaponSwap : HudWidget
{
    public const float Width = 42f;
    public const float Height = 41f;
    public const float GapFromWeaponRow = 1f;
    public const float DropFromRowTop = 40f;
    private const int PlainShader = 3;
    private const uint NormalIconFile = 0x263A4;

    private static readonly HudArtwork Artwork = new(64, 64, 12, 12, 54, 53);

    public override void Compose(HudComposer composer, HudState state) =>
        SetLayer(0, composer.Quad(Artwork.Covering(composer.ScreenRectOf(this)), PlainShader, [NormalIconFile], Vector4.Zero));
}
