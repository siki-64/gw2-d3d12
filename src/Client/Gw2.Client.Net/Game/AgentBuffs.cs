using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public readonly record struct AgentBuff(
    uint InstanceId,
    uint SkillId,
    uint SourceAgentId,
    uint DurationMilliseconds,
    long StartMilliseconds,
    uint LevelAtApplication,
    bool IsActive)
{
    public const uint UnlimitedDuration = uint.MaxValue;

    public bool IsUnlimited => DurationMilliseconds == UnlimitedDuration;

    public long RemainingAt(long nowMilliseconds) =>
        !IsActive ? DurationMilliseconds : Math.Max(0, StartMilliseconds + DurationMilliseconds - nowMilliseconds);
}

public sealed class AgentBuffs
{
    public const int BuffActivate = 0x2de;
    public const int BuffApply = 0x2df;
    public const int BuffDeactivate = 0x2e0;
    public const int BuffDuration = 0x2e1;
    public const int BuffRemoveBySkill = 0x2e2;
    public const int BuffRemoveByInstance = 0x2e3;

    private const int AgentIdField = 1;
    private const int ApplySourceField = 2;
    private const int ApplySkillField = 3;
    private const int ApplyInstanceField = 4;
    private const int ApplyDurationField = 5;
    private const int ApplyLevelField = 6;
    private const int ApplyActiveField = 7;
    private const int TimedInstanceField = 2;
    private const int TimedMillisecondsField = 3;
    private const int RemovedKeyField = 3;

    private readonly Dictionary<uint, Dictionary<uint, AgentBuff>> _buffsByAgent = [];

    public IReadOnlyCollection<AgentBuff> BuffsOf(uint agentId) =>
        _buffsByAgent.TryGetValue(agentId, out var buffs) ? buffs.Values : [];

    public IReadOnlyCollection<uint> SkillIdsOf(uint agentId) => BuffsOf(agentId).Select(buff => buff.SkillId).ToHashSet();

    public AgentBuff? BuffOf(uint agentId, uint instanceId) =>
        _buffsByAgent.TryGetValue(agentId, out var buffs) && buffs.TryGetValue(instanceId, out var buff) ? buff : null;

    public void Handle(DecodedMessage message, long nowMilliseconds)
    {
        switch (message.MessageId)
        {
            case BuffApply:
                Apply(message, nowMilliseconds);
                break;
            case BuffActivate:
                Update(message, (buff, remaining) => buff with
                {
                    IsActive = true,
                    StartMilliseconds = Math.Max(0, nowMilliseconds - (buff.DurationMilliseconds - Math.Min(remaining, buff.DurationMilliseconds))),
                });
                break;
            case BuffDeactivate:
                Update(message, (buff, remaining) => buff.IsActive ? buff with { IsActive = false, DurationMilliseconds = remaining } : buff);
                break;
            case BuffDuration:
                Update(message, (buff, duration) => buff with { DurationMilliseconds = duration });
                break;
            case BuffRemoveByInstance:
                if (_buffsByAgent.TryGetValue(U32(message, AgentIdField), out var byInstance))
                    byInstance.Remove(U32(message, RemovedKeyField));
                break;
            case BuffRemoveBySkill:
                if (_buffsByAgent.TryGetValue(U32(message, AgentIdField), out var bySkill))
                {
                    uint skillId = U32(message, RemovedKeyField);
                    foreach (uint instanceId in bySkill.Values.Where(buff => buff.SkillId == skillId).Select(buff => buff.InstanceId).ToArray())
                        bySkill.Remove(instanceId);
                }
                break;
        }
    }

    private void Apply(DecodedMessage message, long nowMilliseconds)
    {
        uint agentId = U32(message, AgentIdField);
        if (!_buffsByAgent.TryGetValue(agentId, out var buffs))
            _buffsByAgent[agentId] = buffs = [];
        uint instanceId = U32(message, ApplyInstanceField);
        buffs[instanceId] = new AgentBuff(
            instanceId,
            U32(message, ApplySkillField),
            U32(message, ApplySourceField),
            U32(message, ApplyDurationField),
            nowMilliseconds,
            U32(message, ApplyLevelField),
            U32(message, ApplyActiveField) != 0);
    }

    private void Update(DecodedMessage message, Func<AgentBuff, uint, AgentBuff> change)
    {
        if (!_buffsByAgent.TryGetValue(U32(message, AgentIdField), out var buffs)) return;
        uint instanceId = U32(message, TimedInstanceField);
        if (buffs.TryGetValue(instanceId, out var buff))
            buffs[instanceId] = change(buff, U32(message, TimedMillisecondsField));
    }

    private static uint U32(DecodedMessage message, int field) => (uint)(ulong)message.Fields[field].Value!;
}
