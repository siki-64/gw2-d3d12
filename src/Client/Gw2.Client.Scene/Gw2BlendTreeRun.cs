using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

internal interface IGw2BlendTreeInputs
{
    float Heading(uint vectorKind);
    bool MovementIsActive { get; }
    float MeasuredVectorLength { get; }
    bool TryReadParameter(uint parameter, out float value, out float rangeMinimum, out float rangeMaximum);
    bool AdvanceLeaf(int opIndex);
    int? SelectChild(int opIndex);
    float? CrossoverWeight(int opIndex);
    void StartLeaf(int opIndex);
    void StopLeaf(int opIndex);
}

internal sealed class Gw2BlendTreeRun
{
    internal const float SecondsPerMillisecond = 0.001f;
    internal const int IntroBankOffset = 2;

    internal static bool Crosses(Gw2BlendOpType type) => type is Gw2BlendOpType.Crossover;
    internal static bool PicksWeighted(Gw2BlendOpType type) => type is Gw2BlendOpType.WeightedRandom or Gw2BlendOpType.WeightedRandomOptionsFirst;

    internal static bool SelectsOnce(Gw2BlendOpType type) => type is Gw2BlendOpType.Unknown10 or Gw2BlendOpType.Unknown17 or Gw2BlendOpType.Unknown24;
    internal static bool Reselects(Gw2BlendOpType type) => type is Gw2BlendOpType.Unknown12 or Gw2BlendOpType.Unknown16 or Gw2BlendOpType.Unknown25;
    internal static bool ReselectsWithIntro(Gw2BlendOpType type) => type is Gw2BlendOpType.Unknown22 or Gw2BlendOpType.Unknown23 or Gw2BlendOpType.Unknown26;

    private readonly Gw2BlendOpType[] _types;
    private readonly int[][] _children;
    private readonly Gw2BlendComparison?[] _comparisons;
    private readonly uint[][] _vectorKinds;
    private readonly float[] _lengthThresholds;
    private readonly Gw2BlendOpState[] _states;

    internal Gw2BlendTreeRun(Gw2BlendOpType[] types, int[][] children, int root, float treeBlendTime,
        Gw2BlendComparison?[] comparisons, uint[][] vectorKinds, float[] lengthThresholds)
    {
        _types = types;
        _children = children;
        _comparisons = comparisons;
        _vectorKinds = vectorKinds;
        _lengthThresholds = lengthThresholds;
        Root = root;
        TreeBlendTime = treeBlendTime;
        _states = new Gw2BlendOpState[types.Length];
        for (int index = 0; index < _states.Length; index++) _states[index] = new Gw2BlendOpState();
    }

    internal int Root { get; }
    internal float TreeBlendTime { get; }
    internal float? TriggeredBlendTime { get; private set; }
    internal IReadOnlyList<Gw2BlendOpState> States => _states;

    internal static Gw2BlendTreeRun FromTree(Gw2BlendTree tree)
    {
        var types = tree.Ops.Select(op => op.Type).ToArray();
        var children = tree.Ops.Select(op => op.Children.ToArray()).ToArray();
        var comparisons = tree.Ops.Select(op => op.Comparison).ToArray();
        var vectorKinds = tree.Ops.Select(op => op.VectorKinds.ToArray()).ToArray();
        var lengthThresholds = tree.Ops.Select(op => op.LengthThreshold).ToArray();
        return new Gw2BlendTreeRun(types, children, tree.Root, tree.BlendTimeSeconds, comparisons, vectorKinds, lengthThresholds);
    }

    internal void Start(IGw2BlendTreeInputs inputs) => Start(Root, inputs);

    internal bool Advance(IGw2BlendTreeInputs inputs) => Advance(Root, inputs);

