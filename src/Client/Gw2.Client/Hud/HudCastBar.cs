using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Client.Graphics;
using Gw2.Client.Model;

namespace Gw2.Client.Hud;

public readonly record struct HudCastBarState(IReadOnlyList<Gw2CastSegmentView> Segments, float Opacity);

public sealed class HudCastBar : HudWidget
{
    public const float Width = 200f;
    public const float Height = 50f;
    private const int SegmentShader = 0x2E;
    private const int GlowShader = 0x2F;
    private const uint BackingFile = 0x265FF;
    private const uint FillFile = 0x26602;
    private const uint CompletedFillFile = 0x26601;
    private const uint FlashFile = 0x26600;
    private const uint EdgeFile = 0x26604;
    private const uint GlowFile = 0x26603;
    private const float BodyMaskHeights = 4f;

    private readonly record struct SegmentLook(uint FillFile, uint BodyMaskFile, uint EndMaskFile, bool KeepsFillWhenInterrupted);

    private static SegmentLook LookOf(int style) => style switch
    {
        0 => new(CompletedFillFile, 0x26607, 0x26608, true),
        2 => new(FillFile, 0x26607, 0x26608, false),
        3 => new(FillFile, 0x26609, 0x2660A, false),
        4 => new(FillFile, 0x2660B, 0x2660C, false),
        _ => new(FillFile, 0x26605, 0x26606, false),
    };

    public override void Compose(HudComposer composer, HudState state)
    {
        if (state.CastBar is not { Segments.Count: > 0 } bar)
        {
            SetLayer(0);
            return;
        }
        var whole = composer.ScreenRectOf(this);
        var quads = new List<GrUiQuad?>();
        foreach (var segment in bar.Segments) AddSegment(quads, composer, whole, segment, bar.Opacity);
        SetLayer(0, [.. quads]);
    }

    private static void AddSegment(List<GrUiQuad?> quads, HudComposer composer, GrScreenRect whole, Gw2CastSegmentView segment, float opacity)
    {
        var wholeWidth = whole.Right - whole.Left;
        var rect = whole with { Left = whole.Left + wholeWidth * segment.LayoutStart, Right = whole.Left + wholeWidth * segment.LayoutEnd };
        var look = LookOf(segment.Style);
        var painter = new SegmentPainter(composer, rect, look, segment.LayoutStart);
        var seconds = segment.ModeSeconds;
        quads.Add(painter.Fill(BackingFile, SegmentShader, 1f, opacity * opacity));
        switch (segment.Mode)
        {
            case Gw2CastSegmentMode.Casting:
                quads.Add(painter.Fill(look.FillFile, SegmentShader, segment.Fill, opacity));
                quads.Add(painter.Edge(EdgeFile, SegmentShader, segment.Fill, opacity));
                quads.Add(painter.Edge(GlowFile, GlowShader, segment.Fill, opacity));
                break;
            case Gw2CastSegmentMode.Completed when seconds < Gw2CastProgress.CompletionFlashSeconds:
                var rise = seconds / (float)Gw2CastProgress.CompletionFlashSeconds;
                quads.Add(painter.Fill(look.FillFile, SegmentShader, 1f, opacity));
                quads.Add(painter.Fill(CompletedFillFile, SegmentShader, 1f, opacity * rise));
                quads.Add(painter.Fill(FlashFile, SegmentShader, 1f, opacity * rise));
                quads.Add(painter.Fill(FlashFile, GlowShader, 1f, opacity * rise));
                quads.Add(painter.Edge(EdgeFile, SegmentShader, 1f, opacity * (1f - rise)));
                quads.Add(painter.Edge(GlowFile, GlowShader, 1f, opacity * (1f - rise)));
                break;
            case Gw2CastSegmentMode.Completed:
                var fade = Math.Clamp((seconds - (float)Gw2CastProgress.CompletionFlashSeconds) / (float)Gw2CastProgress.CompletionFadeSeconds, 0f, 1f);
                var remaining = opacity * (1f - fade) * (1f - fade);
                quads.Add(painter.Fill(look.FillFile, SegmentShader, 1f, remaining));
                quads.Add(painter.Fill(CompletedFillFile, SegmentShader, 1f, opacity));
                quads.Add(painter.Fill(FlashFile, SegmentShader, 1f, remaining));
                quads.Add(painter.Fill(FlashFile, GlowShader, 1f, remaining));
                break;
            case Gw2CastSegmentMode.Interrupted:
                var fillFade = look.KeepsFillWhenInterrupted ? opacity : opacity * Math.Clamp(1f - seconds / (float)Gw2CastProgress.InterruptionFillFadeSeconds, 0f, 1f);
                quads.Add(painter.Fill(look.FillFile, SegmentShader, segment.Fill, fillFade));
                quads.Add(painter.Edge(EdgeFile, SegmentShader, segment.Fill, fillFade));
                quads.Add(painter.Edge(GlowFile, GlowShader, segment.Fill, opacity * Math.Clamp(1f - seconds / (float)Gw2CastProgress.InterruptionGlowFadeSeconds, 0f, 1f)));
                break;
        }
    }

    private readonly record struct SegmentPainter(HudComposer Composer, GrScreenRect Rect, SegmentLook Look, float ColourStart)
    {
        public GrUiQuad Fill(uint colourFile, int shader, float fill, float alpha)
        {
            var height = Rect.Bottom - Rect.Top;
            var edge = EdgeOf(fill);
            var quad = Rect with { Right = edge + height / 2f };
            var width = quad.Right - quad.Left;
            var colour = Horizontal(width / (BodyMaskHeights * height), ColourStart);
            var body = Horizontal(width / (BodyMaskHeights * height), 0f);
            var end = Horizontal(width / height, (Rect.Left - edge + height / 2f) / height);
            return Composer.Quad(quad, shader, [colourFile, Look.BodyMaskFile, Look.EndMaskFile], Vector4.Zero, WhiteWithAlpha(alpha), [colour, body, end]);
        }

        public GrUiQuad Edge(uint colourFile, int shader, float fill, float alpha)
        {
            var height = Rect.Bottom - Rect.Top;
            var edge = EdgeOf(fill);
            var quad = Rect with { Left = edge - height / 2f, Right = Rect.Right + height / 2f };
            var width = quad.Right - quad.Left;
            var colour = Horizontal(width / height, 0f);
            var body = Horizontal(width / (BodyMaskHeights * height), (quad.Left - Rect.Left) / (BodyMaskHeights * height));
            var end = Horizontal(width / height, (edge - Rect.Right) / height);
            return Composer.Quad(quad, shader, [colourFile, Look.BodyMaskFile, Look.EndMaskFile], Vector4.Zero, WhiteWithAlpha(alpha), [colour, body, end]);
        }

        private float EdgeOf(float fill) => Rect.Left + (Rect.Right - Rect.Left) * fill;
    }

    private static GrUiTextureTransform Horizontal(float scale, float offset) =>
        new(new Vector3(scale, 0f, offset), new Vector3(0f, 1f, 0f));

    private static uint WhiteWithAlpha(float alpha) => ((uint)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 255f) << 24) | 0x00FFFFFF;
}
