using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public sealed class Gw2ScatterActivation
{
    public const float DefaultRadius = 15360f;

    private readonly float _radius;
    private readonly Vector2 _origin;
    private readonly HashSet<(int X, int Y)> _active = new();
    private (int MinX, int MinY, int EndX, int EndY)? _rect;

    public Gw2ScatterActivation(Vector2 origin, float radius = DefaultRadius)
    {
        _origin = origin;
        _radius = radius;
    }

    public bool IsActive(int blockX, int blockY) => _active.Contains((blockX, blockY));

    public bool Update(Vector3 view, float viewDistance, ICollection<(int X, int Y)> released, ICollection<(int X, int Y)> activated)
    {
        var radius = MathF.Min(_radius, viewDistance);
        var local = new Vector2(view.X, view.Y) - _origin;
        var rect = ((int)MathF.Floor((local.X - radius) / Gw2ZoneScatterFade.BlockSize), (int)MathF.Floor((local.Y - radius) / Gw2ZoneScatterFade.BlockSize),
            (int)MathF.Ceiling((local.X + radius) / Gw2ZoneScatterFade.BlockSize), (int)MathF.Ceiling((local.Y + radius) / Gw2ZoneScatterFade.BlockSize));
        if (_rect == rect) return false;
        _rect = rect;
        var next = new HashSet<(int X, int Y)>();
        for (var y = rect.Item2; y < rect.Item4; y++)
        for (var x = rect.Item1; x < rect.Item3; x++)
            next.Add((x, y));
        foreach (var block in _active) if (!next.Contains(block)) released.Add(block);
        foreach (var block in next) if (!_active.Contains(block)) activated.Add(block);
        _active.Clear();
        _active.UnionWith(next);
        return true;
    }
}
