using System;
using System.Numerics;
using Gw2.Client.Graphics;

namespace Gw2.Client.Hud;

public sealed class HudHealthOrb : HudWidget
{
    private const int PlainShader = 3;
    private const int MaskedWindowShader = 0x27;
    private const uint BackingFile = 0x26334;
    private const uint FillFile = 0x26336;
    private const uint SurfaceFile = 0x26338;
    private const uint DiscMaskFile = 0x26339;
    private const uint VerticalRampFile = 0x2633A;
    private const float SurfaceHalfThickness = 0.01f;

    private static readonly HudArtwork Artwork = new(128, 128, 13, 13, 115, 115);

    public override void Compose(HudComposer composer, HudState state)
    {
        var ratio = state.OwnHealth?.Fraction ?? 0f;
        var art = Artwork.Covering(composer.ScreenRectOf(this));
        SetLayer(0, composer.Quad(art, PlainShader, [BackingFile], Vector4.Zero));
        SetLayer(1, composer.Quad(art, MaskedWindowShader, [FillFile, DiscMaskFile, VerticalRampFile], new Vector4(ratio, 0f, 0f, 0f)));
        SetLayer(5, ratio > 0f
            ? composer.Quad(art, MaskedWindowShader, [SurfaceFile, DiscMaskFile, VerticalRampFile], new Vector4(Math.Clamp(ratio + SurfaceHalfThickness, 0f, 1f), Math.Clamp(ratio - SurfaceHalfThickness, 0f, 1f), 0f, 0f))
            : null);
        if (state.OwnHealth is { } health)
            composer.Texts.Add(new HudText(composer.ScreenRectOf(this), MathF.Round(health.Value).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
    }
}

public sealed class HudEnduranceArc : HudWidget
{
    public const float Inset = 8f;
    private const int WindowShader = 0xC;
    private const int MaskedWindowShader = 0x27;
    private const uint DarkDiscFile = 0x5782D;
    private const uint AngularSweepFile = 0x3E5B2;
    private const uint FillFile = 0x3E5A2;
    private const uint DiscMaskFile = 0x26339;
    private const float BackingWindowLow = 0.4351f;
    private const float BackingWindowHigh = 0.0649f;
    private const float FillSweepScale = 0.35014f;
    private const float FillSweepStart = 0.07493f;
    private const float FillWindowHigh = 0.0749f;

    private static readonly HudArtwork Artwork = new(128, 128, 14, 12, 115, 113);

    public override void Compose(HudComposer composer, HudState state)
    {
        var fill = state.OwnEndurance?.Fraction ?? 0f;
        var rect = composer.ScreenRectOf(this);
        var art = Artwork.Covering(new GrScreenRect(rect.Left + Inset, rect.Top + Inset, rect.Right - Inset, rect.Bottom - Inset));
        SetLayer(0, composer.Quad(art, WindowShader, [DarkDiscFile, AngularSweepFile], new Vector4(BackingWindowLow, BackingWindowHigh, 0f, 0f),
            transforms: [GrUiTextureTransform.Identity, GrUiTextureTransform.QuarterTurn]));
        SetLayer(1, composer.Quad(art, MaskedWindowShader, [FillFile, DiscMaskFile, AngularSweepFile], new Vector4(fill * FillSweepScale + FillSweepStart, FillWindowHigh, 0f, 0f),
            transforms: [GrUiTextureTransform.Identity, GrUiTextureTransform.Identity, GrUiTextureTransform.QuarterTurn]));
    }
}
