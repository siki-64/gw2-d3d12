using System;
using System.Collections.Generic;
using System.Numerics;
using Gw2.Client.Model;

namespace Gw2.Client.Scene;

public sealed class Gw2CharacterAnimator
{
    private sealed class Layer(Gw2LocomotionState? state, float seconds)
    {
        public Gw2LocomotionState? State { get; } = state;
        public float Seconds { get; set; } = seconds;
        public float ShareBeforeFade { get; set; }
        public float Rate { get; set; } = 1f;
    }

    private readonly List<Layer> _fadingOut = [];
    private Layer _current = new(null, 0f);
    private float _fadeSeconds;
    private float _fadeLength;
    private Quaternion _rotationBeforeFade = Quaternion.Identity;

    public int StateId { get; private set; } = Gw2CharacterLocomotion.GroundIdle;

    public Quaternion ModelRotation { get; private set; } = Quaternion.Identity;

    public Gw2MoveDirection MoveDirection { get; set; }

    public float FadeWeight => _fadeLength <= 0f ? 1f : Math.Clamp(_fadeSeconds / _fadeLength, 0f, 1f);

    public void Play(int stateId, Gw2LocomotionState? state, float idleBlendInSeconds)
    {
        if (stateId == StateId) return;
        StateId = stateId;
        var weight = FadeWeight;
        foreach (var layer in _fadingOut) layer.ShareBeforeFade *= 1f - weight;
        _current.ShareBeforeFade = weight;
        _fadingOut.Add(_current);
        _fadingOut.RemoveAll(layer => layer.ShareBeforeFade <= 0f);

        var running = _current.State;
        var keepsTime = state is { KeepsClipTime: true } && running is { Clip.Duration: > 0f };
        var seconds = keepsTime ? _current.Seconds / running!.Clip.Duration * state!.Clip.Duration : 0f;
        _current = new Layer(state, seconds);
        _rotationBeforeFade = ModelRotation;
        _fadeLength = state?.BlendInSeconds ?? idleBlendInSeconds;
        _fadeSeconds = 0f;
    }

    public void Advance(float elapsedSeconds, float groundSpeed)
    {
        _fadeSeconds += elapsedSeconds;
        if (_current.State is { } state) _current.Rate = state.Clip.MoveSpeed > 0f ? groundSpeed / state.Clip.MoveSpeed : 1f;
        AdvanceClip(_current, elapsedSeconds);
        foreach (var layer in _fadingOut) AdvanceClip(layer, elapsedSeconds);
        if (FadeWeight >= 1f) _fadingOut.Clear();
        ModelRotation = Quaternion.Slerp(_rotationBeforeFade, _current.State?.Rotation ?? Quaternion.Identity, FadeWeight);
    }

    public IReadOnlyList<Gw2WeightedClipSample> Samples(Gw2AnimationClip? idleClip, float idleSeconds)
    {
        var weight = FadeWeight;
        var samples = new List<Gw2WeightedClipSample>(_fadingOut.Count + 1);
        foreach (var layer in _fadingOut)
            if (SampleOf(layer, idleClip, idleSeconds, layer.ShareBeforeFade * (1f - weight)) is { Weight: > 0f } sample) samples.Add(sample);
        if (SampleOf(_current, idleClip, idleSeconds, weight) is { } current) samples.Add(current);
        return samples;
    }

    private static Gw2WeightedClipSample? SampleOf(Layer layer, Gw2AnimationClip? idleClip, float idleSeconds, float weight) =>
        layer.State is { } state ? new Gw2WeightedClipSample(state.Clip, layer.Seconds, weight)
        : idleClip is not null ? new Gw2WeightedClipSample(idleClip, idleSeconds, weight)
        : null;

    private static void AdvanceClip(Layer layer, float elapsedSeconds)
    {
        if (layer.State is not { Clip.Duration: > 0f } state) return;
        layer.Seconds = (layer.Seconds + elapsedSeconds * layer.Rate) % state.Clip.Duration;
    }
}
