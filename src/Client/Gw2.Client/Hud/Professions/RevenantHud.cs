using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Hud.Professions;

public sealed class RevenantHud : HudProfessionMechanic
{
    private const float LegendSwapSize = 42f;
    private const float SkillSize = 30f;
    private const float SkillRowCentreX = 171f;
    private const float SkillRowBottom = 36f;
    private const int SlotCount = 4;
    private const uint EnergyBackingFile = 0xEBC2D;
    private const uint EnergyFillFile = 0xEBC34;
    private const uint EnergyBarMaskFile = 0xEBC2E;
    private const float EnergyLeft = 88f;
    private const float EnergyBottom = -13f;
    private const float EnergyWidth = 256f;
    private const float EnergyHeight = 32f;
    private const float EnergyFillWidth = 168f;

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var rects = new HudPanelRect?[SlotCount];
        rects[0] = new HudPanelRect(0f, 0f, LegendSwapSize, LegendSwapSize);
        var presentCount = 0;
        for (var index = 1; index < rects.Length; index++)
            if (state.ProfessionSlotHasSkill(index)) presentCount++;
        var left = SkillRowCentreX - presentCount * SkillSize / 2f;
        for (var index = 1; index < rects.Length; index++)
        {
            if (!state.ProfessionSlotHasSkill(index)) continue;
            rects[index] = new HudPanelRect(left, SkillRowBottom, SkillSize, SkillSize);
            left += SkillSize;
        }
        return rects;
    }

    public override void AddArt(HudProfessionArt art)
    {
        art.Image(new HudPanelRect(EnergyLeft, EnergyBottom, EnergyWidth, EnergyHeight), EnergyBackingFile);
        if (art.Resource.Fraction <= 0f) return;
        art.Add(new HudPanelRect(EnergyLeft, EnergyBottom, EnergyFillWidth * art.Resource.Fraction, EnergyHeight), HudProfessionArt.MaskedColourShader, [EnergyFillFile, EnergyBarMaskFile], Vector4.Zero);
    }
}
