using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed record ContentFunctionBinding(uint BindingUid, uint InputId, uint FunctionUid, byte Slot);

public enum ContentFunctionConditionType : uint
{
    HasClientInitiative = 0,
    HasEndurance = 1,
    HasEndurancePercent = 2,
    HasEnergy = 3,
    Locale = 4,
    MovementParameterCompare = 5,
    ProgressIsComplete = 6,
    ProgressCompare = 7,
    PlayerIsUsingAdvancedMovementInput = 8,
    StackInputEquals = 9,
    StackInputReleased = 10,
    HasBuff = 11,
    BuffCountCompare = 12,
    HasMovementModifier = 13,
    MovementModifierDurationRemainingCompare = 14,
    SkillRechargeRemainingCompare = 15,
    TrainingLevelIsComplete = 16,
    IsMount = 17,
    SkillSlotOccupied = 18,
    SkillSlotRangeOccupied = 19,
    And = 20,
    Or = 21,
    TargetWithinFacingAngle = 22,
    TargetInSameGroup = 23,
    TargetIsFriendly = 24,
    TargetIsHostile = 25,
    TargetIsInteractiveGadget = 26,
    TargetIsCharacter = 27,
    MapMode = 28,
    MoverStateHasFlag = 29,
    TargetIsMountGadget = 30,
}

public enum ContentFunctionComparison : uint
{
    Equal = 0,
    Greater = 1,
    GreaterOrEqual = 2,
    Less = 3,
    LessOrEqual = 4,
}

public sealed record ContentFunctionCondition(
    ContentFunctionConditionType Type,
    bool Negated,
    uint LocaleKind = 0,
    uint BuffDataId = 0,
    bool BuffOnSelf = false,
    uint MovementModifierUid = 0,
    uint Selector = 0,
    ContentFunctionComparison Comparison = ContentFunctionComparison.Equal,
    float Value = 0,
    uint Count = 0,
    ContentFunctionCondition? Left = null,
    ContentFunctionCondition? Right = null);

public abstract record ContentFunctionNode(int Index, uint Opcode);

public sealed record ContentFunctionArgumentValue(uint Word, float Single, uint? Uid, Gw2ContentLocation? Location, ContentFunctionCondition? Condition);

public sealed record ContentFunctionEffectNode(int Index, uint Opcode, IReadOnlyDictionary<ContentFunctionArgument, ContentFunctionArgumentValue> Arguments)
    : ContentFunctionNode(Index, Opcode)
{
    public ContentFunctionOpcode Kind => (ContentFunctionOpcode)Opcode;

    public uint Word(ContentFunctionArgument argument) => Arguments.TryGetValue(argument, out var value) ? value.Word : 0;

    public float Single(ContentFunctionArgument argument) => Arguments.TryGetValue(argument, out var value) ? value.Single : 0;

    public uint? Uid(ContentFunctionArgument argument) => Arguments.TryGetValue(argument, out var value) ? value.Uid : null;

    public ContentFunctionCondition? Condition(ContentFunctionArgument argument) => Arguments.TryGetValue(argument, out var value) ? value.Condition : null;
}

public sealed record ContentFunctionWaitNode(int Index, uint Opcode, uint Milliseconds) : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionWaitForConditionNode(int Index, uint Opcode, ContentFunctionCondition Condition, uint TimeoutMilliseconds)
    : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionConditionalNode(int Index, uint Opcode, ContentFunctionCondition Condition, uint? ThenFunctionUid, uint? ElseFunctionUid)
    : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionRunStackNode(int Index, uint Opcode, uint FunctionUid) : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionWhileLoopNode(int Index, uint Opcode, ContentFunctionCondition? Condition, uint MinimumIterations, uint MaximumIterations, uint? BodyFunctionUid)
    : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionApplyMovementModifierNode(int Index, uint Opcode, uint MovementModifierUid, uint? DurationMilliseconds)
    : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionApplyMovementModifierFromEnergyNode(int Index, uint Opcode, uint MovementModifierUid, uint EnergyKind, float MillisecondsPerEnergy)
    : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionEnduranceAdjustNode(int Index, uint Opcode, int Amount) : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionAbortNode(int Index, uint Opcode) : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionRemoveMovementModifierNode(int Index, uint Opcode, uint MovementModifierUid) : ContentFunctionNode(Index, Opcode);

public sealed record ContentFunctionProgram(uint FunctionUid, IReadOnlyList<ContentFunctionNode> Nodes);

