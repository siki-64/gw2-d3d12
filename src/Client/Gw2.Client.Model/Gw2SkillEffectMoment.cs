using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Model;

public enum Gw2SkillEffectMoment
{
    None,
    Activation,
    Execution,
    Hit,
    CriticalHit,
    Projectile,
    ProjectileImpact,
    Pulse,
}

public enum Gw2SkillEffectPlacement
{
    AttachedToCaster,
    PlacedAtTarget,
}

public static class Gw2SkillEffectMoments
{
    private const uint HitEvents = SkillEffectEvents.Hit | SkillEffectEvents.HitOnTarget;

    public static Gw2SkillEffectMoment MomentOf(uint effectEvent)
    {
        if ((effectEvent & SkillEffectEvents.Activation) != 0) return Gw2SkillEffectMoment.Activation;
        if ((effectEvent & SkillEffectEvents.EffectMoment) != 0) return Gw2SkillEffectMoment.Execution;
        if ((effectEvent & HitEvents) != 0) return Gw2SkillEffectMoment.Hit;
        if ((effectEvent & SkillEffectEvents.CriticalHit) != 0) return Gw2SkillEffectMoment.CriticalHit;
        if ((effectEvent & SkillEffectEvents.Projectile) != 0) return Gw2SkillEffectMoment.Projectile;
        if ((effectEvent & SkillEffectEvents.ProjectileGroundImpact) != 0) return Gw2SkillEffectMoment.ProjectileImpact;
        if ((effectEvent & SkillEffectEvents.Pulse) != 0) return Gw2SkillEffectMoment.Pulse;
        return Gw2SkillEffectMoment.None;
    }

    public static Gw2SkillEffectPlacement PlacementOf(uint effectEvent, uint attachArgument) =>
        SkillEffectEvents.IsPlacedInWorld(effectEvent) || attachArgument == SkillEffectEvents.DetachedAttachArgument
            ? Gw2SkillEffectPlacement.PlacedAtTarget
            : Gw2SkillEffectPlacement.AttachedToCaster;

    public static bool LastsUntilStopped(uint effectEvent) => SkillEffectEvents.IsKeptUntilStopped(effectEvent);
}
