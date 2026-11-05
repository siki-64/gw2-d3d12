using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gw2.Client.Graphics;

public sealed class GrMaterialConstantsComparer : IEqualityComparer<IReadOnlyDictionary<uint, Vector4>>
{
    public static readonly GrMaterialConstantsComparer Instance = new();

    public bool Equals(IReadOnlyDictionary<uint, Vector4>? left, IReadOnlyDictionary<uint, Vector4>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Count != right.Count) return false;
        foreach (var (token, value) in left)
            if (!right.TryGetValue(token, out var other) || other != value) return false;
        return true;
    }

    public int GetHashCode(IReadOnlyDictionary<uint, Vector4> constants)
    {
        var hash = constants.Count;
        foreach (var (token, value) in constants) hash ^= HashCode.Combine(token, value);
        return hash;
    }
}