public sealed class Gw2ContentFunctions
{
    public const uint ApplyMovementModifierOpcode = 12;
    public const uint RemoveMovementModifierOpcode = 13;
    public const uint ApplyMovementModifierWithDurationOpcode = 14;
    public const uint ApplyMovementModifierWithDurationFromEnergyOpcode = 16;
    public const uint EnduranceAdjustOpcode = 44;
    public const uint WaitOpcode = 35;
    public const uint WaitForConditionOpcode = 36;
    public const uint ConditionalOpcode = 37;
    public const uint RunFunctionStackOpcode = 38;
    public const uint WhileLoopOpcode = 39;
    public const uint AbortOpcode = 42;

    private const int ConditionTypeOffset = 0x00;
    private const int ConditionParametersOffset = 0x08;
    private const int ConditionNegatedOffset = 0x10;
    private const int LocaleKindOffset = 0x00;
    private const int HasBuffSkillOffset = 0x00;
    private const int HasBuffOnSelfOffset = 0x08;
    private const int HasMovementModifierOffset = 0x00;
    private const int ConditionalConditionOffset = 0x00;
    private const int ConditionalThenOffset = 0x08;
    private const int ConditionalElseOffset = 0x10;
    private const int WaitMillisecondsOffset = 0x00;
    private const int WaitForConditionConditionOffset = 0x00;
    private const int WaitForConditionTimeoutOffset = 0x08;
    private const int RunFunctionStackFunctionOffset = 0x00;
    private const int RemoveMovementModifierOffset = 0x00;
    private const int AppliedMovementModifierOffset = 0x00;
    private const int AppliedDurationOffset = 0x08;
    private const int AppliedEnergyKindOffset = 0x08;
    private const int AppliedEnergyScaleOffset = 0x0C;
    private const int EnduranceAmountOffset = 0x00;
    private const int WhileLoopConditionOffset = 0x00;
    private const int WhileLoopMinimumOffset = 0x08;
    private const int WhileLoopMaximumOffset = 0x0C;
    private const int WhileLoopBodyOffset = 0x10;
    private const int SelectorOffset = 0x00;
    private const int ValueOffset = 0x00;
    private const int EnergyValueOffset = 0x04;
    private const int ParameterComparisonOffset = 0x04;
    private const int ParameterValueOffset = 0x08;
    private const int SkillComparisonOffset = 0x08;
    private const int SkillCountOffset = 0x0C;
    private const int LeftConditionOffset = 0x00;
    private const int RightConditionOffset = 0x08;

    private readonly Gw2ContentIndex _index;
    private readonly ContentFunctionContentLayout _layout;
    private readonly uint _skillContentType;
    private readonly Dictionary<uint, ContentFunctionProgram?> _programs = [];

    public Gw2ContentFunctions(Gw2ContentIndex index, ContentFunctionContentLayout layout, uint skillContentType)
    {
        _index = index;
        _layout = layout;
        _skillContentType = skillContentType;
    }

    public ContentFunctionContentLayout Layout => _layout;

    public ContentFunctionBinding? Binding(uint bindingUid)
    {
        if (_index.Find(_layout.BindingContentType, bindingUid) is not { } binding) return null;
        if (_index.ReadPointer(binding, _layout.BindingInputOffset) is not { } input) return null;
        if (_index.ReadPointer(binding, _layout.BindingFunctionOffset) is not { } function) return null;
        return new ContentFunctionBinding(
            bindingUid,
            _index.ReadUInt32(input, _layout.InputIdOffset),
            FunctionUidOf(function),
            _index.ReadByte(binding, _layout.BindingSlotOffset));
    }

    public ContentFunctionProgram? Program(uint functionUid)
    {
        if (_programs.TryGetValue(functionUid, out var cached)) return cached;
        ContentFunctionProgram? program = null;
        if (_index.Find(_layout.FunctionContentType, functionUid) is { } function)
        {
            var count = (int)_index.ReadUInt32(function, _layout.NodeCountOffset);
            var nodes = new List<ContentFunctionNode>(count);
            if (count > 0 && _index.ReadPointer(function, _layout.NodesOffset) is { } nodeArray)
                for (var index = 0; index < count; index++) nodes.Add(NodeAt(nodeArray, index));
            program = new ContentFunctionProgram(functionUid, nodes);
        }
        _programs[functionUid] = program;
        return program;
    }

