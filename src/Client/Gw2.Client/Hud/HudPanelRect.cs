namespace Gw2.Client.Hud;

public readonly record struct HudPanelRect(float Left, float Bottom, float Width, float Height)
{
    public static HudPanelRect Centred(float centreX, float centreY, float size) => new(centreX - size / 2f, centreY - size / 2f, size, size);
}
