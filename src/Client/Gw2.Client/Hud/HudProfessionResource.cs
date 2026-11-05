using Gw2.Client.Hud.Professions;

namespace Gw2.Client.Hud;

public sealed class HudProfessionResource(HudProfessionMechanics mechanics) : HudWidget
{
    public override void Compose(HudComposer composer, HudState state)
    {
        if (state.ProfessionResource is not { } resource)
        {
            SetLayer(0, []);
            return;
        }
        var art = new HudProfessionArt(composer, composer.ScreenRectOf(this), state, resource);
        mechanics.For(state).AddArt(art);
        SetLayer(0, art.Quads.ToArray());
    }
}
