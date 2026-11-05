using System.Collections.Generic;
using System.Globalization;
using Gw2.Client.Net.Game;
using Gw2.Client.State;
using Gw2.Client.Ui;

namespace Gw2.Client.Hud;

public sealed class HudFloaters
{
    public const long LifetimeMilliseconds = 2000;
    private const int MaximumShown = 12;

    private readonly List<(UiHudFloater Floater, long ShownAtMilliseconds)> _shown = [];

    public IReadOnlyList<UiHudFloater> Update(WorldState world, uint ownAgentId, long nowMilliseconds)
    {
        foreach (var hit in world.Hits.TakeHits())
            if (FloaterOf(hit, ownAgentId) is { } floater) Show(floater, nowMilliseconds);
        foreach (var avoidance in world.Avoidances.TakeAvoidances())
            if (FloaterOf(avoidance, ownAgentId) is { } floater) Show(floater, nowMilliseconds);
        _shown.RemoveAll(entry => nowMilliseconds - entry.ShownAtMilliseconds > LifetimeMilliseconds);
        return _shown.ConvertAll(entry => entry.Floater with { AgeFraction = (nowMilliseconds - entry.ShownAtMilliseconds) / (float)LifetimeMilliseconds });
    }

    private void Show(UiHudFloater floater, long nowMilliseconds)
    {
        if (_shown.Count == MaximumShown) _shown.RemoveAt(0);
        _shown.Add((floater, nowMilliseconds));
    }

    public static UiHudFloater? FloaterOf(AgentHit hit, uint ownAgentId)
    {
        var incoming = hit.TargetAgentId == ownAgentId;
        if (!incoming && hit.SourceAgentId != ownAgentId) return null;
        var amount = hit.Damage + hit.BarrierDamage;
        var text = amount.ToString(CultureInfo.InvariantCulture);
        if (hit.IsCritical) text += "!";
        if (hit.IsGlancing) text += " glancing";
        if (hit.IsKillingBlow) text += " kill";
        return new UiHudFloater(text, incoming, amount < 0 ? UiHudFloaterKind.Heal : hit.IsCritical ? UiHudFloaterKind.Critical : UiHudFloaterKind.Damage, 0f);
    }

    public static UiHudFloater? FloaterOf(AgentAvoidance avoidance, uint ownAgentId)
    {
        var incoming = avoidance.AvoidingAgentId == ownAgentId;
        if (!incoming && avoidance.AttackingAgentId != ownAgentId) return null;
        if (avoidance.Kind == AvoidanceKind.MissShownToAvoider && !incoming) return null;
        var text = avoidance.Kind switch
        {
            AvoidanceKind.Block => "Blocked",
            AvoidanceKind.Evade => "Evaded",
            AvoidanceKind.Interrupt => "Interrupted",
            AvoidanceKind.Invulnerable => "Invulnerable",
            _ => "Miss",
        };
        return new UiHudFloater(text, incoming, UiHudFloaterKind.Avoidance, 0f);
    }
}
