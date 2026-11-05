using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public readonly record struct AgentHealth(float Current, float Maximum, float Barrier, float BarrierMaximum, float ChangePerSecond);

public readonly record struct ProfessionResource(string Name, float Value, float Maximum, float ChangePerSecond, long TimestampMilliseconds)
{
    public float ValueAt(long nowMilliseconds) => ChangePerSecond == 0 ? Value :
        Math.Clamp(Value + (nowMilliseconds - TimestampMilliseconds) * 0.001f * ChangePerSecond, 0f, Maximum);
}

public readonly record struct SkillRecharge(uint SkillId, long DurationMilliseconds, long RemainingMilliseconds, long TimestampMilliseconds)
{
    public long RemainingAt(long nowMilliseconds) => Math.Max(0, RemainingMilliseconds - (nowMilliseconds - TimestampMilliseconds));
}

public sealed class AgentVitals
{
    public const int HealthChange = 0xFF;
    public const int HealthCreate = 0x100;
    public const int HealthSet = 0x101;
    public const int HealthLimits = 0x103;
    public const int HealthChangeRate = 0x104;
    public const int SkillRechargeSet = 0x276;

    private const int AgentIdField = 1;
    private const int CreateCurrentField = 3;
    private const int CreateMaximumField = 4;
    private const int CreateChangeRateField = 5;
    private const int CreateBarrierField = 6;
    private const int CreateBarrierMaximumField = 7;
    private const int ChangeHealthField = 2;
    private const int ChangeBarrierField = 3;
    private const int HealthCurrentField = 2;
    private const int HealthBarrierField = 3;
    private const int LimitsCurrentField = 2;
    private const int LimitsMaximumField = 3;
    private const int LimitsBarrierField = 4;
    private const int LimitsBarrierMaximumField = 5;
    private const int ChangeRateField = 2;
    private const int RechargeSkillField = 2;
    private const int RechargeDurationField = 4;
    private const int RechargeRemainingField = 5;

    private readonly Dictionary<uint, AgentHealth> _healthByAgent = [];
    private readonly Dictionary<uint, Dictionary<uint, SkillRecharge>> _rechargeByAgent = [];
    private readonly Dictionary<uint, Dictionary<uint, int>> _chargesByAgent = [];
    private const int RechargeKindField = 3;
    private const int RechargeChargesField = 6;
    private const int RechargeKindCooldown = 0;
    private const int RechargeKindCharges = 1;

    public int? ChargesOf(uint agentId, uint skillId) =>
        _chargesByAgent.TryGetValue(agentId, out var bySkill) && bySkill.TryGetValue(skillId, out var charges) ? charges : null;

    public AgentHealth? HealthOf(uint agentId) => _healthByAgent.TryGetValue(agentId, out var health) ? health : null;

    public SkillRecharge? RechargeOf(uint agentId, uint skillId) =>
        _rechargeByAgent.TryGetValue(agentId, out var bySkill) && bySkill.TryGetValue(skillId, out var recharge) ? recharge : null;

    public void Handle(DecodedMessage message, long nowMilliseconds)
    {
        if (message.Fields.Count <= AgentIdField) return;
        var agentId = (uint)Number(message, AgentIdField);
        var health = HealthOf(agentId) ?? default;
        switch (message.MessageId)
        {
            case HealthCreate:
                _healthByAgent[agentId] = new AgentHealth(
                    Number(message, CreateCurrentField),
                    Number(message, CreateMaximumField),
                    Number(message, CreateBarrierField),
                    Number(message, CreateBarrierMaximumField),
                    Number(message, CreateChangeRateField));
                return;
            case HealthChange when _healthByAgent.ContainsKey(agentId):
                _healthByAgent[agentId] = health with
                {
                    Current = Math.Clamp(health.Current + Number(message, ChangeHealthField), 0f, health.Maximum),
                    Barrier = Math.Clamp(health.Barrier + Number(message, ChangeBarrierField), 0f, health.BarrierMaximum),
                };
                return;
            case HealthSet:
                _healthByAgent[agentId] = health with { Current = Number(message, HealthCurrentField), Barrier = Number(message, HealthBarrierField) };
                return;
            case HealthLimits:
                _healthByAgent[agentId] = health with
                {
                    Current = Number(message, LimitsCurrentField),
                    Maximum = Number(message, LimitsMaximumField),
                    Barrier = Number(message, LimitsBarrierField),
                    BarrierMaximum = Number(message, LimitsBarrierMaximumField),
                };
                return;
            case HealthChangeRate:
                _healthByAgent[agentId] = health with { ChangePerSecond = Number(message, ChangeRateField) };
                return;
            case SkillRechargeSet:
                var skillId = (uint)Number(message, RechargeSkillField);
                var rechargeKind = message.Fields.Count > RechargeKindField ? (int)Number(message, RechargeKindField) : RechargeKindCooldown;
                if (rechargeKind == RechargeKindCharges)
                {
                    if (message.Fields.Count <= RechargeChargesField) return;
                    if (!_chargesByAgent.TryGetValue(agentId, out var chargesBySkill)) _chargesByAgent[agentId] = chargesBySkill = [];
                    chargesBySkill[skillId] = (int)Number(message, RechargeChargesField);
                    return;
                }
                if (rechargeKind != RechargeKindCooldown) return;
                if (!_rechargeByAgent.TryGetValue(agentId, out var bySkill)) _rechargeByAgent[agentId] = bySkill = [];
                bySkill[skillId] = new SkillRecharge(skillId, (long)Number(message, RechargeDurationField), (long)Number(message, RechargeRemainingField), nowMilliseconds);
                return;
        }
    }

    private static float Number(DecodedMessage message, int field) => MessageNumbers.Number(message, field);
}
