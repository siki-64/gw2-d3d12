using System;
using System.Collections.Generic;

namespace Gw2.Client.Graphics;

public sealed class GrGpuSceneGroups<TKey> where TKey : notnull
{
    public const int NoGroup = -1;

    private GrGpuSceneGroups(List<TKey> keys, int[] first, int[] capacity, int[] groupOfInstance)
    {
        Keys = keys;
        First = first;
        Capacity = capacity;
        GroupOfInstance = groupOfInstance;
    }

    public IReadOnlyList<TKey> Keys { get; }

    public IReadOnlyList<int> First { get; }

    public IReadOnlyList<int> Capacity { get; }

    public IReadOnlyList<int> GroupOfInstance { get; }

    public int Count => Keys.Count;

    public static GrGpuSceneGroups<TKey> Build(int instanceCount, IEnumerable<(int Instance, TKey Key)> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var keys = new List<TKey>();
        var groupOfKey = new Dictionary<TKey, int>();
        var groupOfInstance = new int[instanceCount];
        Array.Fill(groupOfInstance, NoGroup);
        var capacity = new List<int>();
        foreach (var (instance, key) in members)
        {
            if (!groupOfKey.TryGetValue(key, out var group))
            {
                groupOfKey[key] = group = keys.Count;
                keys.Add(key);
                capacity.Add(0);
            }
            groupOfInstance[instance] = group;
            capacity[group]++;
        }

        var first = new int[keys.Count];
        for (var group = 1; group < keys.Count; group++) first[group] = first[group - 1] + capacity[group - 1];
        return new GrGpuSceneGroups<TKey>(keys, first, capacity.ToArray(), groupOfInstance);
    }
}
