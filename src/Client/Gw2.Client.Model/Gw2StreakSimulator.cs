using System.Numerics;

namespace Gw2.Client.Model;

public sealed class Gw2StreakRow
{
    public Gw2StreakRow(Vector3[] positions, float textureU)
    {
        Positions = positions;
        TextureU = textureU;
    }

    public Vector3[] Positions { get; }
    public float TextureU { get; }
    public float AgeSeconds { get; set; }
}

public sealed class Gw2StreakSimulator
{
    public const float MaximumStepSeconds = 0.1f;
    public const int MaximumRows = 256;

    private readonly float _spawnDistance;
    private readonly float _textureScale;
    private readonly float _lifetimeSeconds;
    private readonly List<Gw2StreakRow> _rows = new();
    private Vector3[]? _lastPositions;
    private float _travelledSinceRow;
    private float _textureU;

    public Gw2StreakSimulator(float spawnDistance, float textureScale, float lifetimeSeconds)
    {
        _spawnDistance = spawnDistance > 0f ? spawnDistance : 1f;
        _textureScale = textureScale > 0f ? textureScale : 1f;
        _lifetimeSeconds = lifetimeSeconds > 0f ? lifetimeSeconds : 0.5f;
    }

    public IReadOnlyList<Gw2StreakRow> Rows => _rows;
    public float LifetimeSeconds => _lifetimeSeconds;

    public void Advance(float deltaSeconds, bool isSpawning, Vector3[] anchorPositions)
    {
        if (anchorPositions is null) throw new ArgumentNullException(nameof(anchorPositions));
        var step = Math.Min(deltaSeconds, MaximumStepSeconds);
        foreach (var row in _rows) row.AgeSeconds += step;
        _rows.RemoveAll(row => row.AgeSeconds >= _lifetimeSeconds);

        if (!isSpawning)
        {
            _lastPositions = null;
            _travelledSinceRow = 0f;
            return;
        }

        if (_lastPositions is null || _lastPositions.Length != anchorPositions.Length)
        {
            _lastPositions = (Vector3[])anchorPositions.Clone();
            AddRow(anchorPositions);
            return;
        }

        var travelled = 0f;
        for (var anchor = 0; anchor < anchorPositions.Length; anchor++)
            travelled = MathF.Max(travelled, Vector3.Distance(_lastPositions[anchor], anchorPositions[anchor]));
        if (travelled <= 0f) return;

        var from = _lastPositions;
        var remaining = travelled;
        var covered = 0f;
        while (_travelledSinceRow + remaining >= _spawnDistance && _rows.Count < MaximumRows)
        {
            var needed = _spawnDistance - _travelledSinceRow;
            covered += needed;
            remaining -= needed;
            _travelledSinceRow = 0f;
            _textureU += _spawnDistance / _textureScale;
            AddRow(Interpolate(from, anchorPositions, covered / travelled));
        }

        _travelledSinceRow += remaining;
        _lastPositions = (Vector3[])anchorPositions.Clone();
    }

    public float AlphaOf(Gw2StreakRow row) => Math.Clamp(1f - row.AgeSeconds / _lifetimeSeconds, 0f, 1f);

    private void AddRow(Vector3[] positions)
    {
        _rows.Add(new Gw2StreakRow((Vector3[])positions.Clone(), _textureU));
        if (_rows.Count > MaximumRows) _rows.RemoveAt(0);
    }

    private static Vector3[] Interpolate(Vector3[] from, Vector3[] to, float blend)
    {
        var result = new Vector3[to.Length];
        for (var anchor = 0; anchor < to.Length; anchor++) result[anchor] = Vector3.Lerp(from[anchor], to[anchor], blend);
        return result;
    }
}
