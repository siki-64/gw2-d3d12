using System;
using System.Collections.Generic;

namespace Gw2.Client.Hud.Professions;

public sealed class NecromancerHud : HudProfessionMechanic
{
    private readonly record struct LifeForceArt(HudPanelRect Rect, uint BackingFile, uint FillFile, uint RampFile, uint FillColor);

    private const float SecondarySlotSize = 32f;
    private const float SecondarySlotBottom = 28f;
    private const float SecondaryFirstLeft = 75f;
    private const float SecondaryStep = 36f;
    private const int SlotCount = 5;
    private const float ShroudWithSecondaryCentreX = 18f;
    private const float ShroudWithSecondaryCentreY = 44f;
    private const float ShroudAloneCentreX = 230f;
    private const float ShroudAloneCentreY = 18f;
    private const uint CoreFillColor = 0x80FFFFFF;
    private const uint EliteFillColor = 0xBEFFFFFF;
    private static readonly LifeForceArt CoreLifeForce = new(new HudPanelRect(-5f, -5f, 246f, 75f), 0x26313, 0x26314, 0x26315, CoreFillColor);
    private static readonly LifeForceArt ScourgeLifeForce = new(new HudPanelRect(-2f, -3f, 264f, 16f), 0x18F966, 0x18F967, 0x18F969, EliteFillColor);
    private static readonly LifeForceArt HarbingerLifeForce = new(new HudPanelRect(-5f, 5f, 227f, 16f), 0x25D73E, 0x25D73F, 0x25D741, EliteFillColor);
    private static readonly HudPanelRect ScourgeShadeIcon = new(40f, 22f, 36f, 44f);
    private const uint ScourgeSingleShadeFullFile = 0x18F986;
    private const uint ScourgeSingleShadeEmptyFile = 0x18F984;
    private static readonly uint[] ScourgeThreeShadeFilesByCount = [0x18F983, 0x18F985, 0x18F987, 0x18F988];

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var rects = new HudPanelRect?[SlotCount];
        var anySecondarySkill = false;
        for (var index = 1; index < rects.Length; index++)
        {
            anySecondarySkill |= state.ProfessionSlotHasSkill(index);
            rects[index] = new HudPanelRect(SecondaryFirstLeft + (index - 1) * SecondaryStep, SecondarySlotBottom, SecondarySlotSize, SecondarySlotSize);
        }
        rects[0] = anySecondarySkill
            ? HudPanelRect.Centred(ShroudWithSecondaryCentreX, ShroudWithSecondaryCentreY, SlotSize)
            : HudPanelRect.Centred(ShroudAloneCentreX, ShroudAloneCentreY, SlotSize);
        return rects;
    }

    public override void AddArt(HudProfessionArt art)
    {
        var lifeForce = art.Elite switch
        {
            EliteSpecialization.Scourge => ScourgeLifeForce,
            EliteSpecialization.Harbinger => HarbingerLifeForce,
            _ => CoreLifeForce,
        };
        art.Image(lifeForce.Rect, lifeForce.BackingFile);
        art.Fill(lifeForce.Rect, lifeForce.FillFile, lifeForce.RampFile, art.Resource.Fraction, lifeForce.FillColor);
        if (art.Elite == EliteSpecialization.Scourge && art.State.ProfessionCounter is { } shades && ScourgeShadeFileOf(shades) is { } shadeFile)
            art.Image(ScourgeShadeIcon, shadeFile);
    }

    private static uint? ScourgeShadeFileOf(HudBar shades)
    {
        var current = (int)MathF.Round(shades.Value);
        return (int)MathF.Round(shades.Maximum) switch
        {
            1 => current >= 1 ? ScourgeSingleShadeFullFile : ScourgeSingleShadeEmptyFile,
            3 => ScourgeThreeShadeFilesByCount[Math.Clamp(current, 0, 3)],
            _ => null,
        };
    }
}
