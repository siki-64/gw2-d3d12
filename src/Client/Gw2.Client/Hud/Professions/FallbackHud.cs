using System.Collections.Generic;

namespace Gw2.Client.Hud.Professions;

public sealed class FallbackHud : HudProfessionMechanic
{
    private const float SlotStep = 44f;
    private const float SlotBottom = -2f;
    private const int SlotCount = 5;

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var rects = new HudPanelRect?[SlotCount];
        for (var index = 0; index < rects.Length; index++) rects[index] = new HudPanelRect(index * SlotStep, SlotBottom, SlotSize, SlotSize);
        return rects;
    }
}
