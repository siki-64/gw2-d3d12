using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public enum Gw2BlendOpType
{
    PriorityClip = 0,
    LoopRangeClip = 1,
    Unknown2Clip = 2,
    Unknown3Clip = 3,
    Parallel = 4,
    DirectionSelect = 5,
    WeightedRandom = 6,
    WeightedRandomOptionsFirst = 7,
    Crossover = 8,
    TwoSidedCrossover = 9,
    Unknown10 = 10,
    Unknown11 = 11,
    Unknown12 = 12,
    Unknown13 = 13,
    Selector = 14,
    Unknown15 = 15,
    Unknown16 = 16,
    Unknown17 = 17,
    Unknown18 = 18,
    Unknown19 = 19,
    Comparison = 20,
    Unknown21 = 21,
    Unknown22 = 22,
    Unknown23 = 23,
    Unknown24 = 24,
    Unknown25 = 25,
    Unknown26 = 26,
}

public enum Gw2BlendComparisonKind
{
    Equal = 0,
    Greater = 1,
    GreaterOrEqual = 2,
    Less = 3,
    LessOrEqual = 4,
}

public readonly record struct Gw2BlendWeightedOption(int Child, float Weight, float CooldownSeconds);

public readonly record struct Gw2BlendComparison(uint Parameter, Gw2BlendComparisonKind Kind, float Threshold, float BlendTimeSeconds,
    bool ExplicitRange, float RangeMinimum, float RangeMaximum);

public sealed record Gw2BlendOp(
    Gw2BlendOpType Type,
    IReadOnlyList<int> Children,
    ulong? ClipToken,
    IReadOnlyList<Gw2BlendWeightedOption> Options,
    Gw2BlendComparison? Comparison,
    IReadOnlyList<uint> VectorKinds,
    float LengthThreshold);

public sealed record Gw2BlendTree(int Root, float BlendTimeSeconds, IReadOnlyList<Gw2BlendOp> Ops)
{
    public const int MissingOp = -1;
}

public sealed class Gw2BlendTrees
{
    private const uint ExplicitRangeFlag = 1;
    private const float PercentScale = 0.01f;
    private const float SecondsPerMillisecond = 0.001f;
    private const float NoBlendTime = -1f;

    private readonly Gw2ContentIndex _index;
    private readonly BlendTreeContentLayout _layout;

    public Gw2BlendTrees(Gw2ContentIndex index, BlendTreeContentLayout layout)
    {
        _index = index;
        _layout = layout;
    }

    public Gw2BlendTree? Find(Guid treeContentKey) =>
        _index.FindByContentKey(_layout.TreeContentType, treeContentKey) is { } tree ? Read(tree) : null;

    private Gw2BlendTree Read(Gw2ContentLocation tree)
    {
        uint count = _index.ReadUInt32(tree, _layout.OpCountOffset);
        var opsArray = count == 0 ? null : _index.ReadPointer(tree, _layout.OpsOffset);
        var ops = new Gw2BlendOp[opsArray is null ? 0 : count];
        for (int index = 0; index < ops.Length; index++)
            ops[index] = ReadOp(opsArray!.Value, count, OpAt(opsArray.Value, index));
        int root = opsArray is { } array ? IndexOfReference(array, count, tree, _layout.RootOffset) : Gw2BlendTree.MissingOp;
        return new Gw2BlendTree(root, _index.ReadInt32(tree, _layout.BlendTimeMillisecondsOffset) * SecondsPerMillisecond, ops);
    }

    private Gw2ContentLocation OpAt(Gw2ContentLocation opsArray, int index) =>
        opsArray with { Offset = checked(opsArray.Offset + (uint)(index * _layout.OpSize)) };

    private Gw2BlendOp ReadOp(Gw2ContentLocation opsArray, uint opCount, Gw2ContentLocation op)
    {
        var type = (Gw2BlendOpType)_index.ReadUInt32(op, _layout.OpTypeOffset);
        var data = _index.ReadPointer(op, _layout.OpDataOffset);
        if (data is not { } fields || (int)type >= _layout.OpTypeCount) return new Gw2BlendOp(type, [], null, [], null, [], 0f);
        var options = _layout.WeightedOptionsByOpType.TryGetValue((int)type, out var optionsLayout) ? ReadOptions(opsArray, opCount, fields, optionsLayout) : [];
        int[] children = optionsLayout is not null
            ? [.. options.Select(option => option.Child)]
            : [.. _layout.ChildOffsetsByOpType[(int)type].Select(offset => IndexOfReference(opsArray, opCount, fields, offset))];
        ulong? token = _layout.ClipTokenOffsetByOpType[(int)type] is { } tokenOffset
            ? BitConverter.ToUInt64(_index.ReadBytes(fields, tokenOffset, sizeof(ulong)))
            : null;
        return new Gw2BlendOp(type, children, token, options, ReadComparison(type, fields), ReadVectorKinds(type, fields),
            type == Gw2BlendOpType.Selector ? _index.ReadSingle(fields, _layout.SelectorLengthThresholdOffset) : 0f);
    }

