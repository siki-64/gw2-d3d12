using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gw2.Client.Hud.Professions;

public sealed class ThiefHud : HudProfessionMechanic
{
    private const float SlotWidth = 38f;
    private const float SlotHeight = 42f;
    private const float SlotBottom = -4f;
    private const float SlotStep = 41f;
    private const float AntiquaryFirstLeft = -12f;
    private const float AntiquaryFirstStep = 50f;
    private const int SlotCount = 4;
    private const uint InitiativeEmptyFile = 0x26317;
    private const uint InitiativeFilledFile = 0x26318;
    private const float InitiativePipSize = 26f;
    private const float InitiativeFirstX = 80f;
    private const float InitiativeLastRight = 259f;
    private const float InitiativeUpperBottom = 16f;
    private const float InitiativeLowerBottom = 3f;
    private const float SpecterInitiativeLastRight = 260f;
    private const float SpecterInitiativeUpperBottom = 23f;
    private const float SpecterInitiativeLowerBottom = 10f;
    private static readonly HudPanelRect AntiquaryDivider = new(31f, -4f, 2f, 42f);
    private const uint AntiquaryDividerColor = 0xFF000000;
    private const float AntiquaryInitiativeFirstX = 162f;
    private const float AntiquaryInitiativeLastRight = 268f;
    private static readonly float[] AntiquaryInitiativeRowBottoms = [-6f, 8f, 22f];
    private const uint SpecterBarBackingFile = 0x25A9DB;
    private const uint SpecterBarFlowFile = 0x25A9DC;
    private const uint SpecterBarRampFile = 0x25A9DD;
    private const uint SpecterBarFlowColor = 0xFFFF00CC;
    private static readonly HudPanelRect SpecterBar = new(0f, 0f, 178f, 12f);

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var antiquary = state.Elite == EliteSpecialization.Antiquary;
        var rects = new HudPanelRect?[SlotCount];
        var left = antiquary ? AntiquaryFirstLeft : 0f;
        for (var index = 0; index < rects.Length; index++)
        {
            rects[index] = new HudPanelRect(left, SlotBottom, SlotWidth, SlotHeight);
            left += index == 0 && antiquary ? AntiquaryFirstStep : SlotStep;
        }
        return rects;
    }

    public override void AddArt(HudProfessionArt art)
    {
        AddInitiative(art);
        if (art.Elite == EliteSpecialization.Specter) AddSpecterBar(art);
    }

    private static void AddSpecterBar(HudProfessionArt art)
    {
        art.Image(SpecterBar, SpecterBarBackingFile);
        art.Fill(SpecterBar, SpecterBarFlowFile, SpecterBarRampFile, art.State.ProfessionSecondaryMeter?.Fraction ?? 0f, SpecterBarFlowColor);
        if (art.State.ProfessionSecondaryMeter is { } shroud)
            art.Composer.Texts.Add(new HudText(art.ScreenOf(SpecterBar), MathF.Round(shroud.Value).ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddInitiative(HudProfessionArt art)
    {
        var maximum = (int)MathF.Round(art.Resource.Maximum);
        var displayed = (int)MathF.Round(art.Resource.Value);
        if (maximum <= 0) return;
        if (art.Elite == EliteSpecialization.Antiquary)
        {
            AddAntiquaryInitiative(art, maximum, displayed);
            return;
        }
        var specter = art.Elite == EliteSpecialization.Specter;
        var lastRight = specter ? SpecterInitiativeLastRight : InitiativeLastRight;
        var upperBottom = specter ? SpecterInitiativeUpperBottom : InitiativeUpperBottom;
        var lowerBottom = specter ? SpecterInitiativeLowerBottom : InitiativeLowerBottom;
        var step = maximum > 1 ? (lastRight - InitiativeFirstX - InitiativePipSize) / (maximum - 1) : 0f;
        for (var pip = 0; pip < maximum; pip++)
            AddInitiativePip(art, InitiativeFirstX + pip * step, pip % 2 == 0 ? upperBottom : lowerBottom, pip < displayed);
    }

    private static void AddAntiquaryInitiative(HudProfessionArt art, int maximum, int displayed)
    {
        art.Solid(AntiquaryDivider, AntiquaryDividerColor);
        var perRow = (maximum + 2) / 3;
        var step = (AntiquaryInitiativeLastRight - AntiquaryInitiativeFirstX - InitiativePipSize) / Math.Max(1, perRow - 1);
        for (var point = 0; point < maximum; point++)
        {
            var row = (point + 2) % 3;
            var column = point / 3;
            var left = AntiquaryInitiativeFirstX + column * step + (row == 1 ? step / 2f : 0f);
            AddInitiativePip(art, left, AntiquaryInitiativeRowBottoms[row], point < displayed);
        }
    }

    private static void AddInitiativePip(HudProfessionArt art, float left, float bottom, bool filled) =>
        art.Image(new HudPanelRect(left, bottom, InitiativePipSize, InitiativePipSize), filled ? InitiativeFilledFile : InitiativeEmptyFile);
}
