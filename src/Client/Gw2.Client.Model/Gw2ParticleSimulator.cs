using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Model;

public struct Gw2Particle
{
    public Vector3 PositionInGameUnits;
    public Vector3 VelocityInGameUnits;
    public Vector3 AccelerationInGameUnits;
    public float Width;
    public float Height;
    public float WidthChange;
    public float HeightChange;
    public float RotationRadians;
    public float RotationChangeRadians;
    public float AgeSeconds;
    public float LifetimeSeconds;
    public byte RandomByte;

    public float AgeFraction => LifetimeSeconds <= 0f ? 1f : Math.Clamp(AgeSeconds / LifetimeSeconds, 0f, 1f);
}

public sealed class Gw2ParticleSimulator
{
    public const uint WorldSpaceFlag = 0x10;
    public const uint RandomRotationDirectionFlag = 0x20;
    public const uint OpacityPresetInvertedFlag = 0x80;
    public const uint ScalePresetInvertedFlag = 0x400;
    public const uint UniformScaleFlag = 0x800;
    public const uint RandomFrameFlag = 0x100;
    public const uint SmoothFlipbookFlag = 0x20000;
    public const uint RandomTexCoordsFlag = 0x40000;
    public const uint BurstEmitterFlag = 0x8;
    public const int MaximumParticles = 2048;

    private const float DegreesToRadians = MathF.PI / 180f;

    private readonly Gw2ParticleEmitter _emitter;
    private readonly Random _random;
    private readonly List<Gw2Particle> _particles = new();
    private float _spawnAccumulatorSeconds;
    private bool _wasVisible;
    private bool _burstPending;

