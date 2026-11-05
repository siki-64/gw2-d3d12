namespace Gw2.Formats;

public static class SkillEffectEvents
{
    public const uint BuffActive = 0x1;
    public const uint GadgetBuffActive = 0x2;
    public const uint Pulse = 0x4;
    public const uint EffectMomentAttached = 0x8;
    public const uint EffectMomentAttachedAlternate = 0x10;
    public const uint EffectMomentPlaced = 0x20;
    public const uint EffectMomentPlacedAlternate = 0x40;
    public const uint EffectMomentPlacedGround = 0x80;
    public const uint EffectMoment = EffectMomentAttached | EffectMomentAttachedAlternate | EffectMomentPlaced | EffectMomentPlacedAlternate | EffectMomentPlacedGround;
    public const uint Hit = 0x100;
    public const uint CriticalHit = 0x200;
    public const uint HitOnTarget = 0x400;
    public const uint Projectile = 0x2000;
    public const uint ProjectileGroundImpact = 0x4000;
    public const uint WeaponTrail = 0x8000;
    public const uint WeaponTrailAlternate = 0x10000;
    public const uint ActivationAttached = 0x80000;
    public const uint ActivationPlaced = 0x100000;
    public const uint Activation = ActivationAttached | ActivationPlaced;
    public const uint BuffActiveAlternate = 0x200000;
    public const uint BuffActiveThird = 0x400000;
    public const uint DetachedAttachArgument = 0x8;
    public const uint KeptUntilStopped = 0x180050;
    public const uint PlacedInWorld = EffectMomentPlaced | EffectMomentPlacedAlternate | EffectMomentPlacedGround | HitOnTarget | ActivationPlaced;

    public static bool IsKeptUntilStopped(uint effectEvent) => (effectEvent & KeptUntilStopped) != 0;

    public static bool IsPlacedInWorld(uint effectEvent) => (effectEvent & PlacedInWorld) != 0;
}