    private void Start(int opIndex, IGw2BlendTreeInputs inputs)
    {
        var state = _states[opIndex];
        state.Stopped = false;
        state.Finished = false;
        state.Selected = -1;
        state.PlaysIntro = false;
        int[] children = _children[opIndex];
        switch (_types[opIndex])
        {
            case Gw2BlendOpType.Parallel:
            case Gw2BlendOpType.TwoSidedCrossover:
            case Gw2BlendOpType.Unknown15:
                foreach (int child in children) Start(child, inputs);
                break;
            case Gw2BlendOpType.Unknown13:
                state.Selected = inputs.SelectChild(opIndex) ?? 0;
                foreach (int child in children) Start(child, inputs);
                break;
            case Gw2BlendOpType.Selector:
                state.Selected = Gw2BlendTreeRules.SelectorChildIndex(_vectorKinds[opIndex][0], inputs.MovementIsActive, inputs.MeasuredVectorLength, _lengthThresholds[opIndex]);
                Start(children[state.Selected], inputs);
                break;
            case Gw2BlendOpType.DirectionSelect:
                state.Selected = DirectionOf(opIndex, inputs);
                Start(children[state.Selected], inputs);
                break;
            case Gw2BlendOpType.Comparison:
                Start(children[0], inputs);
                break;
            case var crossing when Crosses(crossing):
                state.Weight = inputs.CrossoverWeight(opIndex) ?? 0f;
                foreach (int child in children) Start(child, inputs);
                break;
            case var once when PicksWeighted(once) || SelectsOnce(once) || Reselects(once) || ReselectsWithIntro(once):
                state.Selected = inputs.SelectChild(opIndex) ?? 0;
                Start(children[state.Selected], inputs);
                break;
            default:
                if (children.Length == 0) inputs.StartLeaf(opIndex);
                else Start(children[0], inputs);
                break;
        }
    }

    private void Stop(int opIndex, IGw2BlendTreeInputs inputs)
    {
        var state = _states[opIndex];
        if (state.Stopped) return;
        state.Stopped = true;
        if (_children[opIndex].Length == 0) inputs.StopLeaf(opIndex);
        else foreach (int child in _children[opIndex]) Stop(child, inputs);
    }

    private bool Advance(int opIndex, IGw2BlendTreeInputs inputs)
    {
        var state = _states[opIndex];
        if (state.Stopped) return true;
        bool finished = AdvanceOp(opIndex, state, inputs);
        state.Finished = finished;
        return finished;
    }

    private bool AdvanceOp(int opIndex, Gw2BlendOpState state, IGw2BlendTreeInputs inputs)
    {
        int[] children = _children[opIndex];
        switch (_types[opIndex])
        {
            case Gw2BlendOpType.Parallel:
            case Gw2BlendOpType.Unknown15:
                bool first = Advance(children[0], inputs);
                return Advance(children[1], inputs) && first;
            case Gw2BlendOpType.TwoSidedCrossover:
                bool anyFinished = false;
                foreach (int child in children) anyFinished |= Advance(child, inputs);
                return anyFinished;
            case Gw2BlendOpType.Unknown13:
                return AdvanceStepped(opIndex, state, inputs, children);
            case Gw2BlendOpType.DirectionSelect:
                int direction = DirectionOf(opIndex, inputs);
                if (direction != state.Selected)
                {
                    if (state.Selected >= 0) Stop(children[state.Selected], inputs);
                    state.Selected = direction;
                    Start(children[direction], inputs);
                }
                return Advance(children[direction], inputs);
            case var selecting when selecting == Gw2BlendOpType.Selector || SelectsOnce(selecting):
                return state.Selected < 0 || Advance(children[state.Selected], inputs);
            case var reselecting when Reselects(reselecting):
                Reselect(opIndex, state, inputs, children, intro: false);
                return state.Selected < 0 || Advance(children[state.Selected], inputs);
            case var crossing when Crosses(crossing):
                if (inputs.CrossoverWeight(opIndex) is not { } weight)
                {
                    foreach (int child in children) Stop(child, inputs);
                    return true;
                }
                state.Weight = weight;
                bool lower = Advance(children[0], inputs);
                return Advance(children[1], inputs) && lower;
            case var weighted when PicksWeighted(weighted):
                Reselect(opIndex, state, inputs, children, intro: false);
                if (state.Selected < 0) return true;
                Advance(children[state.Selected], inputs);
                return false;
            case var withIntro when ReselectsWithIntro(withIntro):
                return AdvanceWithIntro(opIndex, state, inputs, children);
            case Gw2BlendOpType.Comparison:
                return AdvanceComparison(opIndex, inputs);
            case Gw2BlendOpType.Unknown11:
                return AdvanceParameterDriven(opIndex, state, inputs);
            default:
                return children.Length == 0 ? inputs.AdvanceLeaf(opIndex) : Advance(children[0], inputs);
        }
    }