    private ContentFunctionNode NodeAt(Gw2ContentLocation nodeArray, int index)
    {
        var nodeOffset = index * ContentFunctionContentLayout.NodeSize;
        var opcode = _index.ReadUInt32(nodeArray, nodeOffset + ContentFunctionContentLayout.NodeOpcodeOffset);
        if (opcode == AbortOpcode) return new ContentFunctionAbortNode(index, opcode);
        if (_index.ReadPointer(nodeArray, nodeOffset + ContentFunctionContentLayout.NodeArgumentsOffset) is not { } arguments)
            return new ContentFunctionEffectNode(index, opcode, new Dictionary<ContentFunctionArgument, ContentFunctionArgumentValue>());
        return opcode switch
        {
            WaitOpcode => new ContentFunctionWaitNode(index, opcode, _index.ReadUInt32(arguments, WaitMillisecondsOffset)),
            WaitForConditionOpcode => new ContentFunctionWaitForConditionNode(index, opcode,
                ConditionAt(arguments, WaitForConditionConditionOffset), _index.ReadUInt32(arguments, WaitForConditionTimeoutOffset)),
            ConditionalOpcode => new ContentFunctionConditionalNode(index, opcode,
                ConditionAt(arguments, ConditionalConditionOffset), FunctionUidAt(arguments, ConditionalThenOffset), FunctionUidAt(arguments, ConditionalElseOffset)),
            WhileLoopOpcode => new ContentFunctionWhileLoopNode(index, opcode, OptionalConditionAt(arguments, WhileLoopConditionOffset),
                _index.ReadUInt32(arguments, WhileLoopMinimumOffset), _index.ReadUInt32(arguments, WhileLoopMaximumOffset), FunctionUidAt(arguments, WhileLoopBodyOffset)),
            RunFunctionStackOpcode when FunctionUidAt(arguments, RunFunctionStackFunctionOffset) is { } function =>
                new ContentFunctionRunStackNode(index, opcode, function),
            ApplyMovementModifierOpcode when ModifierUidAt(arguments) is { } applied =>
                new ContentFunctionApplyMovementModifierNode(index, opcode, applied, null),
            ApplyMovementModifierWithDurationOpcode when ModifierUidAt(arguments) is { } timed =>
                new ContentFunctionApplyMovementModifierNode(index, opcode, timed, _index.ReadUInt32(arguments, AppliedDurationOffset)),
            ApplyMovementModifierWithDurationFromEnergyOpcode when ModifierUidAt(arguments) is { } fueled =>
                new ContentFunctionApplyMovementModifierFromEnergyNode(index, opcode, fueled,
                    _index.ReadUInt32(arguments, AppliedEnergyKindOffset), _index.ReadSingle(arguments, AppliedEnergyScaleOffset)),
            EnduranceAdjustOpcode => new ContentFunctionEnduranceAdjustNode(index, opcode, (int)_index.ReadSingle(arguments, EnduranceAmountOffset)),
            RemoveMovementModifierOpcode when _index.ReadPointer(arguments, RemoveMovementModifierOffset) is { } modifier =>
                new ContentFunctionRemoveMovementModifierNode(index, opcode, _index.UidOf(_layout.MovementModifierContentType, modifier)),
            _ => new ContentFunctionEffectNode(index, opcode, ArgumentsAt((ContentFunctionOpcode)opcode, arguments)),
        };
    }

    private Dictionary<ContentFunctionArgument, ContentFunctionArgumentValue> ArgumentsAt(ContentFunctionOpcode opcode, Gw2ContentLocation arguments)
    {
        var values = new Dictionary<ContentFunctionArgument, ContentFunctionArgumentValue>();
        foreach (var field in ContentFunctionArgumentLayouts.Of(opcode))
        {
            var pointer = field.Kind is ContentFunctionArgumentKind.UInt32 or ContentFunctionArgumentKind.Single ? null : _index.ReadPointer(arguments, field.Offset);
            values[field.Argument] = field.Kind switch
            {
                ContentFunctionArgumentKind.UInt32 or ContentFunctionArgumentKind.Single =>
                    new ContentFunctionArgumentValue(_index.ReadUInt32(arguments, field.Offset), _index.ReadSingle(arguments, field.Offset), null, null, null),
                ContentFunctionArgumentKind.MovementModifier => new ContentFunctionArgumentValue(0, 0,
                    pointer is { } modifier ? _index.UidOf(_layout.MovementModifierContentType, modifier) : null, pointer, null),
                ContentFunctionArgumentKind.Skill => new ContentFunctionArgumentValue(0, 0,
                    pointer is { } skill ? _index.DataIdOf(_skillContentType, skill) : null, pointer, null),
                ContentFunctionArgumentKind.Function => new ContentFunctionArgumentValue(0, 0, pointer is { } function ? FunctionUidOf(function) : null, pointer, null),
                ContentFunctionArgumentKind.Condition => new ContentFunctionArgumentValue(0, 0, null, pointer, OptionalConditionAt(arguments, field.Offset)),
                _ => new ContentFunctionArgumentValue(0, 0, null, pointer, null),
            };
        }
        return values;
    }