    private Gw2BlendWeightedOption[] ReadOptions(Gw2ContentLocation opsArray, uint opCount, Gw2ContentLocation fields, BlendTreeWeightedOptionsLayout layout)
    {
        uint count = _index.ReadUInt32(fields, layout.CountOffset);
        if (count == 0 || _index.ReadPointer(fields, layout.ArrayOffset) is not { } array) return [];
        var options = new Gw2BlendWeightedOption[count];
        for (int index = 0; index < count; index++)
        {
            var option = array with { Offset = checked(array.Offset + (uint)(index * _layout.OptionSize)) };
            options[index] = new Gw2BlendWeightedOption(
                IndexOfReference(opsArray, opCount, option, _layout.OptionChildOffset),
                _index.ReadSingle(option, _layout.OptionWeightOffset),
                _index.ReadInt32(option, _layout.OptionCooldownMillisecondsOffset) * SecondsPerMillisecond);
        }
        return options;
    }

    private Gw2BlendComparison? ReadComparison(Gw2BlendOpType type, Gw2ContentLocation fields)
    {
        if (type == Gw2BlendOpType.Comparison)
        {
            var layout = _layout.ComparisonFields;
            return new Gw2BlendComparison(
                _index.ReadUInt32(fields, layout.ParameterOffset),
                (Gw2BlendComparisonKind)_index.ReadUInt32(fields, layout.ComparisonOffset),
                _index.ReadSingle(fields, layout.ThresholdPercentOffset) * PercentScale,
                _index.ReadInt32(fields, layout.BlendTimeMillisecondsOffset) * SecondsPerMillisecond,
                (_index.ReadUInt32(fields, layout.FlagsOffset) & ExplicitRangeFlag) != 0,
                _index.ReadSingle(fields, layout.RangeMinimumOffset),
                _index.ReadSingle(fields, layout.RangeMaximumOffset));
        }
        if (type == Gw2BlendOpType.Unknown11)
        {
            var layout = _layout.DrivenFields;
            return new Gw2BlendComparison(
                _index.ReadUInt32(fields, layout.ParameterOffset),
                Gw2BlendComparisonKind.Equal,
                0f,
                NoBlendTime,
                (_index.ReadUInt32(fields, layout.FlagsOffset) & ExplicitRangeFlag) != 0,
                _index.ReadSingle(fields, layout.RangeMinimumOffset),
                _index.ReadSingle(fields, layout.RangeMaximumOffset));
        }
        return null;
    }

    private uint[] ReadVectorKinds(Gw2BlendOpType type, Gw2ContentLocation fields) => type switch
    {
        Gw2BlendOpType.DirectionSelect =>
        [
            _index.ReadUInt32(fields, _layout.DirectionMeasuredVectorKindOffset),
            _index.ReadUInt32(fields, _layout.DirectionReferenceVectorKindOffset),
        ],
        Gw2BlendOpType.Selector => [_index.ReadUInt32(fields, _layout.SelectorParameterOffset)],
        _ => [],
    };

    private int IndexOfReference(Gw2ContentLocation opsArray, uint opCount, Gw2ContentLocation holder, int fieldOffset)
    {
        if (_index.ReadPointer(holder, fieldOffset) is not { } slot) return Gw2BlendTree.MissingOp;
        if (_index.ReadPointer(slot, 0) is not { } op || op.PackIndex != opsArray.PackIndex || op.Offset < opsArray.Offset) return Gw2BlendTree.MissingOp;
        uint distance = op.Offset - opsArray.Offset;
        if (distance % (uint)_layout.OpSize != 0) return Gw2BlendTree.MissingOp;
        uint index = distance / (uint)_layout.OpSize;
        return index < opCount ? (int)index : Gw2BlendTree.MissingOp;
    }
}
