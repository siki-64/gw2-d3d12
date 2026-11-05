using System.Numerics;
using Gw2.Client.Net.Game;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.State;

public sealed class WorldState
{
    public GameAgents Agents { get; } = new();
    public AgentEquipment Equipment { get; } = new();
    public AgentAppearances Appearances { get; } = new();
    public AgentCompositeIdentities CompositeIdentities { get; } = new();
    public AgentEndurance Endurance { get; } = new();
    public AgentActivationSpeeds ActivationSpeeds { get; } = new();
    public AgentWeaponStances WeaponStances { get; } = new();
    public AgentVitals Vitals { get; } = new();
    public AgentBuffs Buffs { get; } = new();
    public AgentMountWardrobe MountWardrobe { get; } = new();
    public GameMissiles Missiles { get; } = new();
    public AgentEnergies Energies { get; } = new();
    public AgentProfessionResources ProfessionResources { get; } = new();
    public AgentLifeState LifeState { get; } = new();
    public AgentHits Hits { get; } = new();
    public AgentBreakBars BreakBars { get; } = new();
    public AgentAvoidances Avoidances { get; } = new();
    public GadgetCombatEvents GadgetCombat { get; } = new();
    public AgentFixedEffectTriggers FixedEffectTriggers { get; } = new();
    public AgentSkillEffects SkillEffects { get; } = new();
    public AgentTransformations Transformations { get; } = new();
    public OwnContentFunctionState ContentFunctions { get; } = new();
    public OwnSkillbar Skillbar { get; } = new();
    public AgentKennels Kennels { get; } = new();
    public AgentSkillOrders SkillOrders { get; } = new();

    public void Advance(float elapsedSeconds)
    {
        Agents.Advance(elapsedSeconds);
        Missiles.Advance(elapsedSeconds, AgentPositionOf);
    }

    private Vector3? AgentPositionOf(uint agentId) => Agents.TryGet(agentId, out var agent) ? agent.PositionInGameUnits : null;

    public void Handle(DecodedMessage message, long nowMilliseconds, float defaultRunSpeedInGameUnits)
    {
        Agents.DefaultRunSpeedInGameUnits = defaultRunSpeedInGameUnits;
        Agents.Handle(message);
        Equipment.Handle(message);
        Appearances.Handle(message);
        CompositeIdentities.Handle(message);
        Endurance.Handle(message);
        ActivationSpeeds.Handle(message);
        WeaponStances.Handle(message);
        Vitals.Handle(message, nowMilliseconds);
        Buffs.Handle(message, nowMilliseconds);
        MountWardrobe.Handle(message);
        Missiles.Handle(message, AgentPositionOf);
        Energies.Handle(message, nowMilliseconds);
        ProfessionResources.Handle(message, nowMilliseconds);
        LifeState.Handle(message);
        Hits.Handle(message);
        BreakBars.Handle(message);
        Avoidances.Handle(message);
        GadgetCombat.Handle(message);
        FixedEffectTriggers.Handle(message);
        SkillEffects.Handle(message);
        Transformations.Handle(message);
        ContentFunctions.Handle(message);
        Skillbar.Handle(message);
        Kennels.Handle(message);
        SkillOrders.Handle(message);
    }
}