    private void Reselect(int opIndex, Gw2BlendOpState state, IGw2BlendTreeInputs inputs, int[] children, bool intro)
    {
        if (inputs.SelectChild(opIndex) is not { } selected || selected == state.Selected) return;
        if (state.Selected >= 0) Stop(children[state.Selected + (state.PlaysIntro ? IntroBankOffset : 0)], inputs);
        state.Selected = selected;
        state.PlaysIntro = intro;
        Start(children[selected + (intro ? IntroBankOffset : 0)], inputs);
    }

    private bool AdvanceStepped(int opIndex, Gw2BlendOpState state, IGw2BlendTreeInputs inputs, int[] children)
    {
        if (inputs.SelectChild(opIndex) is not { } target)
        {
            foreach (int child in children) Stop(child, inputs);
            return true;
        }
        for (int index = 0; index < children.Length; index++)
            if (index != state.Selected) Advance(children[index], inputs);
        if (Advance(children[state.Selected], inputs) && state.Selected == target) return true;
        if (state.Selected != target) state.Selected += target > state.Selected ? 1 : -1;
        return false;
    }

    private bool AdvanceWithIntro(int opIndex, Gw2BlendOpState state, IGw2BlendTreeInputs inputs, int[] children)
    {
        Reselect(opIndex, state, inputs, children, intro: true);
        if (state.Selected < 0) return true;
        if (!state.PlaysIntro) return Advance(children[state.Selected], inputs);
        int introChild = children[state.Selected + IntroBankOffset];
        if (!Advance(introChild, inputs)) return false;
        Stop(introChild, inputs);
        state.PlaysIntro = false;
        Start(children[state.Selected], inputs);
        return false;
    }

    private bool AdvanceParameterDriven(int opIndex, Gw2BlendOpState state, IGw2BlendTreeInputs inputs)
    {
        Gw2BlendComparison settings = _comparisons[opIndex]!.Value;
        if (inputs.TryReadParameter(settings.Parameter, out float parameterValue, out float sourceMinimum, out float sourceMaximum))
        {
            float minimum = settings.ExplicitRange ? settings.RangeMinimum : sourceMinimum;
            float maximum = settings.ExplicitRange ? settings.RangeMaximum : sourceMaximum;
            if (Gw2BlendTreeRules.ComparisonValue(parameterValue, minimum, maximum) is not { } normalised) return true;
            state.Weight = normalised;
        }
        return Advance(_children[opIndex][0], inputs);
    }

    private bool AdvanceComparison(int opIndex, IGw2BlendTreeInputs inputs)
    {
        Gw2BlendComparison settings = _comparisons[opIndex]!.Value;
        float value = 0f;
        if (inputs.TryReadParameter(settings.Parameter, out float parameterValue, out float sourceMinimum, out float sourceMaximum))
        {
            float minimum = settings.ExplicitRange ? settings.RangeMinimum : sourceMinimum;
            float maximum = settings.ExplicitRange ? settings.RangeMaximum : sourceMaximum;
            float? normalised = Gw2BlendTreeRules.ComparisonValue(parameterValue, minimum, maximum);
            if (normalised is null) return false;
            value = normalised.Value;
        }
        if (Gw2BlendTreeRules.Compares(settings.Kind, value, settings.Threshold))
            TriggeredBlendTime = Gw2BlendTreeRules.ComparisonBlendTime(settings.BlendTimeSeconds, TreeBlendTime);
        return Advance(_children[opIndex][0], inputs);
    }

    private int DirectionOf(int opIndex, IGw2BlendTreeInputs inputs) =>
        Gw2BlendTreeRules.DirectionIndex(inputs.Heading(_vectorKinds[opIndex][0]), inputs.Heading(_vectorKinds[opIndex][1]));
}

internal sealed class Gw2BlendOpState
{
    internal bool Stopped { get; set; }
    internal bool Finished { get; set; }
    internal int Selected { get; set; } = -1;
    internal bool PlaysIntro { get; set; }
    internal float Weight { get; set; }
}

