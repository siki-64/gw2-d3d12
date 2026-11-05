using System.Linq;
using System.Buffers.Binary;
using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public enum Gw2SkillProgressWidget
{
    None,
    Warmup,
    Channel,
    Charge,
}

public sealed record Gw2SkillChargeStage(double Seconds, byte EndingChargeLevel);

public sealed record Gw2SkillCastTiming(Gw2SkillProgressWidget Widget, double FirstExecuteSeconds, double LastExecuteSeconds, IReadOnlyList<Gw2SkillChargeStage> ChargeStages)
{
    public static Gw2SkillCastTiming None { get; } = new(Gw2SkillProgressWidget.None, 0.0, 0.0, []);

    public Gw2SkillCastTiming AtActivationSpeed(float activationSpeed) =>
        activationSpeed <= 0f || activationSpeed == 1f ? this : this with
        {
            FirstExecuteSeconds = FirstExecuteSeconds / activationSpeed,
            LastExecuteSeconds = LastExecuteSeconds / activationSpeed,
            ChargeStages = ChargeStages.Select(stage => stage with { Seconds = stage.Seconds / activationSpeed }).ToArray(),
        };
}

public readonly record struct Gw2SkillLoopStepCounts(int Shortest, int Longest);

public sealed class Gw2SkillCastBar
{
    public const double ShortestWarmupSeconds = 0.25;
    public const double ShortestChannelAfterExecuteSeconds = 1.0;
    public const double LongestChannelLoopSeconds = 15.0;
    public const uint LoopFirstStepFlag = 0x10;
    public const uint LoopLastStepFlag = 0x20;
    private const double MillisecondsPerSecond = 1000.0;
    private const int TokenSize = sizeof(ulong);

    private readonly Gw2ContentIndex _index;
    private readonly SkillPaletteContentLayout _skills;
    private readonly SkillCastBarLayout _layout;
    private readonly Gw2AnimSequences _sequences;

    public Gw2SkillCastBar(Gw2ContentIndex index, SkillPaletteContentLayout skills, SkillCastBarLayout layout, Gw2AnimSequences sequences)
    {
        _index = index;
        _skills = skills;
        _layout = layout;
        _sequences = sequences;
    }

    public Gw2SkillCastTiming TimingOf(uint skillId)
    {
        if (_index.FindTyped(_skills.SkillContentType, skillId) is not { } skill) return Gw2SkillCastTiming.None;
        if (_index.ReadUInt32(skill, _skills.SkillPayloadTypeOffset) != SkillPaletteContentLayout.AbilitySkillPayloadType) return Gw2SkillCastTiming.None;
        if (_index.ReadPointer(skill, _skills.SkillPayloadOffset) is not { } ability) return Gw2SkillCastTiming.None;
        if ((_index.ReadUInt32(ability, _layout.AbilityFlagsOffset) & _layout.HidesCastBarAbilityFlag) != 0) return Gw2SkillCastTiming.None;
        var sequenceToken = BinaryPrimitives.ReadUInt64LittleEndian(_index.ReadBytes(ability, _layout.AbilitySequenceTokenOffset, TokenSize));
        var variantToken = BinaryPrimitives.ReadUInt64LittleEndian(_index.ReadBytes(ability, _layout.AbilityVariantTokenOffset, TokenSize));
        var loopCounts = _index.ReadBytes(ability, _layout.AbilityLoopStepCountsOffset, TokenSize);
        var loopStepCounts = new Gw2SkillLoopStepCounts(BinaryPrimitives.ReadInt32LittleEndian(loopCounts), BinaryPrimitives.ReadInt32LittleEndian(loopCounts[sizeof(int)..]));
        return TimingOf(_sequences.DataFor(sequenceToken, variantToken), loopStepCounts);
    }

    public static Gw2SkillCastTiming TimingOf(Gw2AnimSequenceData? data, Gw2SkillLoopStepCounts loopStepCounts)
    {
        if (data is null) return Gw2SkillCastTiming.None;
        var executes = data.Triggers.Where(trigger => trigger.Trigger == Gw2AnimSequences.ExecuteTrigger).Select(trigger => trigger.Time / MillisecondsPerSecond).ToArray();
        if (executes.Length == 0) return Gw2SkillCastTiming.None;
        var loop = LoopOf(data.Steps, loopStepCounts);
        var firstExecute = executes.Min();
        if (firstExecute > loop.OnePassSeconds + loop.BeforeLoopSeconds) firstExecute += loop.LongestSeconds - loop.OnePassSeconds;
        var lastExecute = executes.Max();
        if (lastExecute >= loop.BeforeLoopSeconds) lastExecute += loop.LongestSeconds - loop.OnePassSeconds;
        var chargeStages = data.ChargeStages.Select(stage => new Gw2SkillChargeStage(stage.DurationMilliseconds / MillisecondsPerSecond, stage.EndingChargeLevel)).ToArray();
        var widget = WidgetOf(firstExecute, lastExecute, loop, chargeStages.Length);
        return new Gw2SkillCastTiming(widget, firstExecute, lastExecute, chargeStages);
    }

    private static Gw2SkillProgressWidget WidgetOf(double firstExecute, double lastExecute, Gw2SkillLoop loop, int chargeStageCount)
    {
        if (loop.LongestSeconds > loop.ShortestSeconds && chargeStageCount > 0) return Gw2SkillProgressWidget.Charge;
        if (lastExecute - firstExecute >= ShortestChannelAfterExecuteSeconds && loop.LongestSeconds <= LongestChannelLoopSeconds) return Gw2SkillProgressWidget.Channel;
        return firstExecute >= ShortestWarmupSeconds ? Gw2SkillProgressWidget.Warmup : Gw2SkillProgressWidget.None;
    }

    private readonly record struct Gw2SkillLoop(double BeforeLoopSeconds, double OnePassSeconds, double ShortestSeconds, double LongestSeconds);

    private static Gw2SkillLoop LoopOf(IReadOnlyList<Gw2AnimSequenceStep> steps, Gw2SkillLoopStepCounts counts)
    {
        var loopFirst = 0;
        var loopEnd = 0;
        for (var index = 0; index < steps.Count; index++)
        {
            if ((steps[index].Flags & LoopFirstStepFlag) != 0) loopFirst = index;
            if ((steps[index].Flags & LoopLastStepFlag) != 0) loopEnd = index + 1;
        }
        double SecondsOf(int index) => steps[index].DurationMilliseconds / MillisecondsPerSecond;
        if (loopFirst >= loopEnd) return new Gw2SkillLoop(Enumerable.Range(0, steps.Count).Sum(SecondsOf), 0.0, 0.0, 0.0);
        var loopLength = loopEnd - loopFirst;
        double CyclingSum(int stepCount) => Enumerable.Range(0, Math.Max(0, stepCount)).Sum(step => SecondsOf(loopFirst + step % loopLength));
        return new Gw2SkillLoop(
            Enumerable.Range(0, loopFirst).Sum(SecondsOf),
            Enumerable.Range(loopFirst, loopLength).Sum(SecondsOf),
            CyclingSum(Math.Min(counts.Shortest, counts.Longest + 1)),
            CyclingSum(counts.Longest));
    }
}
