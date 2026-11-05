using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Hud.Professions;

public sealed class WarriorHud : HudProfessionMechanic
{
    private const float SlotStep = 44f;
    private const float SlotBottom = -2f;
    private const int BurstSlotCount = 3;
    private const float BurstRowRightStart = 211f;
    private const float BladeswornFirstLeft = -2f;
    private const float ParagonBottom = 23f;
    private const float ParagonFirstLeft = -2f;
    private const float ParagonSecondLeft = 54f;
    private const float ParagonStep = 47f;
    private const int ParagonSlotCount = 4;
    private const int AdrenalineBackingShader = 0x10;
    private const uint AdrenalineBackingFile = 0x26319;
    private const uint AdrenalineFirstBarFile = 0x2631A;
    private const uint AdrenalineFirstRampFile = 0x26320;
    private const int AdrenalineBarCount = 3;
    private static readonly HudPanelRect AdrenalineArt = new(-5f, -25f, 256f, 64f);

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var elite = state.Elite;
        if (elite == EliteSpecialization.Bladesworn)
        {
            var bladesworn = new HudPanelRect?[BurstSlotCount];
            for (var index = 0; index < bladesworn.Length; index++) bladesworn[index] = new HudPanelRect(BladeswornFirstLeft + index * SlotStep, SlotBottom, SlotSize, SlotSize);
            return bladesworn;
        }
        if (elite == EliteSpecialization.Paragon)
        {
            var paragon = new HudPanelRect?[ParagonSlotCount];
            paragon[0] = new HudPanelRect(ParagonFirstLeft, ParagonBottom, SlotSize, SlotSize);
            for (var index = 1; index < paragon.Length; index++) paragon[index] = new HudPanelRect(ParagonSecondLeft + (index - 1) * ParagonStep, ParagonBottom, SlotSize, SlotSize);
            return paragon;
        }
        var burstCount = elite is EliteSpecialization.Berserker or EliteSpecialization.Spellbreaker ? 2 : 1;
        var rects = new HudPanelRect?[BurstSlotCount];
        var left = (BurstSlotCount - burstCount) * SlotStep + BurstRowRightStart;
        for (var index = rects.Length - 1; index >= 0; index--, left -= SlotStep) rects[index] = new HudPanelRect(left, SlotBottom, SlotSize, SlotSize);
        return rects;
    }

    public override void AddArt(HudProfessionArt art)
    {
        var resource = art.Resource;
        art.Add(AdrenalineArt, AdrenalineBackingShader, [AdrenalineBackingFile], new Vector4(0f, 1f, 0f, 0f));
        var perBar = resource.Maximum / AdrenalineBarCount;
        for (var bar = 0; bar < AdrenalineBarCount; bar++)
        {
            var barFraction = perBar > 0f ? Math.Clamp((resource.Value - bar * perBar) / perBar, 0f, 1f) : 0f;
            art.Fill(AdrenalineArt, AdrenalineFirstBarFile + (uint)bar, AdrenalineFirstRampFile + (uint)bar, barFraction);
        }
    }
}
