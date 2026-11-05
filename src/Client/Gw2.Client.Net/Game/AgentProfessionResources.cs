using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public enum ProfessionResourceKind
{
    EngineerMeter,
    EngineerValue,
    ElementalistCounter,
    ElementalistValue,
    GuardianMeter,
    GuardianPair,
    MesmerPair,
    NecromancerLifeForce,
    NecromancerCounter,
    RangerMeter,
    RangerStacks,
    RangerStacksChange,
    RevenantEnergy,
    RevenantLegendSwapMilliseconds,
    ThiefInitiative,
    ThiefSecondaryMeter,
    ThiefCounter,
    WarriorAdrenaline,
    WarriorSignedValue,
}

public sealed class AgentProfessionResources
{
    public const int MaximumStacks = 16;

    private enum Layout
    {
        MaximumRateCurrent,
        CurrentMaximumRate,
        CurrentMaximum,
        MaximumCurrent,
        ValueAndSecondValue,
        Value,
        SignedValue,
        CappedStacks,
    }

    private readonly record struct Part(ProfessionResourceKind Kind, Layout Layout, int FirstField);

    private static readonly HashSet<int> ProfessionRemoves = [0x1d6, 0x1da, 0x1de, 0x1e2, 0x1e5, 0x1e9, 0x1ee, 0x1f3, 0x1f9];

    private static readonly Dictionary<int, Part[]> PartsByMessage = new()
    {
        [0x1d5] = [new(ProfessionResourceKind.EngineerMeter, Layout.MaximumRateCurrent, 2)],
        [0x1d7] = [new(ProfessionResourceKind.EngineerMeter, Layout.MaximumRateCurrent, 2)],
        [0x1d8] = [new(ProfessionResourceKind.EngineerValue, Layout.Value, 2)],
        [0x1d9] = [new(ProfessionResourceKind.ElementalistCounter, Layout.MaximumCurrent, 2), new(ProfessionResourceKind.ElementalistValue, Layout.Value, 4)],
        [0x1db] = [new(ProfessionResourceKind.ElementalistCounter, Layout.MaximumCurrent, 2)],
        [0x1dc] = [new(ProfessionResourceKind.ElementalistValue, Layout.Value, 2)],
        [0x1dd] = [new(ProfessionResourceKind.GuardianMeter, Layout.MaximumRateCurrent, 2), new(ProfessionResourceKind.GuardianPair, Layout.ValueAndSecondValue, 5)],
        [0x1df] = [new(ProfessionResourceKind.GuardianMeter, Layout.MaximumRateCurrent, 2)],
        [0x1e0] = [new(ProfessionResourceKind.GuardianPair, Layout.ValueAndSecondValue, 2)],
        [0x1e1] = [new(ProfessionResourceKind.MesmerPair, Layout.ValueAndSecondValue, 2)],
        [0x1e3] = [new(ProfessionResourceKind.MesmerPair, Layout.ValueAndSecondValue, 2)],
        [0x1e4] = [new(ProfessionResourceKind.NecromancerLifeForce, Layout.CurrentMaximum, 2), new(ProfessionResourceKind.NecromancerCounter, Layout.CurrentMaximum, 4)],
        [0x1e6] = [new(ProfessionResourceKind.NecromancerLifeForce, Layout.CurrentMaximum, 2)],
        [0x1e7] = [new(ProfessionResourceKind.NecromancerCounter, Layout.CurrentMaximum, 2)],
        [0x1e8] = [new(ProfessionResourceKind.RangerMeter, Layout.MaximumRateCurrent, 2)],
        [0x1ea] = [new(ProfessionResourceKind.RangerStacks, Layout.CappedStacks, 2)],
        [0x1ec] = [new(ProfessionResourceKind.RangerMeter, Layout.MaximumRateCurrent, 2)],
        [0x1ef] = [new(ProfessionResourceKind.RevenantLegendSwapMilliseconds, Layout.Value, 2)],
        [0x1f0] = [new(ProfessionResourceKind.RevenantEnergy, Layout.CurrentMaximumRate, 2)],
        [0x1f2] = [new(ProfessionResourceKind.ThiefInitiative, Layout.MaximumRateCurrent, 2), new(ProfessionResourceKind.ThiefCounter, Layout.CurrentMaximum, 5)],
        [0x1f4] = [new(ProfessionResourceKind.ThiefInitiative, Layout.MaximumRateCurrent, 2)],
        [0x1f5] = [new(ProfessionResourceKind.ThiefSecondaryMeter, Layout.CurrentMaximum, 2)],
        [0x1f6] = [new(ProfessionResourceKind.ThiefCounter, Layout.CurrentMaximum, 2)],
        [0x1f8] = [new(ProfessionResourceKind.WarriorAdrenaline, Layout.MaximumCurrent, 2)],
        [0x1fa] = [new(ProfessionResourceKind.WarriorSignedValue, Layout.SignedValue, 2)],
        [0x1fb] = [new(ProfessionResourceKind.WarriorAdrenaline, Layout.MaximumCurrent, 2)],
    };