    public Gw2ParticleSimulator(Gw2ParticleEmitter emitter, Random random)
    {
        _emitter = emitter ?? throw new ArgumentNullException(nameof(emitter));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public Gw2ParticleEmitter Emitter => _emitter;
    public IReadOnlyList<Gw2Particle> Particles => _particles;
    public bool IsWorldSpace => (_emitter.Flags & WorldSpaceFlag) != 0;
    public bool IsBurst => (_emitter.EmitterFlags & BurstEmitterFlag) != 0;

    public void Advance(float deltaSeconds, bool isVisible, Matrix4x4 emitterToSimulation)
    {
        if (deltaSeconds <= 0f) return;
        if (isVisible && !_wasVisible)
        {
            _spawnAccumulatorSeconds = 0f;
            if (IsBurst) _burstPending = true;
        }

        _wasVisible = isVisible;
        AgeParticles(deltaSeconds);
        if (!isVisible) return;
        if (IsBurst) SpawnBurst(emitterToSimulation);
        else Spawn(deltaSeconds, emitterToSimulation);
    }

    public Vector4 ColourOf(in Gw2Particle particle)
    {
        var period = _emitter.ColorPeriod > 0f ? _emitter.ColorPeriod : 1f;
        var colourBlend = Gw2ParticleEasing.Remap(_emitter.ColorFalloff.Minimum, _emitter.ColorFalloff.Maximum, particle.AgeFraction / period, 0f, 1f);
        var colour = Vector4.Lerp(_emitter.ColorBegin, _emitter.ColorEnd, colourBlend);
        var opacity = Math.Clamp(Gw2ParticleEasing.Evaluate(_emitter.OpacityCurve, _emitter.OpacityCurvePreset,
            (_emitter.Flags & OpacityPresetInvertedFlag) != 0, particle.AgeFraction), 0f, 1f);
        return colour with { W = colour.W * opacity };
    }

    public float ScaleOf(in Gw2Particle particle) =>
        MathF.Max(0f, Gw2ParticleEasing.Evaluate(_emitter.ScaleCurve, _emitter.ScaleCurvePreset,
            (_emitter.Flags & ScalePresetInvertedFlag) != 0, particle.AgeFraction));

    public int FrameOf(in Gw2Particle particle, int framesAhead = 0)
    {
        if (_emitter.Flipbook is not { } flipbook) return 0;
        var frame = (int)MathF.Floor(MathF.Max(0f, particle.AgeSeconds * flipbook.FramesPerSecond)) + framesAhead;
        if ((_emitter.Flags & RandomFrameFlag) != 0) frame += particle.RandomByte;
        var count = (int)Math.Max(1u, flipbook.FrameCount);
        var cells = (int)Math.Max(1u, flipbook.Columns * flipbook.Rows);
        return (int)((flipbook.StartFrame + (uint)(frame % count)) % (uint)cells);
    }

    public float FrameBlendOf(in Gw2Particle particle)
    {
        if (_emitter.Flipbook is not { } flipbook) return 0f;
        var position = particle.AgeSeconds * flipbook.FramesPerSecond;
        return position - MathF.Truncate(position);
    }

    private void AgeParticles(float deltaSeconds)
    {
        var dragFactor = MathF.Max(0f, 1f - _emitter.Drag * deltaSeconds);
        for (var index = _particles.Count - 1; index >= 0; index--)
        {
            var particle = _particles[index];
            particle.AgeSeconds += deltaSeconds;
            if (particle.AgeSeconds >= particle.LifetimeSeconds)
            {
                _particles[index] = _particles[^1];
                _particles.RemoveAt(_particles.Count - 1);
                continue;
            }

            particle.VelocityInGameUnits = (particle.VelocityInGameUnits + particle.AccelerationInGameUnits * deltaSeconds) * dragFactor;
            particle.PositionInGameUnits += particle.VelocityInGameUnits * deltaSeconds;
            particle.Width += particle.WidthChange * deltaSeconds;
            particle.Height += particle.HeightChange * deltaSeconds;
            particle.RotationRadians += particle.RotationChangeRadians * deltaSeconds;
            _particles[index] = particle;
        }
    }

    private void SpawnBurst(Matrix4x4 emitterToSimulation)
    {
        if (!_burstPending || _particles.Count > 0) return;
        _burstPending = false;
        SpawnGroup(emitterToSimulation);
    }

    private void SpawnGroup(Matrix4x4 emitterToSimulation)
    {
        if (NextUnit() * 100f > _emitter.SpawnProbabilityPercent) return;
        var groupSize = (int)MathF.Round(Sample(_emitter.SpawnGroupSize));
        for (var member = 0; member < groupSize && _particles.Count < MaximumParticles; member++)
            _particles.Add(CreateParticle(emitterToSimulation));
    }

    private void Spawn(float deltaSeconds, Matrix4x4 emitterToSimulation)
    {
        if (_emitter.SpawnPeriodSeconds <= 0f) return;
        _spawnAccumulatorSeconds += deltaSeconds;
        while (_spawnAccumulatorSeconds >= _emitter.SpawnPeriodSeconds)
        {
            _spawnAccumulatorSeconds -= _emitter.SpawnPeriodSeconds;
            SpawnGroup(emitterToSimulation);
        }
    }

    private Gw2Particle CreateParticle(Matrix4x4 emitterToSimulation)
    {
        var uniformScale = (_emitter.Flags & UniformScaleFlag) != 0;
        var width = Sample(RangeAt(_emitter.ScaleInitial, 0));
        var widthChange = Sample(RangeAt(_emitter.ScaleChange, 0));
        var rotationSign = (_emitter.Flags & RandomRotationDirectionFlag) != 0 ? NextUnit() * 2f - 1f : 1f;

        return new Gw2Particle
        {
            PositionInGameUnits = Vector3.Transform(SpawnOffset(), emitterToSimulation),
            VelocityInGameUnits = Vector3.TransformNormal(DirectedMagnitude(_emitter.Velocity), emitterToSimulation),
            AccelerationInGameUnits = Vector3.TransformNormal(DirectedMagnitude(_emitter.Acceleration), emitterToSimulation),
            Width = width,
            Height = uniformScale ? width : Sample(RangeAt(_emitter.ScaleInitial, 1)),
            WidthChange = widthChange,
            HeightChange = uniformScale ? widthChange : Sample(RangeAt(_emitter.ScaleChange, 1)),
            RotationRadians = Sample(_emitter.RotationInitialDegrees) * DegreesToRadians,
            RotationChangeRadians = Sample(_emitter.RotationChangeDegrees) * DegreesToRadians * rotationSign,
            LifetimeSeconds = MathF.Max(0.001f, Sample(_emitter.LifetimeSeconds)),
            RandomByte = (byte)_random.Next(256),
        };
    }

    private Vector3 SpawnOffset()
    {
        var radius = _emitter.SpawnRadius;
        if (radius.Minimum == 0f && radius.Maximum == 0f) return Vector3.Zero;
        var angle = NextUnit() * (MathF.PI * 2f);
        switch (_emitter.SpawnShape)
        {
            case 0:
                return new Vector3(MathF.Cos(angle) * radius.Minimum, MathF.Sin(angle) * radius.Minimum,
                    -NextUnit() * (radius.Minimum + radius.Maximum));
            case 1:
                var distance = Sample(radius);
                return new Vector3(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0f);
            case 2:
                return new Vector3(NextUnit() * (radius.Minimum + radius.Maximum),
                    MathF.Cos(angle) * radius.Minimum, MathF.Sin(angle) * radius.Minimum);
            case 3:
                return new Vector3((radius.Minimum + radius.Maximum) * (NextUnit() * 2f - 1f),
                    radius.Minimum * (NextUnit() * 2f - 1f), 0f);
            default:
                return RandomDirection() * Sample(radius);
        }
    }

    private Vector3 DirectedMagnitude(IReadOnlyList<Gw2ParticleRange> components)
    {
        if (components.Count < 4) return Vector3.Zero;
        var direction = new Vector3(Sample(components[0]), Sample(components[1]), Sample(components[2]));
        var length = direction.Length();
        if (length <= 0f) return Vector3.Zero;
        return direction / length * Sample(components[3]);
    }

    private Vector3 RandomDirection()
    {
        var direction = new Vector3(NextUnit() * 2f - 1f, NextUnit() * 2f - 1f, NextUnit() * 2f - 1f);
        var length = direction.Length();
        return length <= 0f ? Vector3.UnitX : direction / length;
    }

    private float Sample(Gw2ParticleRange range) => range.Minimum + (range.Maximum - range.Minimum) * NextUnit();

    private float NextUnit() => (float)_random.NextDouble();

    private static Gw2ParticleRange RangeAt(IReadOnlyList<Gw2ParticleRange> ranges, int index) =>
        index < ranges.Count ? ranges[index] : default;
}
