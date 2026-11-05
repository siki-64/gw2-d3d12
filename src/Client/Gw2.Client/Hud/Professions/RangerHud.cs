using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Hud.Professions;

public sealed class RangerHud : HudProfessionMechanic
{
    private const float CentreWidgetCentreFromPanelLeft = PanelWidth + 13f + 101f / 2f;
    private const float CentreWidgetBottomFromPanelBottom = 13f - 98f;
    private const float CentreWidgetSize = 101f;
    private const float PetWidgetDropFromCentreWidgetTop = 35f;
    private const float PetWidgetSize = 128f;
    private const float PetWidgetSpacer = 46f;
    private const float PetSlotSize = 30f;
    private const int SlotCount = 5;
    private const int MechanicSlotCount = 4;
    private const int PetSlotIndex = 4;
    private const float SoulbeastFirstLeft = 68f;
    private const float SoulbeastStep = 50f;
    private const float SoulbeastBottom = 5f;
    private const float CoreFirstLeft = -1f;
    private const float CoreStep = 33f;
    private const float CoreBottom = 18f;
    private const float CoreSlotSize = 30f;
    private const int PetMeterFlowShader = 0x27;
    private const uint PetMeterBackingFile = 0x102DF9;
    private const uint PetMeterFlowFile = 0x114076;
    private const uint PetMeterFlowMaskFile = 0x102DFA;
    private const uint PetMeterFlowRampFile = 0xF2965;
    private const uint PetMeterFrameFile = 0x102DFD;
    private const uint PetMeterFullFrameFile = 0x102DFE;
    private const uint PetPipBackingFile = 0x3829BD;
    private const uint PetPipFlowFile = 0x36F6E7;
    private const uint PetPipFlowMaskFile = 0x3829BE;
    private const uint PetPipEmptyFile = 0x3829BC;
    private const uint PetPipFilledFile = 0x3829BB;
    private const uint PetPipColor = 0xFFBCCF16;
    private const float PetPipSize = 25.6f;
    private const float PetPipRadius = 83.2f;
    private const float PetPipCentreX = 50.25f;
    private const float PetPipCentreY = -14.07f;
    private const float PetPipLeftStartAngle = 0.94247776f;
    private const float PetPipRightStartAngle = -0.31415927f;
    private const float PetPipArc = 1.2566371f;

    private static readonly HudPanelRect PetWidget = new(
        CentreWidgetCentreFromPanelLeft - PetWidgetSize / 2f,
        CentreWidgetBottomFromPanelBottom + CentreWidgetSize - PetWidgetDropFromCentreWidgetTop,
        PetWidgetSize,
        PetWidgetSize);

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var soulbeast = state.Elite == EliteSpecialization.Soulbeast;
        var rects = new HudPanelRect?[SlotCount];
        rects[PetSlotIndex] = new HudPanelRect(PetWidget.Left + (PetWidget.Width - PetSlotSize) / 2f, PetWidget.Bottom + PetWidget.Height - PetWidgetSpacer - PetSlotSize, PetSlotSize, PetSlotSize);
        for (var index = 0; index < MechanicSlotCount; index++)
            rects[index] = soulbeast
                ? new HudPanelRect(SoulbeastFirstLeft + index * SoulbeastStep, SoulbeastBottom, SlotSize, SlotSize)
                : new HudPanelRect(CoreFirstLeft + index * CoreStep, CoreBottom, CoreSlotSize, CoreSlotSize);
        return rects;
    }

    public override void AddArt(HudProfessionArt art)
    {
        if (art.Elite == EliteSpecialization.RangerWithPetPips) AddPetPips(art);
        else AddPetMeter(art);
    }

    private static void AddPetMeter(HudProfessionArt art)
    {
        var fraction = art.Resource.Fraction;
        art.Image(PetWidget, PetMeterBackingFile);
        art.Add(PetWidget, PetMeterFlowShader, [PetMeterFlowFile, PetMeterFlowMaskFile, PetMeterFlowRampFile], new Vector4(1f, 1f - fraction, 0f, 0f));
        art.Image(PetWidget, fraction >= 1f ? PetMeterFullFrameFile : PetMeterFrameFile);
    }

    private static void AddPetPips(HudProfessionArt art)
    {
        art.Image(PetWidget, PetPipBackingFile);
        art.Add(PetWidget, HudProfessionArt.MaskedColourShader, [PetPipFlowFile, PetPipFlowMaskFile], Vector4.Zero, PetPipColor);
        if (art.State.ProfessionCounter is not { } stacks) return;
        var count = (int)MathF.Round(stacks.Maximum);
        var filled = (int)MathF.Round(stacks.Value);
        var half = count >> 1;
        var step = count > 0 ? PetPipArc / (count + (count & 1)) : 0f;
        for (var pip = 0; pip < count; pip++)
        {
            var angle = pip >= half ? PetPipRightStartAngle - (pip - half) * step : PetPipLeftStartAngle - (pip + 1) * step;
            var left = PetPipCentreX - MathF.Sin(angle) * PetPipRadius;
            var bottom = MathF.Cos(angle) * PetPipRadius + PetPipCentreY;
            art.Image(new HudPanelRect(PetWidget.Left + left, PetWidget.Bottom + bottom, PetPipSize, PetPipSize), pip < filled ? PetPipFilledFile : PetPipEmptyFile, PetPipColor);
        }
    }
}
