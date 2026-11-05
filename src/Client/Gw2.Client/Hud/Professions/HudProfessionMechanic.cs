using System.Collections.Generic;

namespace Gw2.Client.Hud.Professions;

public abstract class HudProfessionMechanic
{
    public const float PanelWidth = 256f;
    public const float PanelHeight = 64f;
    protected const float SlotSize = 40f;

    public abstract IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state);

    public virtual void AddArt(HudProfessionArt art)
    {
    }
}