    private ContentFunctionCondition? OptionalConditionAt(Gw2ContentLocation arguments, int offset) =>
        _index.ReadPointer(arguments, offset) is null ? null : ConditionAt(arguments, offset);

    private ContentFunctionCondition ConditionAt(Gw2ContentLocation arguments, int offset)
    {
        var condition = _index.ReadPointer(arguments, offset)!.Value;
        var type = (ContentFunctionConditionType)_index.ReadUInt32(condition, ConditionTypeOffset);
        var negated = _index.ReadUInt32(condition, ConditionNegatedOffset) != 0;
        if (_index.ReadPointer(condition, ConditionParametersOffset) is not { } parameters) return new ContentFunctionCondition(type, negated);
        return type switch
        {
            ContentFunctionConditionType.Locale => new ContentFunctionCondition(type, negated, LocaleKind: _index.ReadUInt32(parameters, LocaleKindOffset)),
            ContentFunctionConditionType.HasBuff => new ContentFunctionCondition(type, negated,
                BuffDataId: _index.ReadPointer(parameters, HasBuffSkillOffset) is { } skill ? _index.DataIdOf(_skillContentType, skill) : 0,
                BuffOnSelf: _index.ReadUInt32(parameters, HasBuffOnSelfOffset) != 0),
            ContentFunctionConditionType.HasMovementModifier => new ContentFunctionCondition(type, negated,
                MovementModifierUid: _index.ReadPointer(parameters, HasMovementModifierOffset) is { } modifier ? _index.UidOf(_layout.MovementModifierContentType, modifier) : 0),
            ContentFunctionConditionType.HasEndurance or ContentFunctionConditionType.HasEndurancePercent or ContentFunctionConditionType.TargetWithinFacingAngle =>
                new ContentFunctionCondition(type, negated, Value: _index.ReadSingle(parameters, ValueOffset)),
            ContentFunctionConditionType.HasEnergy => new ContentFunctionCondition(type, negated,
                Selector: _index.ReadUInt32(parameters, SelectorOffset), Value: _index.ReadSingle(parameters, EnergyValueOffset)),
            ContentFunctionConditionType.MovementParameterCompare => new ContentFunctionCondition(type, negated,
                Selector: _index.ReadUInt32(parameters, SelectorOffset),
                Comparison: (ContentFunctionComparison)_index.ReadUInt32(parameters, ParameterComparisonOffset),
                Value: _index.ReadSingle(parameters, ParameterValueOffset)),
            ContentFunctionConditionType.PlayerIsUsingAdvancedMovementInput or ContentFunctionConditionType.StackInputEquals
                or ContentFunctionConditionType.MapMode or ContentFunctionConditionType.MoverStateHasFlag =>
                new ContentFunctionCondition(type, negated, Selector: _index.ReadUInt32(parameters, SelectorOffset)),
            ContentFunctionConditionType.BuffCountCompare or ContentFunctionConditionType.SkillRechargeRemainingCompare => new ContentFunctionCondition(type, negated,
                BuffDataId: _index.ReadPointer(parameters, HasBuffSkillOffset) is { } countedSkill ? _index.DataIdOf(_skillContentType, countedSkill) : 0,
                Comparison: (ContentFunctionComparison)_index.ReadUInt32(parameters, SkillComparisonOffset),
                Count: _index.ReadUInt32(parameters, SkillCountOffset)),
            ContentFunctionConditionType.And or ContentFunctionConditionType.Or => new ContentFunctionCondition(type, negated,
                Left: OptionalConditionAt(parameters, LeftConditionOffset), Right: OptionalConditionAt(parameters, RightConditionOffset)),
            _ => new ContentFunctionCondition(type, negated),
        };
    }

    private uint? ModifierUidAt(Gw2ContentLocation arguments) =>
        _index.ReadPointer(arguments, AppliedMovementModifierOffset) is { } modifier ? _index.UidOf(_layout.MovementModifierContentType, modifier) : null;

    private uint? FunctionUidAt(Gw2ContentLocation arguments, int offset) =>
        _index.ReadPointer(arguments, offset) is { } function ? FunctionUidOf(function) : null;

    private uint FunctionUidOf(Gw2ContentLocation function) => _index.UidOf(_layout.FunctionContentType, function);
}
