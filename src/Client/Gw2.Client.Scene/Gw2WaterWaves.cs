using System;
using System.Numerics;

namespace Gw2.Client.Scene;

public sealed class Gw2WaterWaves
{
    public const int TerrainScaleOneGridSize = 64;
    public const int TerrainScaleTwoGridSize = 128;
    private const float Gravity = 9.81f;
    private const float GaussianScale = 0.70710677f;
    private const float WindAlignmentExponent = 4f;
    private const float NearTaperExponent = 0.25f;
    private const float TaperBlendStartHeight = 500f;
    private const float TaperBlendRange = 1500f;
    private const int TaperZeroCells = 2;
    private const int TerrainScaleShift = 5;
    public const float LowQualityRefreshSeconds = 0.2f;

    private readonly int _size;
    private readonly Vector2[] _spectrum;
    private readonly Vector2[] _height;
    private readonly Vector2[] _displacementX;
    private readonly Vector2[] _displacementY;
    private readonly float[] _nearTaper;
    private readonly float[] _farTaper;
    private float _amplitude = float.PositiveInfinity;
    private Vector2 _wind;
    private float _choppiness;
    private float _time;
    private float _lastRefreshTime = float.NegativeInfinity;
    private readonly float[] _previousHeight = [];
    private readonly float[] _previousDisplacementX = [];
    private readonly float[] _previousDisplacementY = [];
    private readonly float[] _latestHeight = [];
    private readonly float[] _latestDisplacementX = [];
    private readonly float[] _latestDisplacementY = [];

