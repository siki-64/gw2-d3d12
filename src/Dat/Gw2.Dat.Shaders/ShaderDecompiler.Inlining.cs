using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Gw2.Dat.Content;

namespace Gw2.Dat.Shaders;

public sealed partial class ShaderDecompiler
{
    private const char ReferenceStart = '\u0001';
    private const char ReferenceEnd = '\u0002';

    private static readonly HashSet<string> ConditionalControlFlowNames = ["if", "breakc", "continuec", "discard", "retc", "switch"];
    private static readonly HashSet<string> ControlFlowNames = ["else", "endif", "loop", "endloop", "break", "continue", "case", "default", "endswitch", "ret"];

    private readonly List<Statement> _statements = [];
    private readonly List<SourceReference> _references = [];
    private readonly List<int> _pendingReferences = [];
    private bool _pendingReadsUntracked;
    private int _block;
    private int _loopDepth;
    private int _depth = 1;

    private sealed class Statement
    {
        public required string Template { get; init; }
        public required int Depth { get; init; }
        public required int Block { get; init; }
        public required bool InsideLoop { get; init; }
        public required IReadOnlyList<int> References { get; init; }
        public required bool ReadsUntracked { get; init; }
        public int? DestinationTemp { get; init; }
        public IReadOnlyList<int> DestinationComponents { get; init; } = [];
        public string? ValueTemplate { get; init; }
        public string? ConditionTemplate { get; init; }
        public ValueType ValueType { get; init; }
        public bool ScalarValue { get; init; }
        public bool Inlined { get; set; }
        public int ConsumerIndex { get; set; }
        public SourceReference? ConsumerReference { get; set; }
        public bool OpensLoop { get; init; }
        public string? BreakCondition { get; init; }
        public HashSet<(int Temp, int Component)> EffectiveReads { get; } = [];
    }

    private sealed class SourceReference
    {
        public required int Temp { get; init; }
        public required IReadOnlyList<int> RegisterComponents { get; init; }
        public required ValueType Type { get; init; }
        public required Gw2Sm5OperandModifier Modifier { get; init; }
        public required bool AsCondition { get; init; }
        public required string Fallback { get; init; }
        public Statement? Definition { get; set; }
    }

    private void Translate(Gw2Sm5Instruction instruction)
    {
        if (ControlFlowNames.Contains(instruction.Name)) _block++;
        if (instruction.Name == "endloop") _loopDepth--;
        TranslateInstruction(instruction);
        if (instruction.Name == "loop") _loopDepth++;
        if (ControlFlowNames.Contains(instruction.Name) || ConditionalControlFlowNames.Contains(instruction.Name)) _block++;
    }

    private void Line(string template) => _statements.Add(NewStatement(template));

    private void BreakIf(string condition) => _statements.Add(NewStatement($"if ({condition}) break;", breakCondition: condition));

    private Statement NewStatement(string template, int? destinationTemp = null, IReadOnlyList<int>? destinationComponents = null, string? valueTemplate = null, string? conditionTemplate = null, ValueType valueType = ValueType.Float, bool scalarValue = false, bool opensLoop = false, string? breakCondition = null)
    {
        var statement = new Statement
        {
            Template = template,
            Depth = _depth,
            Block = _block,
            InsideLoop = _loopDepth > 0,
            References = _pendingReferences.ToArray(),
            ReadsUntracked = _pendingReadsUntracked,
            DestinationTemp = destinationTemp,
            DestinationComponents = destinationComponents ?? [],
            ValueTemplate = valueTemplate,
            ConditionTemplate = conditionTemplate,
            ValueType = valueType,
            ScalarValue = scalarValue,
            OpensLoop = opensLoop,
            BreakCondition = breakCondition,
        };
        _pendingReferences.Clear();
        _pendingReadsUntracked = false;
        return statement;
    }

    private void EmitAssignment(Gw2Sm5Instruction instruction, Gw2Sm5Operand destination, IReadOnlyList<int> components, string expression, ValueType resultType, bool scalarValue = false, string? condition = null)
    {
        if (instruction.Saturate)
        {
            expression = $"saturate({expression})";
            condition = null;
        }

        var temp = destination.RegisterType == Gw2ShaderRegisterType.Temp && destination.ComponentCount == 4 ? destination.FirstIndex : (int?)null;
        var destinationType = DestinationValueType(destination);
        var value = destinationType == ValueType.Bool && condition is not null ? condition : Convert(expression, resultType, destinationType);
        var template = $"{DestinationName(destination, components)} = {value};";
        _statements.Add(NewStatement(template, temp, components, expression, condition, resultType, scalarValue));
    }

