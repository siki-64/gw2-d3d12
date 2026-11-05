using System;
using System.Collections.Generic;

namespace Gw2.Client;

internal enum CharacterLodState
{
    Full = 0,
    Reduced = 1,
    Lowest = 2,
    Excluded = 3,
}

[Flags]
internal enum CharacterLodPriority : uint
{
    None = 0,
    AgentFlag0x2 = 0x10000000,
    AgentFlag0x800 = 0x20000000,
    AlwaysFullDetail = 0x40000000,
    AgentKind3 = 0x04000000,
    ControlledCharacter = 0x80000000,
}

internal readonly record struct CharacterLodBudget(int Full, int Reduced)
{
    internal const int DefaultCountOption = 2;
    internal const int DefaultQualityOption = 2;

    private static readonly int[] TotalByCountOption = [30, 60, 100, 200, 300];
    private static readonly int[] FullByQualityOption = [0, 30, 60, 100, 300];

    internal static CharacterLodBudget Of(int countOption, int qualityOption)
    {
        int total = TotalByCountOption[countOption];
        int full = Math.Min(FullByQualityOption[qualityOption], total);
        return new CharacterLodBudget(full, total - full);
    }

    internal static CharacterLodBudget Default => Of(DefaultCountOption, DefaultQualityOption);
}

internal sealed class CharacterLodEntry(uint agentId)
{
    internal uint AgentId { get; } = agentId;
    internal float Cost { get; set; } = float.MaxValue;
    internal CharacterLodPriority Priority { get; set; }
    internal CharacterLodState State { get; set; } = CharacterLodState.Excluded;
}

internal static class CharacterLodManager
{
    private static readonly int[] UpgradesPerUpdate = [2, 5, int.MaxValue];

    internal static float Cost(float distance, float heightAboveCamera, float kindFactor, float stanceFactor, float heightScale, float widthScale)
    {
        float heightPenalty = Math.Clamp((MathF.Abs(heightAboveCamera) - 800f) / 200f, 0f, 1f);
        float weighted = (heightPenalty + heightPenalty + 1f) * kindFactor * stanceFactor;
        float height = Math.Clamp(132f / heightScale, 0.5f, 2f);
        float width = Math.Clamp(13.2f / widthScale, 0.5f, 2f);
        float sizeFactor = ((height - 0.5f) * (2f / 3f) * 0.3f + 0.8f) * ((width - 0.5f) * (2f / 3f) * 0.3f + 0.8f);
        float range = distance;
        if (distance < 500f)
        {
            float near = distance / 500f * 0.5f * (distance / 500f * 0.5f);
            range = (near + near) * 250f + 250f;
        }
        return range * weighted * sizeFactor;
    }

    internal static void Order(List<CharacterLodEntry> entries) =>
        entries.Sort((left, right) => left.Priority != right.Priority ? right.Priority.CompareTo(left.Priority) : left.Cost.CompareTo(right.Cost));

    internal static void Assign(List<CharacterLodEntry> entries, CharacterLodBudget budget, Action<CharacterLodEntry, CharacterLodState> apply)
    {
        Order(entries);
        int[] capacity = [budget.Full, budget.Reduced, -1];
        int start = 0;
        for (int level = 0; level < 3; level++)
        {
            int end = capacity[level] == -1 ? entries.Count : Math.Min(start + capacity[level], entries.Count);
            int upgrades = 0;
            for (int index = start; index < end; index++)
            {
                var entry = entries[index];
                int current = (int)entry.State;
                if (current == level || (current > level && upgrades >= UpgradesPerUpdate[level])) continue;
                apply(entry, (CharacterLodState)level);
                entry.State = (CharacterLodState)level;
                if (level < current) upgrades++;
            }
            start = end;
        }
    }
}
