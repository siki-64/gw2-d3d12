using System.Collections.Generic;
using System.Numerics;
using Gw2.Client.Frames;
using Gw2.Client.Graphics;

namespace Gw2.Client.Hud;

public readonly record struct HudArtwork(float Width, float Height, float RegionLeft, float RegionTop, float RegionRight, float RegionBottom)
{
    public GrScreenRect Covering(GrScreenRect rect)
    {
        var scaleX = (rect.Right - rect.Left) / (RegionRight - RegionLeft);
        var scaleY = (rect.Bottom - rect.Top) / (RegionBottom - RegionTop);
        var left = rect.Left - RegionLeft * scaleX;
        var top = rect.Top - RegionTop * scaleY;
        return new GrScreenRect(left, top, left + Width * scaleX, top + Height * scaleY);
    }
}

public enum HudTextStyle : ulong
{
    Body = 0x0000559824573822,
    SkillCharges = 0x06302D981816C9EE,
    SkillKey = SkillCharges,
    Countdown = 0x00000301DC9204A8,
}

public readonly record struct HudText(GrScreenRect Rect, string Text, HudTextStyle Style = HudTextStyle.Body, bool HasShadowHalo = false);

public sealed class HudComposer
{
    public List<HudText> Texts { get; } = [];

    private readonly HudTextures _textures;

    public HudComposer(HudTextures textures, float screenHeight)
    {
        _textures = textures;
        ScreenHeight = screenHeight;
    }

    public float ScreenHeight { get; set; }

    public GrScreenRect ScreenRectOf(Frame frame) =>
        new(frame.ScreenRect.Left, ScreenHeight - frame.ScreenRect.Bottom, frame.ScreenRect.Right, ScreenHeight - frame.ScreenRect.Top);

    public GrUiQuad Quad(GrScreenRect rect, int uiShader, uint[] textureFileIds, Vector4 control, uint argb = 0xFFFFFFFF, GrUiTextureTransform[]? transforms = null)
    {
        var textures = new Crystal.Graphics.Texture2D?[textureFileIds.Length];
        for (var slot = 0; slot < textureFileIds.Length; slot++) textures[slot] = _textures.Of(textureFileIds[slot]);
        return new GrUiQuad(rect, uiShader, textures, transforms ?? [], ColorOf(argb), control);
    }

    public static Vector4 ColorOf(uint argb) =>
        new(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, (argb >> 24) / 255f);
}

public abstract class HudWidget : Frame
{
    public abstract void Compose(HudComposer composer, HudState state);

    protected void SetLayer(int layer, params GrUiQuad?[] quads)
    {
        var present = new List<object>(quads.Length);
        foreach (var quad in quads)
            if (quad is not null) present.Add(quad);
        SetContent(layer, present);
    }
}