    private string TempReference(Gw2Sm5Operand operand, IReadOnlyList<int> components, ValueType type, bool asCondition, string fallback) =>
        TempReference(operand.FirstIndex, components.Select(component => operand.Swizzle[component]).ToArray(), operand.Modifier, type, asCondition, fallback);

    private string TempReference(int temp, IReadOnlyList<int> registerComponents, Gw2Sm5OperandModifier modifier, ValueType type, bool asCondition, string fallback)
    {
        var index = _references.Count;
        _references.Add(new SourceReference
        {
            Temp = temp,
            RegisterComponents = registerComponents,
            Type = type,
            Modifier = modifier,
            AsCondition = asCondition,
            Fallback = fallback,
        });
        _pendingReferences.Add(index);
        return $"{ReferenceStart}{index.ToString(CultureInfo.InvariantCulture)}{ReferenceEnd}";
    }

    private void MarkUntrackedRead() => _pendingReadsUntracked = true;

    private string RenderBody()
    {
        DecideInlining();
        var text = new StringBuilder();
        var foldedIntoLoopCondition = new HashSet<Statement>();
        foreach (var statement in _statements)
        {
            if (statement.Inlined || foldedIntoLoopCondition.Contains(statement)) continue;
            var line = statement.OpensLoop && LoopCondition(statement, foldedIntoLoopCondition) is { } condition ? $"while ({condition})" : Render(statement.Template);
            text.Append(' ', statement.Depth * 4).Append(line).Append('\n');
        }

        return text.ToString();
    }

    private string? LoopCondition(Statement loop, HashSet<Statement> folded)
    {
        var parts = new List<string>();
        var prefix = new List<Statement>();
        for (var index = _statements.IndexOf(loop) + 2; index < _statements.Count; index++)
        {
            var statement = _statements[index];
            if (statement.Inlined) continue;
            if (statement.BreakCondition is { } condition)
            {
                parts.Add($"!({Render(condition)})");
                folded.UnionWith(prefix);
                folded.Add(statement);
                return string.Join(", ", parts);
            }

            if (statement.ValueTemplate is null || !statement.Template.EndsWith(';') || statement.Template.StartsWith("float", StringComparison.Ordinal)) return null;
            prefix.Add(statement);
            parts.Add(Render(statement.Template)[..^1]);
        }

        return null;
    }

    private void DecideInlining()
    {
        for (var index = 0; index < _statements.Count; index++)
        {
            var statement = _statements[index];
            foreach (var referenceIndex in statement.References)
            {
                var reference = _references[referenceIndex];
                if (reference.Definition is { } definition) statement.EffectiveReads.UnionWith(definition.EffectiveReads);
                else foreach (var component in reference.RegisterComponents) statement.EffectiveReads.Add((reference.Temp, component));
            }

            if (statement.DestinationTemp is not { } temp || statement.ValueTemplate is null || statement.InsideLoop || statement.ReadsUntracked) continue;
            if (FindSingleUse(index, temp, statement.DestinationComponents) is not { } use) continue;
            var (useIndex, useReference) = use;
            if (!ClobbersNothing(statement, temp, index, useIndex)) continue;
            useReference.Definition = statement;
            statement.Inlined = true;
            statement.ConsumerIndex = useIndex;
            statement.ConsumerReference = useReference;
        }

        KeepSourceOrder();
    }

