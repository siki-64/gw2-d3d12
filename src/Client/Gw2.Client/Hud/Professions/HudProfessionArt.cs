using System.Collections.Generic;
using System.Numerics;
using Gw2.Client.Graphics;

namespace Gw2.Client.Hud.Professions;

public sealed class HudProfessionArt(HudComposer composer, GrScreenRect panel, HudState state, HudBar resource)
{
    public const int PlainShader = 3;
    public const int WindowShader = 0xC;
    public const int MaskedColourShader = 0x20;

    public HudComposer Composer => composer;
    public HudState State => state;
    public HudBar Resource => resource;
    public EliteSpecialization? Elite => state.Elite;
    public List<GrUiQuad> Quads { get; } = [];

    public GrScreenRect ScreenOf(HudPanelRect rect) =>
        new(panel.Left + rect.Left, panel.Bottom - rect.Bottom - rect.Height, panel.Left + rect.Left + rect.Width, panel.Bottom - rect.Bottom);

    public void Add(GrScreenRect rect, int shader, uint[] files, Vector4 control, uint argb = 0xFFFFFFFF, GrUiTextureTransform[]? transforms = null) =>
        Quads.Add(composer.Quad(rect, shader, files, control, argb, transforms));

    public void Add(HudPanelRect rect, int shader, uint[] files, Vector4 control, uint argb = 0xFFFFFFFF, GrUiTextureTransform[]? transforms = null) =>
        Add(ScreenOf(rect), shader, files, control, argb, transforms);

    public void Image(HudPanelRect rect, uint file, uint argb = 0xFFFFFFFF) => Add(rect, PlainShader, [file], Vector4.Zero, argb);

    public void Image(GrScreenRect rect, uint file, uint argb = 0xFFFFFFFF) => Add(rect, PlainShader, [file], Vector4.Zero, argb);

    public void Solid(HudPanelRect rect, uint argb) => Add(rect, PlainShader, [], Vector4.Zero, argb);

    public void Fill(HudPanelRect rect, uint flowFile, uint rampFile, float fraction, uint argb = 0xFFFFFFFF) =>
        Fill(ScreenOf(rect), flowFile, rampFile, fraction, argb);

    public void Fill(GrScreenRect rect, uint flowFile, uint rampFile, float fraction, uint argb = 0xFFFFFFFF) =>
        Add(rect, WindowShader, [flowFile, rampFile], new Vector4(1f, 1f - fraction, 0f, 0f), argb);
}
