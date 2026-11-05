using System;
using System.Collections.Generic;

namespace Gw2.Client.Hud.Professions;

public sealed class MesmerHud : HudProfessionMechanic
{
    private const float SlotBottom = -2f;
    private static readonly float[] SlotLefts = [25f, 70f, 115f, 160f, 215f];
    private const int DividerProfessionSlot = 4;
    private static readonly HudPanelRect Divider = new(206.5f, -2f, 2f, 40f);
    private const uint DividerColor = 0xFF000000;
    private const uint CloneEmptyFile = 0x2630D;
    private const uint CloneFilledFile = 0x2630E;
    private const float ClonesLeft = 63f;
    private const float ClonesBottom = 40f;
    private const float ClonesTotalWidth = 100f;
    private static readonly HudArtwork CloneArtwork = new(64, 64, 4, 4, 60, 60);
    private const uint TroubadourBackingFile = 0x3829AD;
    private static readonly HudPanelRect TroubadourBacking = new(71f, 51f, 122f, 39f);
    private static readonly uint[] TroubadourEmptyFiles = [0x3829AF, 0x3829B1, 0x3829B3];
    private static readonly uint[] TroubadourFilledFiles = [0x3829AE, 0x3829B0, 0x3829B2];
    private static readonly (float Left, float Bottom)[] TroubadourIconCorners = [(84f, 52f), (117f, 56f), (149f, 55f)];
    private const float TroubadourIconSize = 32f;
    private const uint TroubadourEmptyColor = 0xFF202020;
    private const uint TroubadourFilledColor = 0xFFC765E8;

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var rects = new HudPanelRect?[SlotLefts.Length];
        for (var index = 0; index < rects.Length; index++) rects[index] = new HudPanelRect(SlotLefts[index], SlotBottom, SlotSize, SlotSize);
        return rects;
    }

    public override void AddArt(HudProfessionArt art)
    {
        if (art.State.ProfessionSlotHasSkill(DividerProfessionSlot)) art.Solid(Divider, DividerColor);
        if (art.Elite == EliteSpecialization.Troubadour) AddTroubadour(art);
        else AddClones(art);
    }

    private static void AddTroubadour(HudProfessionArt art)
    {
        art.Image(TroubadourBacking, TroubadourBackingFile);
        var filled = Math.Clamp((int)MathF.Round(art.Resource.Value), 0, TroubadourIconCorners.Length);
        for (var icon = 0; icon < TroubadourIconCorners.Length; icon++)
        {
            var (left, bottom) = TroubadourIconCorners[icon];
            var rect = new HudPanelRect(left, bottom, TroubadourIconSize, TroubadourIconSize);
            art.Image(rect, TroubadourEmptyFiles[icon], TroubadourEmptyColor);
            if (icon < filled) art.Image(rect, TroubadourFilledFiles[icon], TroubadourFilledColor);
        }
    }

    private static void AddClones(HudProfessionArt art)
    {
        var maximum = (int)MathF.Round(art.Resource.Maximum);
        var current = (int)MathF.Round(art.Resource.Value);
        if (maximum <= 0) return;
        var width = ClonesTotalWidth / maximum;
        for (var clone = 0; clone < maximum; clone++)
        {
            var rect = CloneArtwork.Covering(art.ScreenOf(new HudPanelRect(ClonesLeft + clone * width, ClonesBottom, width, width)));
            art.Image(rect, clone < current ? CloneFilledFile : CloneEmptyFile);
        }
    }
}
