using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

internal readonly record struct Gw2BlendPlayingClip(ulong Token, float Seconds);

internal sealed class Gw2BlendTreePlayer : IGw2BlendTreeInputs
{
    private readonly Gw2BlendTreeRun _run;
    private readonly int[][] _children;
    private readonly ulong?[] _clipTokens;
    private readonly Gw2BlendWeightedOption[][] _options;
    private readonly float[][] _cooldowns;
    private readonly Dictionary<int, float> _leafSeconds = [];
    private readonly List<int> _leafOrder = [];
    private readonly Func<ulong, float?> _clipDuration;
    private readonly Random _random;

    internal Gw2BlendTreePlayer(Gw2BlendTree tree, Func<ulong, float?> clipDuration, Random random)
    {
        _run = Gw2BlendTreeRun.FromTree(tree);
        _clipDuration = clipDuration;
        _random = random;
        _children = [.. tree.Ops.Select(op => op.Children.ToArray())];
        _clipTokens = [.. tree.Ops.Select(op => op.ClipToken)];
        _options = [.. tree.Ops.Select(op => op.Options.ToArray())];
        _cooldowns = [.. _options.Select(options => new float[options.Length])];
    }

    internal static Gw2BlendTreePlayer? Of(Gw2BlendTree tree, Func<ulong, float?> clipDuration, Random random) =>
        tree.Root < 0 ? null : new Gw2BlendTreePlayer(tree, clipDuration, random);

    internal IEnumerable<ulong> ClipTokens => _clipTokens.OfType<ulong>().Where(token => token != 0).Distinct();

    internal IReadOnlyList<Gw2BlendPlayingClip> PlayingClips =>
        [.. _leafOrder.Where(leaf => _clipTokens[leaf] is not null and not 0).Select(leaf => new Gw2BlendPlayingClip(_clipTokens[leaf]!.Value, LoopedSeconds(leaf)))];

    internal void Start() => _run.Start(this);

    internal void Advance(float deltaSeconds)
    {
        foreach (float[] cooldowns in _cooldowns)
            for (int index = 0; index < cooldowns.Length; index++)
                if (cooldowns[index] > 0f) cooldowns[index] = Math.Max(cooldowns[index] - deltaSeconds, 0f);
        foreach (int leaf in _leafOrder) _leafSeconds[leaf] += deltaSeconds;
        _run.Advance(this);
    }

    public float Heading(uint vectorKind) => 0f;
    public bool MovementIsActive => false;
    public float MeasuredVectorLength => 0f;

    public bool TryReadParameter(uint parameter, out float value, out float rangeMinimum, out float rangeMaximum)
    {
        value = rangeMinimum = rangeMaximum = 0f;
        return false;
    }

    public bool AdvanceLeaf(int opIndex) =>
        _leafSeconds.TryGetValue(opIndex, out float seconds) && DurationOf(opIndex) is { } duration && seconds >= duration;

    public int? SelectChild(int opIndex)
    {
        if (_options[opIndex].Length == 0) return null;
        var state = _run.States[opIndex];
        if (state.Selected >= 0 && !_run.States[_children[opIndex][state.Selected]].Finished) return state.Selected;
        if (state.Selected >= 0) _cooldowns[opIndex][state.Selected] = _options[opIndex][state.Selected].CooldownSeconds;
        int picked = PickWeighted(opIndex) ?? Math.Max(state.Selected, 0);
        if (picked == state.Selected) RestartLeavesUnder(_children[opIndex][picked]);
        return picked;
    }

    private void RestartLeavesUnder(int opIndex)
    {
        if (_leafSeconds.ContainsKey(opIndex)) _leafSeconds[opIndex] = 0f;
        foreach (int child in _children[opIndex]) RestartLeavesUnder(child);
    }

    public float? CrossoverWeight(int opIndex) => null;

    public void StartLeaf(int opIndex)
    {
        _leafSeconds[opIndex] = 0f;
        if (!_leafOrder.Contains(opIndex)) _leafOrder.Add(opIndex);
    }

    public void StopLeaf(int opIndex)
    {
        _leafSeconds.Remove(opIndex);
        _leafOrder.Remove(opIndex);
    }

    private int? PickWeighted(int opIndex)
    {
        Gw2BlendWeightedOption[] options = _options[opIndex];
        float[] cooldowns = _cooldowns[opIndex];
        float total = 0f;
        for (int index = 0; index < options.Length; index++)
            if (cooldowns[index] == 0f) total += options[index].Weight;
        if (total <= 0f) return null;
        float target = (float)_random.NextDouble() * total;
        float cumulative = 0f;
        int last = -1;
        for (int index = 0; index < options.Length; index++)
        {
            if (cooldowns[index] != 0f) continue;
            cumulative += options[index].Weight;
            last = index;
            if (cumulative >= target) return index;
        }
        return last;
    }

    private float? DurationOf(int leaf) => _clipTokens[leaf] is { } token and not 0 ? _clipDuration(token) : null;

    private float LoopedSeconds(int leaf)
    {
        float seconds = _leafSeconds[leaf];
        return DurationOf(leaf) is { } duration and > 0f ? seconds % duration : seconds;
    }
}
