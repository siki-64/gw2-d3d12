using System;
using System.Collections.Generic;

namespace Gw2.Client.Hud.Professions;

public sealed class GuardianHud : HudProfessionMechanic
{
    private const float CompactSlotSize = 34f;
    private const float CompactSlotGap = 4f;
    private const float CompactFirstCentreX = 20f;
    private const float FirstCentreX = 120f;
    private const float SlotCentreY = 18f;
    private const float FourthSlotCentreX = 149f;
    private const int DividerProfessionSlot = 3;
    private static readonly HudPanelRect Divider = new(122f, -2f, 2f, 40f);
    private const uint DividerColor = 0xFFE0E0E0;
    private const uint PageBackingFile = 0x18F972;
    private static readonly HudPanelRect PageBacking = new(5f, -26f, 256f, 64f);
    private static readonly HudPanelRect PageTrack = new(114f, -5f, 140f, 44f);
    private static readonly uint[] FivePageFiles = [0x18F973, 0x18F974, 0x18F975, 0x18F976, 0x18F977, 0x18F978];
    private static readonly uint[] EightPageFiles = [0x18F979, 0x18F97A, 0x18F97B, 0x18F97C, 0x18F97D, 0x18F97E, 0x18F97F, 0x18F980, 0x18F981];
    private const uint LuminaryTimerBackingFile = 0x3829B8;
    private const uint LuminaryTimerFlowFile = 0x3829B9;
    private const uint LuminaryTimerRampFile = 0x3829BA;
    private const uint LuminaryTimerBackingColor = 0x68E0E0E0;
    private const uint LuminaryTimerFlowColor = 0xFFD3F4FA;
    private static readonly HudPanelRect LuminaryTimer = new(132f, 40f, 36f, 7f);

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var compact = state.Elite == EliteSpecialization.Luminary || PageFilesOf(state) is not null;
        var size = compact ? CompactSlotSize : SlotSize;
        var step = size + (compact ? CompactSlotGap : 0f);
        var firstCentreX = compact ? CompactFirstCentreX : FirstCentreX;
        return
        [
            HudPanelRect.Centred(firstCentreX, SlotCentreY, size),
            HudPanelRect.Centred(firstCentreX + step, SlotCentreY, size),
            HudPanelRect.Centred(firstCentreX + 2f * step, SlotCentreY, size),
            HudPanelRect.Centred(FourthSlotCentreX, SlotCentreY, size),
        ];
    }

    public override void AddArt(HudProfessionArt art)
    {
        AddPages(art);
        if (art.State.ProfessionSlotHasSkill(DividerProfessionSlot)) art.Solid(Divider, DividerColor);
        if (art.Elite != EliteSpecialization.Luminary || art.Resource.Fraction <= 0f) return;
        art.Image(LuminaryTimer, LuminaryTimerBackingFile, LuminaryTimerBackingColor);
        art.Fill(LuminaryTimer, LuminaryTimerFlowFile, LuminaryTimerRampFile, art.Resource.Fraction, LuminaryTimerFlowColor);
    }

    private static void AddPages(HudProfessionArt art)
    {
        if (PageFilesOf(art.State) is not { } files) return;
        var pages = Math.Clamp((int)MathF.Round(art.State.ProfessionCounter!.Value.Value), 0, files.Length - 1);
        art.Image(PageBacking, PageBackingFile);
        art.Image(PageTrack, files[pages]);
    }

    private static uint[]? PageFilesOf(HudState state) => state.ProfessionCounter is { } counter ? (int)MathF.Round(counter.Maximum) switch
    {
        5 => FivePageFiles,
        8 => EightPageFiles,
        _ => null,
    } : null;
}
