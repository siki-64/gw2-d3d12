using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2AnimSequenceStepMove(Quaternion MoveRotation, Quaternion FacingRotation, Vector2 Velocity);

public sealed record Gw2AnimSequenceStep(uint Type, float AnimationSpeed, uint Flags, uint DurationMilliseconds = 0)
{
    public Gw2AnimSequenceStepMove? Move { get; init; }
}

public sealed record Gw2AnimSequenceChargeStage(ushort DurationMilliseconds, byte EndingChargeLevel);

public sealed record Gw2AnimSequenceTrigger(uint Trigger, uint Time, uint Flags);

public sealed record Gw2AnimSequenceData(
    ulong Token,
    uint Flags,
    float BlendInTime,
    float BlendOutTime,
    IReadOnlyList<Gw2AnimSequenceStep> Steps,
    IReadOnlyList<Gw2AnimSequenceTrigger> Triggers)
{
    public IReadOnlyList<Gw2AnimSequenceChargeStage> ChargeStages { get; init; } = [];
}

public sealed record Gw2AnimSequence(ulong Sequence, IReadOnlyList<Gw2AnimSequenceData> AnimationData);

public sealed class Gw2AnimSequences
{
    public const uint ExecuteTrigger = 3;
    public const ulong DefaultAnimationDataToken = 0;

    public Gw2AnimSequences(IReadOnlyList<Gw2AnimSequence> sequences)
    {
        Sequences = sequences;
    }

    public IReadOnlyList<Gw2AnimSequence> Sequences { get; }

    public const uint EffectMomentTrigger = 1;

    public Gw2AnimSequenceData? DataFor(ulong sequenceToken, ulong variantToken)
    {
        var sequence = Sequences.FirstOrDefault(candidate => candidate.Sequence == sequenceToken);
        if (sequence is null) return null;
        return sequence.AnimationData.FirstOrDefault(candidate => candidate.Token == variantToken)
            ?? sequence.AnimationData.FirstOrDefault(candidate => candidate.Token == DefaultAnimationDataToken);
    }

    public uint? FirstExecuteMilliseconds(ulong sequenceToken, ulong variantToken)
    {
        var data = DataFor(sequenceToken, variantToken);
        var execute = data?.Triggers.Where(trigger => trigger.Trigger == ExecuteTrigger).OrderBy(trigger => trigger.Time).FirstOrDefault();
        return execute?.Time;
    }

    public static Gw2AnimSequences? TryParse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.FindRoot("PackAnimSequences");
        if (root is null) return null;

        var sequences = new List<Gw2AnimSequence>();
        if (root.Fields.GetValueOrDefault("sequences") is object?[] sequenceObjects)
            foreach (var sequenceObject in sequenceObjects)
                if (sequenceObject is PackObject sequence)
                    sequences.Add(new(
                        PackValue.AsU64(sequence.Fields.GetValueOrDefault("sequence")),
                        AnimationData(sequence.Fields.GetValueOrDefault("animationData"))));

        return new(sequences);
    }

    private static IReadOnlyList<Gw2AnimSequenceData> AnimationData(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2AnimSequenceData>(values.Length);
        foreach (var item in values)
            if (item is PackObject data)
                result.Add(new(
                    PackValue.AsU64(data.Fields.GetValueOrDefault("token")),
                    PackValue.AsU32(data.Fields.GetValueOrDefault("flags")),
                    PackValue.AsF32(data.Fields.GetValueOrDefault("blendInTime")),
                    PackValue.AsF32(data.Fields.GetValueOrDefault("blendOutTime")),
                    Steps(data.Fields.GetValueOrDefault("steps")),
                    Triggers(data.Fields.GetValueOrDefault("triggers")))
                {
                    ChargeStages = PackValue.AsObjects(data.Fields.GetValueOrDefault("chargeStages"))
                        .Select(stage => new Gw2AnimSequenceChargeStage(
                            (ushort)PackValue.AsU32(stage.Fields.GetValueOrDefault("duration")),
                            PackValue.AsU8(stage.Fields.GetValueOrDefault("endingChargeLevel"))))
                        .ToArray(),
                });
        return result;
    }

    private static IReadOnlyList<Gw2AnimSequenceStep> Steps(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2AnimSequenceStep>(values.Length);
        foreach (var item in values)
            if (item is PackObject step)
                result.Add(new(
                    PackValue.AsU32(step.Fields.GetValueOrDefault("type")),
                    PackValue.AsF32(step.Fields.GetValueOrDefault("animationSpeed")),
                    PackValue.AsU32(step.Fields.GetValueOrDefault("flags")),
                    StepDurationMilliseconds(step))
                {
                    Move = StepMove(step.Fields.GetValueOrDefault("move") as PackObject),
                });
        return result;
    }

    private static Gw2AnimSequenceStepMove? StepMove(PackObject? move) => move is null
        ? null
        : new(
            Rotation(move.Fields.GetValueOrDefault("moveRotation")),
            Rotation(move.Fields.GetValueOrDefault("facingRotation")),
            move.Fields.GetValueOrDefault("velocity") is float[] { Length: >= 2 } velocity
                ? new Vector2(velocity[0], velocity[1])
                : Vector2.Zero);

    private static Quaternion Rotation(object? value) => value is float[] { Length: >= 4 } rotation
        ? new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3])
        : Quaternion.Identity;

    private static uint StepDurationMilliseconds(PackObject step)
    {
        var part = step.Fields.GetValueOrDefault("action") as PackObject ?? step.Fields.GetValueOrDefault("move") as PackObject;
        return part is null ? 0 : PackValue.AsU32(part.Fields.GetValueOrDefault("duration"));
    }

    private static IReadOnlyList<Gw2AnimSequenceTrigger> Triggers(object? value)
    {
        if (value is not object?[] values) return [];
        var result = new List<Gw2AnimSequenceTrigger>(values.Length);
        foreach (var item in values)
            if (item is PackObject trigger)
                result.Add(new(
                    PackValue.AsU32(trigger.Fields.GetValueOrDefault("trigger")),
                    PackValue.AsU32(trigger.Fields.GetValueOrDefault("time")),
                    PackValue.AsU32(trigger.Fields.GetValueOrDefault("flags"))));
        return result;
    }
}
