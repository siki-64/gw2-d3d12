using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Client.Graphics;

namespace Gw2.Client.Hud.Professions;

public readonly record struct HolosmithHeatThresholds(float OverheatFraction, float PenaltyHeat, float BonusHeat);

public sealed class EngineerHud(HolosmithHeatThresholds heatThresholds) : HudProfessionMechanic
{
    private const float SlotStep = 45f;
    private const float FirstCentreX = 50f;
    private const float SlotCentreY = 18f;
    private const float MechToolbeltShiftX = 3f;
    private const float MechToolbeltShiftY = 7f;
    private const float MechToolbeltSize = 52f;
    private const float ToolbeltGap = 5f;
    private const uint MechBackingFile = 0x264AE;
    private const uint MechHealthFile = 0x264AF;
    private const uint MechBarrierFile = 0x19F7D9;
    private const uint MechRadialMaskFile = 0x264B0;
    private const int RadialWindowShader = 0x1C;
    private const uint MechBarrierColor = 0x80FAE29A;
    private static readonly HudPanelRect MechOverlay = new(-29f, -9f, 193f, 36f);
    private const uint HeatBackingFile = 0x18F96E;
    private const uint HeatFillFile = 0x18F96F;
    private const uint HeatFillEndFile = 0x18F971;
    private const uint HeatOverheatZoneFile = 0x18F970;
    private const float HeatFillUScaleAtOrBelowPenalty = 0.666f;
    private const uint HeatTickColor = 0xFF000000;
    private const uint HeatSeparatorColor = 0xA0000000;
    private const float HeatTickHalfWidth = 1f;
    private const float HeatSeparatorHalfWidth = 1f;
    private const float HeatSeparatorHalfHeight = 20f;
    private const uint HeatFillColor = 0xC8FFFFFF;
    private const float HeatFillLeft = -1f;
    private const float HeatFillBottom = -4f;
    private const float HeatFillSpan = 262f;
    private const float HeatFillHeight = 10f;
    private const float HeatFillEndSize = 12f;
    private static readonly HudPanelRect HeatBackingArt = new(-2f, -5f, 264f, 12f);

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state) =>
        SlotRects(state.Elite == EliteSpecialization.Mechanist);

    private static HudPanelRect?[] SlotRects(bool mechanist)
    {
        var toolbeltCentreX = FirstCentreX + 3f * SlotStep + (mechanist ? MechToolbeltShiftX : 0f);
        var toolbeltCentreY = SlotCentreY + (mechanist ? MechToolbeltShiftY : 0f);
        var toolbeltSize = mechanist ? MechToolbeltSize : SlotSize;
        return
        [
            HudPanelRect.Centred(FirstCentreX, SlotCentreY, SlotSize),
            HudPanelRect.Centred(FirstCentreX + SlotStep, SlotCentreY, SlotSize),
            HudPanelRect.Centred(FirstCentreX + 2f * SlotStep, SlotCentreY, SlotSize),
            HudPanelRect.Centred(toolbeltCentreX, toolbeltCentreY, toolbeltSize),
            HudPanelRect.Centred(toolbeltCentreX + toolbeltSize + ToolbeltGap, toolbeltCentreY, SlotSize),
        ];
    }

    public override void AddArt(HudProfessionArt art)
    {
        AddHeat(art);
        if (art.Elite == EliteSpecialization.Mechanist && art.State.PetHealth is { } mechHealth) AddMechHealth(art, mechHealth.Fraction, art.State.PetBarrierFraction);
    }

    private void AddHeat(HudProfessionArt art)
    {
        var resource = art.Resource;
        art.Image(HeatBackingArt, HeatBackingFile);
        var zoneLeft = HeatFillLeft + heatThresholds.OverheatFraction * HeatFillSpan;
        var zoneRect = new HudPanelRect(zoneLeft, HeatFillBottom, HeatFillLeft + HeatFillSpan - zoneLeft, HeatFillHeight);
        art.Add(zoneRect, HudProfessionArt.PlainShader, [HeatOverheatZoneFile], Vector4.Zero, transforms: [BackingWindowOf(zoneRect, 1f)]);
        AddHeatTicks(art);
        AddHeatSeparator(art);
        var fillWidth = resource.Fraction * HeatFillSpan;
        if (fillWidth <= 0f) return;
        var fillRect = new HudPanelRect(HeatFillLeft, HeatFillBottom, fillWidth, HeatFillHeight);
        var uScale = resource.Maximum <= heatThresholds.PenaltyHeat ? HeatFillUScaleAtOrBelowPenalty : 1f;
        art.Add(fillRect, HudProfessionArt.PlainShader, [HeatFillFile], Vector4.Zero, HeatFillColor, [BackingWindowOf(fillRect, uScale)]);
        var endLeft = MathF.Max(HeatFillLeft, HeatFillLeft + fillWidth - HeatFillEndSize);
        art.Image(new HudPanelRect(endLeft, HeatFillBottom, HeatFillEndSize, HeatFillHeight), HeatFillEndFile);
    }

    private void AddHeatTicks(HudProfessionArt art)
    {
        var maximum = art.Resource.Maximum;
        if (maximum <= 0f) return;
        foreach (var heat in new[] { heatThresholds.BonusHeat, heatThresholds.PenaltyHeat })
        {
            var ratio = heat / maximum;
            if (ratio <= 0.000001f || ratio >= 0.999999f) continue;
            var centre = ratio * HeatFillSpan + HeatFillLeft;
            var left = MathF.Max(HeatFillLeft, centre - HeatTickHalfWidth);
            var right = MathF.Min(HeatFillLeft + HeatFillSpan, centre + HeatTickHalfWidth);
            art.Solid(new HudPanelRect(left, HeatFillBottom, right - left, HeatFillHeight), HeatTickColor);
        }
    }

    private static void AddHeatSeparator(HudProfessionArt art)
    {
        var slots = SlotRects(mechanist: false);
        if (slots[0] is not { } first || slots[1] is not { } second) return;
        var left = MathF.Min(first.Left, second.Left);
        var right = MathF.Max(first.Left + first.Width, second.Left + second.Width);
        var bottom = MathF.Min(first.Bottom, second.Bottom);
        var top = MathF.Max(first.Bottom + first.Height, second.Bottom + second.Height);
        var centreX = (left + right) / 2f;
        var centreY = (bottom + top) / 2f;
        art.Solid(new HudPanelRect(centreX - HeatSeparatorHalfWidth, centreY - HeatSeparatorHalfHeight, 2f * HeatSeparatorHalfWidth, 2f * HeatSeparatorHalfHeight), HeatSeparatorColor);
    }

    private static GrUiTextureTransform BackingWindowOf(HudPanelRect rect, float uScale)
    {
        var backing = HeatBackingArt;
        var uOffset = (rect.Left - backing.Left) / backing.Width;
        var vOffset = (backing.Bottom + backing.Height - rect.Bottom - rect.Height) / backing.Height;
        return new GrUiTextureTransform(new Vector3(rect.Width / backing.Width * uScale, 0f, uOffset * uScale), new Vector3(0f, rect.Height / backing.Height, vOffset));
    }

    private static void AddMechHealth(HudProfessionArt art, float health, float barrier)
    {
        art.Image(MechOverlay, MechBackingFile);
        art.Add(MechOverlay, RadialWindowShader, [MechHealthFile, MechRadialMaskFile], new Vector4(0f, health, 0f, 0f));
        var total = health + barrier;
        var overflow = MathF.Max(total - 1f, 0f);
        var barrierEnd = total >= 1f ? 1f : total;
        art.Add(MechOverlay, RadialWindowShader, [MechBarrierFile, MechRadialMaskFile], new Vector4(health - overflow, barrierEnd, 0f, 0f), MechBarrierColor);
    }
}
