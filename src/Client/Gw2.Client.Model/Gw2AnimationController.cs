using System;
using System.Collections.Generic;

namespace Gw2.Client.Model;

[Flags]
public enum Gw2AnimationEntryFlags : uint
{
    None = 0,
    FixedWeight = 0x02,
    ForcesUpdate = 0x08,
    FullWeightOutsidePriority = 0x20,
}

public struct Gw2AnimationEntry
{
    public ulong AnimationData;
    public Gw2AnimationEntryFlags Flags;
    public float Weight;
    public float TargetWeight;
    public float SampleWeight;
}

public struct Gw2AnimationClock
{
    public float LocalClock;
    public float Speed;
    public float LocalDuration;
    public int LoopIndex;
    public int LoopCount;
}

public static class Gw2AnimationController
{
    public const float WeightChangePerSecond = 4f;
    public const float NegligibleRemainingWeight = 1e-6f;

    public static void Step(Span<Gw2AnimationEntry> entries, float deltaSeconds)
    {
        EaseWeights(entries, deltaSeconds);
        MergeRestartedClips(entries);
        AssignSampleWeights(entries);
    }

    public static void EaseWeights(Span<Gw2AnimationEntry> entries, float deltaSeconds)
    {
        var maximumChange = WeightChangePerSecond * deltaSeconds;
        for (var index = 0; index < entries.Length; index++)
        {
            ref var entry = ref entries[index];
            if ((entry.Flags & Gw2AnimationEntryFlags.FixedWeight) != 0) continue;
            var change = Math.Clamp(entry.TargetWeight - entry.Weight, -maximumChange, maximumChange);
            entry.Weight = Math.Clamp(entry.Weight + change, 0f, 1f);
        }
    }

    public static void MergeRestartedClips(Span<Gw2AnimationEntry> entries)
    {
        for (var index = 0; index + 1 < entries.Length; index++)
        {
            ref var entry = ref entries[index];
            ref var next = ref entries[index + 1];
            if (entry.AnimationData != next.AnimationData) continue;
            next.Weight = Math.Min(1f, next.Weight + entry.Weight);
            entry.Weight = 0f;
        }
    }

    public static void AssignSampleWeights(Span<Gw2AnimationEntry> entries)
    {
        var remaining = 1f;
        for (var index = 0; index < entries.Length; index++)
        {
            ref var entry = ref entries[index];
            if ((entry.Flags & Gw2AnimationEntryFlags.FullWeightOutsidePriority) != 0)
            {
                entry.SampleWeight = 1f;
                continue;
            }
            if (remaining <= NegligibleRemainingWeight)
            {
                entry.SampleWeight = 0f;
                continue;
            }
            entry.SampleWeight = Math.Min(entry.Weight, remaining);
            remaining -= entry.SampleWeight;
        }
    }

    public static void Advance(ref Gw2AnimationClock clock, float deltaSeconds)
    {
        clock.LocalClock += deltaSeconds * clock.Speed;
        if (clock.LocalDuration <= 0f) return;
        while (clock.LocalClock >= clock.LocalDuration)
        {
            if (clock.LoopCount != 0 && clock.LoopIndex + 1 >= clock.LoopCount)
            {
                clock.LocalClock = clock.LocalDuration;
                return;
            }
            clock.LocalClock -= clock.LocalDuration;
            clock.LoopIndex++;
        }
        while (clock.LocalClock < 0f)
        {
            if (clock.LoopCount != 0 && clock.LoopIndex <= 0)
            {
                clock.LocalClock = 0f;
                return;
            }
            clock.LocalClock += clock.LocalDuration;
            clock.LoopIndex--;
        }
    }
}
