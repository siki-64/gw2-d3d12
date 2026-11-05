using System;
using System.Collections.Generic;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed class EnergyMeter
{
    private const uint PausedFlag = 1;
    private const uint DefaultRateFlag = 2;

    private uint _flags = DefaultRateFlag;

    public long TimestampMilliseconds { get; private set; }

    public float Current { get; private set; }

    public float Maximum { get; private set; }

    public float ChangePerSecond { get; private set; }

    public float DefaultChangePerSecond { get; private set; }

    public float ChangeScale { get; private set; } = 1;

    public bool Paused => (_flags & PausedFlag) != 0;

    public bool UsesDefaultRate => (_flags & DefaultRateFlag) != 0;

    public float ValueAt(long nowMilliseconds)
    {
        if (Paused || TimestampMilliseconds == 0) return Current;
        return Math.Clamp(Current + (nowMilliseconds - TimestampMilliseconds) * 0.001f * ChangePerSecond, 0, Maximum);
    }

    public void AdvanceTo(long nowMilliseconds)
    {
        if (!Paused && TimestampMilliseconds != 0 && TimestampMilliseconds != nowMilliseconds) Current = ValueAt(nowMilliseconds);
        TimestampMilliseconds = nowMilliseconds;
    }

    public void Adjust(float amount) => Current = Math.Clamp(Current + amount, 0, Maximum);

    public void SetCurrent(float current) => Current = Math.Clamp(current, 0, Maximum);

    public void SetMaximum(float maximum)
    {
        if (maximum == Maximum) return;
        var fraction = Maximum == 0 ? 0 : Current / Maximum;
        Maximum = maximum;
        Current = fraction * maximum;
    }

    public void SetChangePerSecond(float changePerSecond)
    {
        _flags &= ~DefaultRateFlag;
        ChangePerSecond = changePerSecond;
    }

    public void SetDefaultChangePerSecond(float changePerSecond)
    {
        DefaultChangePerSecond = changePerSecond;
        if (UsesDefaultRate) ChangePerSecond = DefaultChangePerSecond * ChangeScale;
    }

    public void SetChangeScale(float scale)
    {
        ChangeScale = scale;
        if (UsesDefaultRate) ChangePerSecond = DefaultChangePerSecond * ChangeScale;
    }

    public void RestoreDefaultRate()
    {
        _flags |= DefaultRateFlag;
        ChangePerSecond = DefaultChangePerSecond * ChangeScale;
    }

    public void SetPaused(bool paused) => _flags = paused ? _flags | PausedFlag : _flags & ~PausedFlag;
}

public sealed class AgentEnergies
{
    public const int EnergyChangeScale = 0xde;
    public const int EnergyLimits = 0xdf;
    public const int EnergyState = 0xe0;
    public const int KindCount = 4;

    private const int AgentIdField = 1;
    private const int KindField = 2;
    private const int ScaleField = 3;
    private const int MaximumField = 3;
    private const int CurrentField = 4;
    private const int ChangePerSecondField = 5;
    private const int DefaultChangePerSecondField = 6;
    private const int ChangeScaleField = 7;
    private const int PausedField = 8;

    private readonly Dictionary<uint, EnergyMeter[]> _metersByAgent = [];

    public float DefaultMaximum { get; set; }

    public float DefaultChangePerSecond { get; set; }

    public EnergyMeter? MeterOf(uint agentId, uint kind) =>
        kind < KindCount && _metersByAgent.TryGetValue(agentId, out var meters) ? meters[kind] : null;

    public float ValueOf(uint agentId, uint kind, long nowMilliseconds) => MeterOf(agentId, kind)?.ValueAt(nowMilliseconds) ?? 0;

    public EnergyMeter MeterFor(uint agentId, uint kind, long nowMilliseconds)
    {
        if (!_metersByAgent.TryGetValue(agentId, out var meters))
        {
            meters = new EnergyMeter[KindCount];
            for (var index = 0; index < KindCount; index++) meters[index] = DefaultMeter(nowMilliseconds);
            _metersByAgent[agentId] = meters;
        }
        var meter = meters[Math.Min(kind, KindCount - 1)];
        meter.AdvanceTo(nowMilliseconds);
        return meter;
    }

    private EnergyMeter DefaultMeter(long nowMilliseconds)
    {
        var meter = new EnergyMeter();
        meter.AdvanceTo(nowMilliseconds);
        meter.SetMaximum(DefaultMaximum);
        meter.SetCurrent(DefaultMaximum);
        meter.SetDefaultChangePerSecond(DefaultChangePerSecond);
        meter.RestoreDefaultRate();
        return meter;
    }

    public void Handle(DecodedMessage message, long nowMilliseconds)
    {
        if (message.MessageId is not (EnergyChangeScale or EnergyLimits or EnergyState)) return;
        var kind = (uint)MessageNumbers.Number(message, KindField);
        if (kind >= KindCount) return;
        var meter = MeterFor((uint)MessageNumbers.Number(message, AgentIdField), kind, nowMilliseconds);
        switch (message.MessageId)
        {
            case EnergyChangeScale:
                meter.SetChangeScale(MessageNumbers.Number(message, ScaleField));
                break;
            case EnergyLimits:
                meter.SetMaximum(MessageNumbers.Number(message, MaximumField));
                meter.SetCurrent(MessageNumbers.Number(message, CurrentField));
                break;
            case EnergyState:
                meter.SetMaximum(MessageNumbers.Number(message, MaximumField));
                meter.SetCurrent(MessageNumbers.Number(message, CurrentField));
                meter.SetChangePerSecond(MessageNumbers.Number(message, ChangePerSecondField));
                meter.SetDefaultChangePerSecond(MessageNumbers.Number(message, DefaultChangePerSecondField));
                meter.SetChangeScale(MessageNumbers.Number(message, ChangeScaleField));
                meter.SetPaused(MessageNumbers.Number(message, PausedField) != 0);
                break;
        }
    }
}