    public Gw2WaterWaves(int size, bool lowQuality = false)
    {
        if (size < 4 || (size & (size - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(size));
        _size = size;
        _spectrum = new Vector2[size * size];
        _height = new Vector2[size * size];
        _displacementX = new Vector2[size * size];
        _displacementY = new Vector2[size * size];
        _nearTaper = new float[size];
        _farTaper = new float[size];
        for (var index = TaperZeroCells; index < size / 2; index++)
        {
            var far = MathF.Sin((index - 1) / (float)(size - 1) * MathF.PI);
            var near = MathF.Pow(far, NearTaperExponent);
            _nearTaper[index] = _nearTaper[size - index - 1] = near;
            _farTaper[index] = _farTaper[size - index - 1] = far;
        }
        Taper = new float[size];
        Height = new float[size * size];
        DisplacementX = new float[size * size];
        DisplacementY = new float[size * size];
        LowQuality = lowQuality;
        if (!lowQuality) return;
        _previousHeight = new float[size * size];
        _previousDisplacementX = new float[size * size];
        _previousDisplacementY = new float[size * size];
        _latestHeight = new float[size * size];
        _latestDisplacementX = new float[size * size];
        _latestDisplacementY = new float[size * size];
    }

    public bool LowQuality { get; }

    public int Size => _size;

    public float[] Taper { get; }

    public float[] Height { get; }

    public float[] DisplacementX { get; }

    public float[] DisplacementY { get; }

    public ReadOnlySpan<Vector2> Spectrum => _spectrum;

    public static int GridSizeForTilesPerChunk(int tilesPerChunk) => GridSizeOf(tilesPerChunk >> TerrainScaleShift);

    public static int GridSizeOf(int terrainScale) => terrainScale switch
    {
        1 => TerrainScaleOneGridSize,
        2 => TerrainScaleTwoGridSize,
        _ => throw new ArgumentOutOfRangeException(nameof(terrainScale)),
    };

    public void Advance(float elapsedSeconds, float animAmplitude, Vector2 animWind, float animChoppiness, float eyeHeightAboveSurface)
    {
        _time += elapsedSeconds;
        _choppiness = animChoppiness;
        if (animAmplitude != _amplitude || animWind != _wind)
        {
            _amplitude = animAmplitude;
            _wind = animWind;
            if (animWind == Vector2.Zero) Array.Clear(_spectrum);
            else BuildSpectrum();
        }
        BlendTaper(eyeHeightAboveSurface);
        if (_amplitude == 0f) return;
        if (!LowQuality)
        {
            Transform(Height, DisplacementX, DisplacementY);
            return;
        }
        if (_time - _lastRefreshTime > LowQualityRefreshSeconds)
        {
            _lastRefreshTime = _time;
            _latestHeight.CopyTo(_previousHeight, 0);
            _latestDisplacementX.CopyTo(_previousDisplacementX, 0);
            _latestDisplacementY.CopyTo(_previousDisplacementY, 0);
            Transform(_latestHeight, _latestDisplacementX, _latestDisplacementY);
        }
        var blend = Math.Clamp((_time - _lastRefreshTime) / LowQualityRefreshSeconds, 0f, 1f);
        Lerp(_previousHeight, _latestHeight, blend, Height);
        Lerp(_previousDisplacementX, _latestDisplacementX, blend, DisplacementX);
        Lerp(_previousDisplacementY, _latestDisplacementY, blend, DisplacementY);
    }

    private static void Lerp(float[] previous, float[] latest, float blend, float[] output)
    {
        for (var index = 0; index < output.Length; index++) output[index] = previous[index] * (1f - blend) + latest[index] * blend;
    }

    private void Transform(float[] height, float[] displacementX, float[] displacementY)
    {
        Evolve();
        InverseTransform(_height);
        Store(_height, height, 1f);
        if (_choppiness > 0f)
        {
            InverseTransform(_displacementX);
            InverseTransform(_displacementY);
        }
        var displacementScale = -Math.Clamp(_choppiness, 0f, 1f);
        Store(_displacementX, displacementX, _choppiness > 0f ? displacementScale : 0f);
        Store(_displacementY, displacementY, _choppiness > 0f ? displacementScale : 0f);
    }

    public static Vector2 GridOrigin(Vector2 eye, Vector2 viewDirection, float halfExtent, float cellSize)
    {
        var lead = viewDirection == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(viewDirection) * halfExtent * 0.9f;
        return Snap(Snap(lead, cellSize) + eye, cellSize);
    }

    public static int FieldOffset(float origin, float cellSize, int size) => (size << 10) + (int)MathF.Round(origin / cellSize, MidpointRounding.ToEven);

    public float WaveWeight(int row, int column, float depthWeight) => Math.Clamp(depthWeight, 0f, 1f) * Taper[row] * Taper[column];

    public byte Alpha(int row, int column, float depthWeight)
    {
        var taper = Taper[row] * Taper[column];
        return (byte)Math.Clamp((int)MathF.Round((1f - taper + WaveWeight(row, column, depthWeight)) * 255f), 0, 255);
    }

    public int FieldIndex(int row, int column, int offsetX, int offsetY) => (row + offsetY) % _size * _size + (column + offsetX) % _size;

    private static Vector2 Snap(Vector2 value, float cellSize) =>
        new(value.X - value.X % cellSize, value.Y - value.Y % cellSize);

    private void BlendTaper(float eyeHeightAboveSurface)
    {
        var blend = Math.Clamp((eyeHeightAboveSurface - TaperBlendStartHeight) / TaperBlendRange, 0f, 1f);
        for (var index = 0; index < _size; index++)
            Taper[index] = (_farTaper[index] - _nearTaper[index]) * blend + _nearTaper[index];
    }

    private void BuildSpectrum()
    {
        var random = new MsvcRandom(0);
        var step = 2f * MathF.PI / _size;
        var start = -(_size * 0.5f) * step;
        var windLengthSquared = _wind.LengthSquared();
        var largestWave = windLengthSquared / Gravity;
        var windDirection = windLengthSquared >= 1e-12f ? _wind / MathF.Sqrt(windLengthSquared) : Vector2.Zero;
        for (var row = 0; row < _size; row++)
        for (var column = 0; column < _size; column++)
        {
            var wave = new Vector2(column * step + start, row * step + start);
            var lengthSquared = wave.LengthSquared();
            var power = 0f;
            if (lengthSquared != 0f)
            {
                var direction = lengthSquared >= 1e-12f ? wave / MathF.Sqrt(lengthSquared) : Vector2.Zero;
                var alignment = MathF.Pow(Vector2.Dot(direction, windDirection) * 0.5f + 0.5f, WindAlignmentExponent);
                power = alignment * (MathF.Exp(-1f / (largestWave * lengthSquared * largestWave)) / (lengthSquared * lengthSquared) * _amplitude);
            }
            var amplitude = MathF.Sqrt(power) * GaussianScale;
            var gaussian = random.GaussianPair();
            _spectrum[row * _size + column] = gaussian * amplitude;
        }
    }

    private void Evolve()
    {
        var step = 2f * MathF.PI / _size;
        var start = -(_size * 0.5f) * step;
        for (var row = 0; row < _size; row++)
        for (var column = 0; column < _size; column++)
        {
            var waveX = column * step + start;
            var waveY = row * step + start;
            var length = MathF.Sqrt(waveX * waveX + waveY * waveY);
            var phase = MathF.Sqrt(length * Gravity) * _time;
            var cos = MathF.Cos(phase);
            var sin = MathF.Sin(phase);
            var index = row * _size + column;
            var mirrored = _spectrum[(_size - row) % _size * _size + (_size - column) % _size];
            var forward = _spectrum[index];
            var height = new Vector2(
                forward.X * cos - forward.Y * sin + (cos * mirrored.X - mirrored.Y * sin),
                forward.Y * cos + forward.X * sin + (-mirrored.Y * cos - sin * mirrored.X));
            _height[index] = height;
            if (length == 0f)
            {
                _displacementX[index] = Vector2.Zero;
                _displacementY[index] = Vector2.Zero;
                continue;
            }
            var directionX = waveX / length;
            var directionY = waveY / length;
            _displacementX[index] = new Vector2(directionX * height.Y, -directionX * height.X);
            _displacementY[index] = new Vector2(directionY * height.Y, -directionY * height.X);
        }
    }

    private void InverseTransform(Vector2[] field)
    {
        var line = new Vector2[_size];
        for (var row = 0; row < _size; row++)
        {
            for (var column = 0; column < _size; column++) line[column] = field[row * _size + column];
            InverseTransformLine(line);
            for (var column = 0; column < _size; column++) field[row * _size + column] = line[column];
        }
        for (var column = 0; column < _size; column++)
        {
            for (var row = 0; row < _size; row++) line[row] = field[row * _size + column];
            InverseTransformLine(line);
            for (var row = 0; row < _size; row++) field[row * _size + column] = line[row];
        }
    }

    private static void InverseTransformLine(Vector2[] line)
    {
        var count = line.Length;
        for (int index = 1, reversed = 0; index < count; index++)
        {
            var bit = count >> 1;
            for (; (reversed & bit) != 0; bit >>= 1) reversed ^= bit;
            reversed ^= bit;
            if (index < reversed) (line[index], line[reversed]) = (line[reversed], line[index]);
        }
        for (var span = 2; span <= count; span <<= 1)
        {
            var angle = 2f * MathF.PI / span;
            for (var start = 0; start < count; start += span)
            for (var offset = 0; offset < span / 2; offset++)
            {
                var twiddle = new Vector2(MathF.Cos(angle * offset), MathF.Sin(angle * offset));
                var odd = line[start + offset + span / 2];
                var rotated = new Vector2(odd.X * twiddle.X - odd.Y * twiddle.Y, odd.X * twiddle.Y + odd.Y * twiddle.X);
                var even = line[start + offset];
                line[start + offset] = even + rotated;
                line[start + offset + span / 2] = even - rotated;
            }
        }
    }

    private void Store(Vector2[] field, float[] output, float scale)
    {
        for (var row = 0; row < _size; row++)
        for (var column = 0; column < _size; column++)
        {
            var index = row * _size + column;
            output[index] = ((row + column) & 1) == 0 ? field[index].X * scale : -field[index].X * scale;
        }
    }

    private struct MsvcRandom(uint seed)
    {
        private uint _state = seed;

        public int Next()
        {
            _state = _state * 214013u + 2531011u;
            return (int)((_state >> 16) & 0x7FFF);
        }

        public Vector2 GaussianPair()
        {
            float first, second, radiusSquared;
            do
            {
                var a = Next();
                first = (a + (float)a) / 32767f - 1f;
                var b = Next();
                second = (b + (float)b) / 32767f - 1f;
                radiusSquared = second * second + first * first;
            }
            while (radiusSquared >= 1f);
            var scale = MathF.Sqrt(MathF.Log(radiusSquared) * -2f / radiusSquared);
            return new Vector2(scale * first, scale * second);
        }
    }
}