    private readonly Dictionary<uint, Dictionary<ProfessionResourceKind, ProfessionResource>> _resourcesByAgent = [];

    public IReadOnlyDictionary<ProfessionResourceKind, ProfessionResource> ResourcesOf(uint agentId) =>
        _resourcesByAgent.TryGetValue(agentId, out var resources) ? resources : new Dictionary<ProfessionResourceKind, ProfessionResource>();

    public ProfessionResource? ResourceOf(uint agentId, ProfessionResourceKind kind) =>
        _resourcesByAgent.TryGetValue(agentId, out var resources) && resources.TryGetValue(kind, out var resource) ? resource : null;

    public void Handle(DecodedMessage message, long nowMilliseconds)
    {
        if (message.Fields.Count < 2) return;
        var agentId = (uint)MessageNumbers.Number(message, 1);
        if (ProfessionRemoves.Contains(message.MessageId))
        {
            _resourcesByAgent.Remove(agentId);
            return;
        }
        if (!PartsByMessage.TryGetValue(message.MessageId, out var parts)) return;
        if (!_resourcesByAgent.TryGetValue(agentId, out var resources)) _resourcesByAgent[agentId] = resources = [];
        foreach (var part in parts)
        {
            float Field(int offset) => MessageNumbers.Number(message, part.FirstField + offset);
            var name = part.Kind.ToString();
            switch (part.Layout)
            {
                case Layout.MaximumRateCurrent:
                    resources[part.Kind] = new ProfessionResource(name, System.Math.Clamp(Field(2), 0, Field(0)), Field(0), Field(1), nowMilliseconds);
                    break;
                case Layout.CurrentMaximumRate:
                    resources[part.Kind] = new ProfessionResource(name, System.Math.Clamp(Field(0), 0, Field(1)), Field(1), Field(2), nowMilliseconds);
                    break;
                case Layout.CurrentMaximum:
                    resources[part.Kind] = new ProfessionResource(name, System.Math.Clamp(Field(0), 0, Field(1)), Field(1), 0, nowMilliseconds);
                    break;
                case Layout.MaximumCurrent:
                    resources[part.Kind] = new ProfessionResource(name, System.Math.Clamp(Field(1), 0, Field(0)), Field(0), 0, nowMilliseconds);
                    break;
                case Layout.ValueAndSecondValue:
                    resources[part.Kind] = new ProfessionResource(name, Field(0), Field(1), 0, nowMilliseconds);
                    break;
                case Layout.Value:
                    resources[part.Kind] = new ProfessionResource(name, Field(0), Field(0), 0, nowMilliseconds);
                    break;
                case Layout.SignedValue:
                    var magnitude = Field(0);
                    var signed = Field(1) != 0 ? -magnitude : magnitude;
                    resources[part.Kind] = new ProfessionResource(name, signed, magnitude, 0, nowMilliseconds);
                    break;
                case Layout.CappedStacks:
                    var maximum = System.Math.Clamp(Field(1), 0, MaximumStacks);
                    resources[part.Kind] = new ProfessionResource(name, System.Math.Clamp(Field(0), 0, maximum), maximum, 0, nowMilliseconds);
                    resources[ProfessionResourceKind.RangerStacksChange] = new ProfessionResource(nameof(ProfessionResourceKind.RangerStacksChange), Field(2), Field(2), 0, nowMilliseconds);
                    break;
            }
        }
    }
}