    private void KeepSourceOrder()
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var index = 0; index < _statements.Count; index++)
            {
                var statement = _statements[index];
                if (!statement.Inlined) continue;
                var root = RootIndex(index);
                var interleaved = false;
                for (var between = index + 1; between < statement.ConsumerIndex && !interleaved; between++)
                    interleaved = RootIndex(between) != root;
                if (!interleaved && !EvaluatedOutOfOrder(statement)) continue;
                statement.Inlined = false;
                statement.ConsumerReference!.Definition = null;
                changed = true;
            }
        }
    }

    private bool EvaluatedOutOfOrder(Statement statement)
    {
        var consumer = _statements[statement.ConsumerIndex];
        var ownPosition = _statements.IndexOf(statement);
        var passed = false;
        foreach (var referenceIndex in consumer.References)
        {
            if (_references[referenceIndex].Definition is not { } definition) continue;
            if (definition == statement) passed = true;
            else if (!passed && _statements.IndexOf(definition) > ownPosition) return true;
        }

        return false;
    }

    private int RootIndex(int index)
    {
        while (_statements[index].Inlined) index = _statements[index].ConsumerIndex;
        return index;
    }

    private (int StatementIndex, SourceReference Reference)? FindSingleUse(int definitionIndex, int temp, IReadOnlyList<int> components)
    {
        var definition = _statements[definitionIndex];
        var live = new HashSet<int>(components);
        (int, SourceReference)? use = null;
        for (var index = definitionIndex + 1; index < _statements.Count && live.Count > 0; index++)
        {
            var statement = _statements[index];
            foreach (var referenceIndex in statement.References)
            {
                var reference = _references[referenceIndex];
                if (reference.Temp != temp || !reference.RegisterComponents.Any(live.Contains)) continue;
                if (use is not null || statement.Block != definition.Block || !reference.RegisterComponents.All(live.Contains)) return null;
                use = (index, reference);
            }

            if (statement.DestinationTemp == temp && statement.Block == definition.Block) live.ExceptWith(statement.DestinationComponents);
        }

        return use;
    }

    private bool ClobbersNothing(Statement definition, int temp, int definitionIndex, int useIndex)
    {
        for (var index = definitionIndex + 1; index < useIndex; index++)
        {
            var statement = _statements[index];
            if (statement.DestinationTemp is not { } written) continue;
            if (statement.DestinationComponents.Any(component => definition.EffectiveReads.Contains((written, component)))) return false;
        }

        return true;
    }

    private string Render(string template) =>
        ReferenceMarker().Replace(template, match => RenderReference(_references[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)]));

    private string RenderReference(SourceReference reference)
    {
        if (reference.Definition is not { } definition) return Render(reference.Fallback);

        if (reference.AsCondition && definition.ConditionTemplate is not null && reference.Modifier == Gw2Sm5OperandModifier.None)
            return Parenthesize(Select(Render(definition.ConditionTemplate), definition, reference));

        var value = Convert(Render(definition.ValueTemplate!), definition.ValueType, reference.Type);
        value = Select(value, definition, reference);
        value = reference.Modifier switch
        {
            Gw2Sm5OperandModifier.Negate => "-" + Parenthesize(value),
            Gw2Sm5OperandModifier.Absolute => $"abs({value})",
            Gw2Sm5OperandModifier.AbsoluteNegate => $"-abs({value})",
            _ => value,
        };
        return reference.AsCondition ? $"({value} != 0u)" : Parenthesize(value);
    }

    private static string Select(string value, Statement definition, SourceReference reference)
    {
        if (definition.ScalarValue) return value;
        var positions = reference.RegisterComponents.Select(component => definition.DestinationComponents.ToList().IndexOf(component)).ToArray();
        var identity = positions.Length == definition.DestinationComponents.Count && positions.Select((position, order) => position == order).All(same => same);
        return identity ? value : Parenthesize(value) + "." + string.Concat(positions.Select(position => Components[position]));
    }

    private static string Parenthesize(string expression) => IsAtomic(expression) ? expression : $"({expression})";

    private static bool IsAtomic(string expression)
    {
        if (expression.Length == 0 || expression[0] == '-' || expression[0] == '~' || expression[0] == '!') return false;
        var depth = 0;
        var leadingGroupOpen = expression[0] == '(';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (character is '(' or '[') depth++;
            else if (character is ')' or ']')
            {
                depth--;
                if (depth != 0 || !leadingGroupOpen) continue;
                leadingGroupOpen = false;
                if (index + 1 < expression.Length && expression[index + 1] != '.') return false;
            }
            else if (depth == 0 && (character == ' ' || character == '?')) return false;
        }

        return true;
    }

    [GeneratedRegex("\u0001(\\d+)\u0002")]
    private static partial Regex ReferenceMarker();
}
