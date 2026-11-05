using System.Globalization;
using Gw2.Dat.Content;

namespace Gw2.Dat.Shaders;

public sealed partial class ShaderDecompiler
{
    private const int UnwrittenDefinition = -1;

    private static readonly HashSet<string> SourceOnlyNames = ["if", "breakc", "continuec", "retc", "discard", "switch", "callc", "case"];
    private static readonly HashSet<string> TwoDestinationNames = ["sincos", "udiv", "umul", "imul", "swapc", "uaddc", "usubb"];
    private static readonly HashSet<string> UnstructuredNames = ["switch", "call", "callc", "label"];

    private readonly Dictionary<int, Dictionary<(int Temp, int Component), HashSet<int>>> _readDefinitions = [];
    private readonly Dictionary<int, HashSet<int>> _readers = [];
    private readonly Dictionary<int, Func<IReadOnlyList<int>, string>> _idiomExpressions = [];
    private readonly Dictionary<int, List<(string Name, int Temp, int Component)>> _namedCopies = [];
    private readonly HashSet<int> _skippedInstructions = [];

    private IReadOnlyList<Gw2Sm5Instruction> Instructions => _program.Instructions;

    private void RecogniseIdioms()
    {
        if (WritesLiterally || !AnalyseDataflow()) return;
        BuildVariables();
        ChooseMatrixUniforms();
        RecogniseAwayFromZero();
        RecognisePows();
        RecogniseSmoothsteps();
        RecogniseReflects();
        RecogniseNormalizes();
        RecogniseLerps();
        RecogniseMatrixRows();
        RecogniseClips();
        RecogniseMatrixMultiplies();
    }

    private bool AnalyseDataflow()
    {
        if (Instructions.Any(instruction => UnstructuredNames.Contains(instruction.Name))) return false;
        var loopEntries = new Dictionary<int, Dictionary<(int, int), HashSet<int>>?>();
        for (var round = 0; round < MaximumDataflowRounds; round++)
        {
            var backEdges = AnalyseDataflowRound(loopEntries);
            var stable = backEdges.All(pair => loopEntries.TryGetValue(pair.Key, out var known) && SameState(known, pair.Value));
            foreach (var (loop, state) in backEdges) loopEntries[loop] = state;
            if (stable) return true;
        }

        return false;
    }

    private Dictionary<int, Dictionary<(int, int), HashSet<int>>?> AnalyseDataflowRound(Dictionary<int, Dictionary<(int, int), HashSet<int>>?> loopEntries)
    {
        _readDefinitions.Clear();
        _reachingBefore.Clear();
        _readers.Clear();
        Dictionary<(int, int), HashSet<int>>? current = [];
        var frames = new Stack<(Dictionary<(int, int), HashSet<int>>? Before, Dictionary<(int, int), HashSet<int>>? ThenBranch)>();
        var loops = new Stack<(int Index, Dictionary<(int, int), HashSet<int>>? Exits, Dictionary<(int, int), HashSet<int>>? Continues)>();
        var backEdges = new Dictionary<int, Dictionary<(int, int), HashSet<int>>?>();
        for (var index = 0; index < Instructions.Count; index++)
        {
            var instruction = Instructions[index];
            if (Gw2Sm5OpCodes.IsDeclaration(instruction.OpCode) || instruction.OpCode == Gw2Sm5OpCodes.CustomData) continue;
            if (instruction.Name == "loop") current = Merge(current, loopEntries.GetValueOrDefault(index));

            var state = current ?? [];
            var destinationCount = DestinationCount(instruction);
            var reads = new Dictionary<(int Temp, int Component), HashSet<int>>();
            for (var operandIndex = 0; operandIndex < instruction.Operands.Count; operandIndex++)
                RecordReads(instruction.Operands[operandIndex], operandIndex >= destinationCount, index, state, reads);
            _readDefinitions[index] = reads;
            _reachingBefore[index] = new(state);

            switch (instruction.Name)
            {
                case "if":
                    frames.Push((Copy(current), null));
                    break;
                case "else":
                    var thenFrame = frames.Pop();
                    frames.Push((thenFrame.Before, Copy(current)));
                    current = Copy(thenFrame.Before);
                    break;
                case "endif":
                    var frame = frames.Pop();
                    current = frame.ThenBranch is null && frame.Before is null ? current : Merge(frame.ThenBranch ?? frame.Before, current);
                    break;
                case "loop":
                    loops.Push((index, null, null));
                    break;
                case "break" or "breakc":
                    var breaking = loops.Pop();
                    loops.Push((breaking.Index, Merge(breaking.Exits, current), breaking.Continues));
                    if (instruction.Name == "break") current = null;
                    break;
                case "continue" or "continuec":
                    var continuing = loops.Pop();
                    loops.Push((continuing.Index, continuing.Exits, Merge(continuing.Continues, current)));
                    if (instruction.Name == "continue") current = null;
                    break;
                case "endloop":
                    var loop = loops.Pop();
                    backEdges[loop.Index] = Merge(loop.Continues, current);
                    current = loop.Exits;
                    break;
                case "ret":
                    current = null;
                    break;
            }

            if (current is null) continue;
            for (var operandIndex = 0; operandIndex < destinationCount && operandIndex < instruction.Operands.Count; operandIndex++)
            {
                var destination = instruction.Operands[operandIndex];
                if (destination.RegisterType != Gw2ShaderRegisterType.Temp || destination.ComponentCount != 4) continue;
                foreach (var component in DestinationComponents(destination)) current[(destination.FirstIndex, component)] = [index];
            }
        }

        return backEdges;
    }

    private static Dictionary<(int, int), HashSet<int>>? Copy(Dictionary<(int, int), HashSet<int>>? state) =>
        state?.ToDictionary(pair => pair.Key, pair => new HashSet<int>(pair.Value));

    private static bool SameState(Dictionary<(int, int), HashSet<int>>? first, Dictionary<(int, int), HashSet<int>>? second)
    {
        if (first is null || second is null) return first is null && second is null;
        return first.Count == second.Count && first.All(pair => second.TryGetValue(pair.Key, out var other) && other.SetEquals(pair.Value));
    }

    private static int DestinationCount(Gw2Sm5Instruction instruction) =>
        SourceOnlyNames.Contains(instruction.Name) ? 0 : TwoDestinationNames.Contains(instruction.Name) ? 2 : 1;

    private void RecordReads(Gw2Sm5Operand operand, bool isSource, int reader, Dictionary<(int Temp, int Component), HashSet<int>> current, Dictionary<(int Temp, int Component), HashSet<int>> reads)
    {
        foreach (var index in operand.Indices)
            if (index.Relative is { } relative) RecordReads(relative, true, reader, current, reads);
        if (!isSource || operand.RegisterType != Gw2ShaderRegisterType.Temp || operand.ComponentCount != 4) return;
        foreach (var component in operand.Swizzle.Distinct())
        {
            var definitions = current.TryGetValue((operand.FirstIndex, component), out var known) ? known : [UnwrittenDefinition];
            reads[(operand.FirstIndex, component)] = definitions;
            foreach (var definition in definitions)
            {
                if (!_readers.TryGetValue(definition, out var readers)) _readers[definition] = readers = [];
                readers.Add(reader);
            }
        }
    }

    private const int MaximumDataflowRounds = 16;

    private static Dictionary<(int, int), HashSet<int>>? Merge(Dictionary<(int, int), HashSet<int>>? first, Dictionary<(int, int), HashSet<int>>? second)
    {
        if (first is null) return Copy(second);
        if (second is null) return Copy(first);
        var merged = new Dictionary<(int, int), HashSet<int>>();
        foreach (var key in first.Keys.Union(second.Keys))
        {
            var definitions = new HashSet<int>();
            definitions.UnionWith(first.TryGetValue(key, out var fromFirst) ? fromFirst : [UnwrittenDefinition]);
            definitions.UnionWith(second.TryGetValue(key, out var fromSecond) ? fromSecond : [UnwrittenDefinition]);
            merged[key] = definitions;
        }

        return merged;
    }

    private int? ReachingDefinition(int reader, Gw2Sm5Operand operand, int destinationComponent)
    {
        if (operand.RegisterType != Gw2ShaderRegisterType.Temp || operand.ComponentCount != 4) return null;
        if (!_readDefinitions.TryGetValue(reader, out var reads)) return null;
        if (!reads.TryGetValue((operand.FirstIndex, operand.Swizzle[destinationComponent]), out var definitions)) return null;
        return definitions.Count == 1 && definitions.First() is var definition and >= 0 ? definition : null;
    }

    private static int? ScalarDestinationComponent(Gw2Sm5Instruction instruction)
    {
        if (instruction.Operands.Count == 0) return null;
        var destination = instruction.Operands[0];
        if (destination.RegisterType != Gw2ShaderRegisterType.Temp || destination.ComponentCount != 4 || BitCount(destination.Mask) != 1) return null;
        return DestinationComponents(destination)[0];
    }

    private static bool IsImmediate(Gw2Sm5Operand operand, int destinationComponent, float value) =>
        operand.RegisterType == Gw2ShaderRegisterType.Immediate32 && operand.Modifier == Gw2Sm5OperandModifier.None &&
        BitConverter.UInt32BitsToSingle(operand.Immediates[operand.ComponentCount == 4 ? operand.Swizzle[destinationComponent] : 0]) == value;

    private static bool IsPlainTemp(Gw2Sm5Operand operand, Gw2Sm5OperandModifier modifier = Gw2Sm5OperandModifier.None) =>
        operand.RegisterType == Gw2ShaderRegisterType.Temp && operand.ComponentCount == 4 && operand.Modifier == modifier;

    private (int Definition, int Temp, int Component)? ThreeMinusTwoTimesInput(int index)
    {
        var instruction = Instructions[index];
        if (instruction is not { Name: "mad", Saturate: false } || ScalarDestinationComponent(instruction) is not { } component) return null;
        var input = instruction.Operands[1];
        if (!IsPlainTemp(input) || !IsImmediate(instruction.Operands[2], component, -2f) || !IsImmediate(instruction.Operands[3], component, 3f)) return null;
        return ReachingDefinition(index, input, component) is { } definition ? (definition, input.FirstIndex, input.Swizzle[component]) : null;
    }

    private (int Definition, int Temp, int Component)? SquareInput(int index)
    {
        var instruction = Instructions[index];
        if (instruction is not { Name: "mul", Saturate: false } || ScalarDestinationComponent(instruction) is not { } component) return null;
        var left = instruction.Operands[1];
        var right = instruction.Operands[2];
        if (!IsPlainTemp(left) || !IsPlainTemp(right) || left.FirstIndex != right.FirstIndex || left.Swizzle[component] != right.Swizzle[component]) return null;
        return ReachingDefinition(index, left, component) is { } definition ? (definition, left.FirstIndex, left.Swizzle[component]) : null;
    }

    private void RecogniseSmoothsteps()
    {
        var inputNames = new Dictionary<(int Definition, int Temp, int Component), string>();
        var partSites = new Dictionary<int, HashSet<int>>();
        for (var index = 0; index < Instructions.Count; index++)
        {
            var instruction = Instructions[index];
            if (ScalarDestinationComponent(instruction) is not { } component) continue;
            var oneMinus = instruction.Name == "mad" && IsPlainTemp(instruction.Operands[1], Gw2Sm5OperandModifier.Negate) && IsImmediate(instruction.Operands[3], component, 1f);
            if (!oneMinus && instruction.Name != "mul") continue;
            if (!IsPlainTemp(instruction.Operands[1], oneMinus ? Gw2Sm5OperandModifier.Negate : Gw2Sm5OperandModifier.None) || !IsPlainTemp(instruction.Operands[2])) continue;
            if (ReachingDefinition(index, instruction.Operands[1], component) is not { } first || ReachingDefinition(index, instruction.Operands[2], component) is not { } second) continue;

            foreach (var (threeMinusTwo, square) in new[] { (first, second), (second, first) })
            {
                if (ThreeMinusTwoTimesInput(threeMinusTwo) is not { } input || SquareInput(square) != input) continue;
                if (!Instructions[input.Definition].Saturate) continue;
                if (!inputNames.TryGetValue(input, out var name))
                {
                    name = "smoothstepInput" + inputNames.Count.ToString(CultureInfo.InvariantCulture);
                    inputNames[input] = name;
                    if (!_namedCopies.TryGetValue(input.Definition, out var copies)) _namedCopies[input.Definition] = copies = [];
                    copies.Add((name, input.Temp, input.Component));
                }

                var call = $"smoothstep(0.0, 1.0, {name})";
                var expression = oneMinus ? $"1.0 - {call}" : call;
                _idiomExpressions[index] = _ => expression;
                foreach (var part in new[] { threeMinusTwo, square })
                {
                    if (!partSites.TryGetValue(part, out var sites)) partSites[part] = sites = [];
                    sites.Add(index);
                }

                break;
            }
        }

        foreach (var (part, sites) in partSites)
            if (_readers.TryGetValue(part, out var readers) && readers.IsSubsetOf(sites)) _skippedInstructions.Add(part);
    }

    private void RecogniseLerps()
    {
        var differenceSites = new Dictionary<int, HashSet<int>>();
        for (var index = 0; index < Instructions.Count; index++)
        {
            var instruction = Instructions[index];
            if (instruction.Name != "mad" || _idiomExpressions.ContainsKey(index) || _skippedInstructions.Contains(index)) continue;
            var destination = instruction.Operands[0];
            if (destination.RegisterType is not (Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.Output)) continue;
            var components = DestinationComponents(destination);
            var start = instruction.Operands[3];
            if (start.Modifier != Gw2Sm5OperandModifier.None) continue;

            foreach (var (weightIndex, differenceIndex) in new[] { (1, 2), (2, 1) })
            {
                var weight = instruction.Operands[weightIndex];
                var difference = instruction.Operands[differenceIndex];
                if (!IsPlainTemp(difference)) continue;
                var definitions = components.Select(component => ReachingDefinition(index, difference, component)).Distinct().ToArray();
                if (definitions is not [{ } definition] || _idiomExpressions.ContainsKey(definition) || _skippedInstructions.Contains(definition)) continue;
                if (LerpEnd(definition, index, start, difference, components) is not { } end) continue;

                _idiomExpressions[index] = sourceComponents =>
                    $"lerp({Source(start, sourceComponents, ValueType.Float)}, {end(sourceComponents)}, {Source(weight, sourceComponents, ValueType.Float)})";
                if (!differenceSites.TryGetValue(definition, out var sites)) differenceSites[definition] = sites = [];
                sites.Add(index);
                break;
            }
        }

        foreach (var (definition, sites) in differenceSites)
            if (_readers.TryGetValue(definition, out var readers) && readers.IsSubsetOf(sites)) _skippedInstructions.Add(definition);
    }

    private Func<IReadOnlyList<int>, string>? LerpEnd(int definitionIndex, int siteIndex, Gw2Sm5Operand start, Gw2Sm5Operand difference, IReadOnlyList<int> components)
    {
        var definition = Instructions[definitionIndex];
        if (definition.Saturate || !StraightLineBetween(definitionIndex, siteIndex) || !OnlyFeedsBetween(definitionIndex, siteIndex)) return null;
        Gw2Sm5Operand[] endOperands;
        Gw2Sm5Operand subtracted;
        switch (definition.Name)
        {
            case "add" when definition.Operands[1].Modifier == Gw2Sm5OperandModifier.Negate:
                (subtracted, endOperands) = (definition.Operands[1], [definition.Operands[2]]);
                break;
            case "add" when definition.Operands[2].Modifier == Gw2Sm5OperandModifier.Negate:
                (subtracted, endOperands) = (definition.Operands[2], [definition.Operands[1]]);
                break;
            case "mad" when definition.Operands[3].Modifier == Gw2Sm5OperandModifier.Negate:
                (subtracted, endOperands) = (definition.Operands[3], [definition.Operands[1], definition.Operands[2]]);
                break;
            default:
                return null;
        }

        foreach (var component in components)
        {
            var definitionComponent = difference.Swizzle[component];
            if ((definition.Operands[0].Mask & (1 << definitionComponent)) == 0) return null;
            if (!SameValue(start, siteIndex, component, subtracted with { Modifier = Gw2Sm5OperandModifier.None }, definitionIndex, definitionComponent)) return null;
        }

        if (endOperands.Any(operand => !UnchangedBetween(operand, definitionIndex, siteIndex))) return null;
        var remapped = endOperands.Select(operand => Remap(operand, difference, components)).ToArray();
        return sourceComponents => string.Join(" * ", remapped.Select(operand => SourceAt(definitionIndex, operand, sourceComponents, ValueType.Float)));
    }

    private static Gw2Sm5Operand Remap(Gw2Sm5Operand operand, Gw2Sm5Operand difference, IReadOnlyList<int> components)
    {
        if (operand.ComponentCount != 4) return operand;
        var swizzle = operand.Swizzle.ToArray();
        foreach (var component in components) swizzle[component] = operand.Swizzle[difference.Swizzle[component]];
        return operand with { Swizzle = swizzle };
    }

    private bool SameValue(Gw2Sm5Operand first, int firstReader, int firstComponent, Gw2Sm5Operand second, int secondReader, int secondComponent)
    {
        if (first.RegisterType != second.RegisterType || first.Modifier != second.Modifier || first.ComponentCount != second.ComponentCount) return false;
        if (first.RegisterType is Gw2ShaderRegisterType.Output or Gw2ShaderRegisterType.OutputDepth or Gw2ShaderRegisterType.IndexableTemp) return false;
        if (first.Indices.Count != second.Indices.Count || first.Indices.Concat(second.Indices).Any(index => index.Relative is not null)) return false;
        if (!first.Indices.Select(index => index.Immediate).SequenceEqual(second.Indices.Select(index => index.Immediate))) return false;
        if (first.RegisterType == Gw2ShaderRegisterType.Immediate32)
            return first.Immediates[first.ComponentCount == 4 ? first.Swizzle[firstComponent] : 0] == second.Immediates[second.ComponentCount == 4 ? second.Swizzle[secondComponent] : 0];
        if (first.ComponentCount == 4 && first.Swizzle[firstComponent] != second.Swizzle[secondComponent]) return false;
        if (first.RegisterType != Gw2ShaderRegisterType.Temp) return true;
        return ReachingDefinition(firstReader, first, firstComponent) is { } firstDefinition && firstDefinition == ReachingDefinition(secondReader, second, secondComponent);
    }

    private bool StraightLineBetween(int first, int last)
    {
        for (var index = first + 1; index < last; index++)
            if (ControlFlowNames.Contains(Instructions[index].Name) || ConditionalControlFlowNames.Contains(Instructions[index].Name)) return false;
        return true;
    }

    private bool OnlyFeedsBetween(int first, int last)
    {
        for (var index = first + 1; index < last; index++)
        {
            if (Gw2Sm5OpCodes.IsDeclaration(Instructions[index].OpCode)) continue;
            if (!_readers.TryGetValue(index, out var readers) || readers.Any(reader => reader <= first || reader > last)) return false;
        }

        return true;
    }

    private bool UnchangedBetween(Gw2Sm5Operand operand, int first, int last, IReadOnlyCollection<int>? removed = null)
    {
        if (operand.Indices.Any(index => index.Relative is not null)) return false;
        if (operand.RegisterType is Gw2ShaderRegisterType.Output or Gw2ShaderRegisterType.OutputDepth or Gw2ShaderRegisterType.IndexableTemp) return false;
        if (operand.RegisterType != Gw2ShaderRegisterType.Temp) return true;
        var read = operand.ComponentCount == 4 ? operand.Swizzle.ToHashSet() : [0];
        for (var index = first + 1; index < last; index++)
        {
            var instruction = Instructions[index];
            if (Gw2Sm5OpCodes.IsDeclaration(instruction.OpCode) || removed?.Contains(index) == true || _skippedInstructions.Contains(index)) continue;
            for (var destinationIndex = 0; destinationIndex < DestinationCount(instruction) && destinationIndex < instruction.Operands.Count; destinationIndex++)
            {
                var destination = instruction.Operands[destinationIndex];
                if (destination.RegisterType == Gw2ShaderRegisterType.Temp && destination.FirstIndex == operand.FirstIndex && DestinationComponents(destination).Any(read.Contains)) return false;
            }
        }

        return true;
    }

    private const string AwayFromZeroFunction =
        "float AwayFromZero(float value)\n{\n    return abs(value) > 1e-37 ? value : (sign(value) != 0.0 ? sign(value) * 1e-37 : 1e-37);\n}\n\n";

    private const float SmallestPowBase = 1e-37f;

    private bool _usesAwayFromZero;

    private void RecogniseAwayFromZero()
    {
        for (var index = 0; index < Instructions.Count; index++)
        {
            var select = Instructions[index];
            if (select is not { Name: "movc", Saturate: false } || ScalarDestinationComponent(select) is not { } component) continue;
            var condition = select.Operands[1];
            var value = select.Operands[2];
            var fallback = select.Operands[3];
            if (!IsPlainTemp(condition) || !IsPlainTemp(fallback)) continue;
            if (ReachingDefinition(index, condition, component) is not { } largeIndex || ReachingDefinition(index, fallback, component) is not { } fallbackIndex) continue;

            var large = Instructions[largeIndex];
            var largeComponent = condition.Swizzle[component];
            if (large.Name != "lt" || !IsImmediate(large.Operands[1], largeComponent, SmallestPowBase)) continue;
            var compared = large.Operands[2];
            var comparesValue = SameValue(value, index, component, compared, largeIndex, largeComponent) ||
                (value.Modifier == Gw2Sm5OperandModifier.None && compared.Modifier == Gw2Sm5OperandModifier.Absolute &&
                 SameValue(value, index, component, compared with { Modifier = Gw2Sm5OperandModifier.None }, largeIndex, largeComponent));
            if (!comparesValue) continue;

            var fallbackSelect = Instructions[fallbackIndex];
            var fallbackComponent = fallback.Swizzle[component];
            if (fallbackSelect is not { Name: "movc", Saturate: false } || !IsImmediate(fallbackSelect.Operands[3], fallbackComponent, SmallestPowBase)) continue;
            var nonZero = fallbackSelect.Operands[1];
            var scaled = fallbackSelect.Operands[2];
            if (!IsPlainTemp(nonZero) || !IsPlainTemp(scaled)) continue;
            if (ReachingDefinition(fallbackIndex, nonZero, fallbackComponent) is not { } nonZeroIndex || ReachingDefinition(fallbackIndex, scaled, fallbackComponent) is not { } scaledIndex) continue;

            var nonZeroTest = Instructions[nonZeroIndex];
            var nonZeroComponent = nonZero.Swizzle[fallbackComponent];
            if (nonZeroTest.Name != "ne" || !IsPlainTemp(nonZeroTest.Operands[1]) || !IsImmediate(nonZeroTest.Operands[2], nonZeroComponent, 0f)) continue;
            if (ReachingDefinition(nonZeroIndex, nonZeroTest.Operands[1], nonZeroComponent) is not { } signIndex) continue;

            var scale = Instructions[scaledIndex];
            var scaledComponent = scaled.Swizzle[fallbackComponent];
            if (scale is not { Name: "mul", Saturate: false } || !IsPlainTemp(scale.Operands[1]) || !IsImmediate(scale.Operands[2], scaledComponent, SmallestPowBase)) continue;
            if (ReachingDefinition(scaledIndex, scale.Operands[1], scaledComponent) != signIndex) continue;

            if (SignParts(signIndex, value, index, component) is not { } signParts || !UnchangedBetween(value, signParts.Min(), index)) continue;

            _idiomExpressions[index] = components => $"AwayFromZero({Source(value, components, ValueType.Float)})";
            _usesAwayFromZero = true;
            var parts = new HashSet<int>(signParts) { largeIndex, nonZeroIndex, scaledIndex, fallbackIndex, index };
            foreach (var part in parts.Where(part => part != index))
                if (_readers.TryGetValue(part, out var readers) && readers.IsSubsetOf(parts)) _skippedInstructions.Add(part);
        }
    }

    private HashSet<int>? SignParts(int signIndex, Gw2Sm5Operand value, int siteIndex, int siteComponent)
    {
        var sign = Instructions[signIndex];
        if (sign.Name != "itof" || ScalarDestinationComponent(sign) is not { } signComponent) return null;
        var mask = sign.Operands[1];
        if (IsPlainTemp(mask, Gw2Sm5OperandModifier.Negate))
            return ZeroCompare(signIndex, mask, signComponent, true, value, siteIndex, siteComponent) is { } positive ? [signIndex, positive] : null;

        if (!IsPlainTemp(mask) || ReachingDefinition(signIndex, mask, signComponent) is not { } differenceIndex) return null;
        var difference = Instructions[differenceIndex];
        var differenceComponent = mask.Swizzle[signComponent];
        if (difference.Name != "iadd") return null;
        foreach (var (negatedIndex, plainIndex) in new[] { (1, 2), (2, 1) })
        {
            if (!IsPlainTemp(difference.Operands[negatedIndex], Gw2Sm5OperandModifier.Negate) || !IsPlainTemp(difference.Operands[plainIndex])) continue;
            if (ZeroCompare(differenceIndex, difference.Operands[negatedIndex], differenceComponent, true, value, siteIndex, siteComponent) is not { } positive) continue;
            if (ZeroCompare(differenceIndex, difference.Operands[plainIndex], differenceComponent, false, value, siteIndex, siteComponent) is not { } negative) continue;
            return [signIndex, differenceIndex, positive, negative];
        }

        return null;
    }

    private int? ZeroCompare(int reader, Gw2Sm5Operand mask, int readerComponent, bool zeroFirst, Gw2Sm5Operand value, int siteIndex, int siteComponent)
    {
        if (ReachingDefinition(reader, mask, readerComponent) is not { } compareIndex) return null;
        var compare = Instructions[compareIndex];
        var compareComponent = mask.Swizzle[readerComponent];
        if (compare.Name != "lt") return null;
        var (zero, compared) = zeroFirst ? (compare.Operands[1], compare.Operands[2]) : (compare.Operands[2], compare.Operands[1]);
        if (!IsImmediate(zero, compareComponent, 0f) || !SameValue(value, siteIndex, siteComponent, compared, compareIndex, compareComponent)) return null;
        return compareIndex;
    }

    private void RecognisePows()
    {
        var candidates = new List<(int Exponential, int Product, int Log, Gw2Sm5Operand Base, Gw2Sm5Operand Exponent)>();
        for (var index = 0; index < Instructions.Count; index++)
        {
            var exponential = Instructions[index];
            if (exponential.Name != "exp" || exponential.Operands[0].RegisterType is not (Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.Output)) continue;
            var components = DestinationComponents(exponential.Operands[0]);
            var scaled = exponential.Operands[1];
            if (!IsPlainTemp(scaled)) continue;
            if (components.Select(component => ReachingDefinition(index, scaled, component)).Distinct().ToArray() is not [{ } productIndex]) continue;
            var product = Instructions[productIndex];
            if (product is not { Name: "mul", Saturate: false } || !_readers.TryGetValue(productIndex, out var productReaders) || !productReaders.SetEquals([index])) continue;

            foreach (var (logarithmIndex, exponentIndex) in new[] { (1, 2), (2, 1) })
            {
                var logarithm = Remap(product.Operands[logarithmIndex], scaled, components);
                var exponent = Remap(product.Operands[exponentIndex], scaled, components);
                if (!IsPlainTemp(logarithm)) continue;
                if (components.Select(component => ReachingDefinition(productIndex, logarithm, component)).Distinct().ToArray() is not [{ } logIndex]) continue;
                if (Instructions[logIndex] is not { Name: "log", Saturate: false } log || !StraightLineBetween(logIndex, index) || !UnchangedBetween(exponent, productIndex, index)) continue;
                candidates.Add((index, productIndex, logIndex, Remap(log.Operands[1], logarithm, components), exponent));
                break;
            }
        }

        foreach (var group in candidates.GroupBy(candidate => candidate.Log))
        {
            var products = group.Select(candidate => candidate.Product).ToHashSet();
            if (!_readers.TryGetValue(group.Key, out var logReaders) || !logReaders.IsSubsetOf(products)) continue;
            var removed = new HashSet<int>(products) { group.Key };
            if (group.Any(candidate => !UnchangedBetween(Instructions[group.Key].Operands[1], group.Key, candidate.Exponential, removed))) continue;

            foreach (var candidate in group)
            {
                _idiomExpressions[candidate.Exponential] = sourceComponents =>
                    $"pow({SourceAt(candidate.Log, candidate.Base, sourceComponents, ValueType.Float)}, {SourceAt(candidate.Product, candidate.Exponent, sourceComponents, ValueType.Float)})";
                _skippedInstructions.Add(candidate.Product);
            }

            _skippedInstructions.Add(group.Key);
        }
    }

    private void RecogniseReflects()
    {
        for (var index = 0; index < Instructions.Count; index++)
        {
            var instruction = Instructions[index];
            if (instruction.Name != "mad" || _idiomExpressions.ContainsKey(index)) continue;
            var destination = instruction.Operands[0];
            if (destination.RegisterType is not (Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.Output)) continue;
            var components = DestinationComponents(destination);
            var incident = instruction.Operands[3];
            foreach (var (normalIndex, scaleIndex) in new[] { (1, 2), (2, 1) })
            {
                var normal = instruction.Operands[normalIndex];
                var scale = instruction.Operands[scaleIndex];
                if (!IsPlainTemp(scale, Gw2Sm5OperandModifier.Negate)) continue;
                if (components.Select(component => ReachingDefinition(index, scale, component)).Distinct().ToArray() is not [{ } doubledIndex]) continue;
                if (components.Select(component => scale.Swizzle[component]).Distinct().ToArray() is not [var doubledComponent]) continue;
                var doubled = Instructions[doubledIndex];
                if (doubled is not { Name: "add", Saturate: false } || !IsPlainTemp(doubled.Operands[1]) || !SameValue(doubled.Operands[1], doubledIndex, doubledComponent, doubled.Operands[2], doubledIndex, doubledComponent)) continue;
                if (ReachingDefinition(doubledIndex, doubled.Operands[1], doubledComponent) is not { } dotIndex) continue;
                var dot = Instructions[dotIndex];
                if (dot.Saturate || dot.Name != $"dp{components.Count}") continue;
                var matches = true;
                for (var position = 0; position < components.Count && matches; position++)
                    matches = SameValue(incident, index, components[position], dot.Operands[1], dotIndex, position) &&
                              SameValue(normal, index, components[position], dot.Operands[2], dotIndex, position);
                if (!matches) continue;

                _idiomExpressions[index] = sourceComponents =>
                    $"reflect({Source(incident, sourceComponents, ValueType.Float)}, {Source(normal, sourceComponents, ValueType.Float)})";
                var parts = new HashSet<int> { dotIndex, doubledIndex, index };
                foreach (var part in new[] { dotIndex, doubledIndex })
                    if (_readers.TryGetValue(part, out var readers) && readers.IsSubsetOf(parts)) _skippedInstructions.Add(part);
                break;
            }
        }
    }

    private void RecogniseNormalizes()
    {
        for (var index = 0; index < Instructions.Count; index++)
        {
            var instruction = Instructions[index];
            if (instruction is not { Name: "mul", Saturate: false } || _idiomExpressions.ContainsKey(index) || _skippedInstructions.Contains(index)) continue;
            var destination = instruction.Operands[0];
            if (destination.RegisterType is not (Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.Output)) continue;
            var components = DestinationComponents(destination);
            if (components.Count is < 2 or > 4) continue;
            foreach (var (lengthIndex, vectorIndex) in new[] { (1, 2), (2, 1) })
            {
                var inverseLength = instruction.Operands[lengthIndex];
                var vector = instruction.Operands[vectorIndex];
                if (!IsPlainTemp(inverseLength) || vector.Modifier != Gw2Sm5OperandModifier.None) continue;
                if (components.Select(component => ReachingDefinition(index, inverseLength, component)).Distinct().ToArray() is not [{ } rsqIndex]) continue;
                if (components.Select(component => inverseLength.Swizzle[component]).Distinct().ToArray() is not [var rsqComponent]) continue;
                var rsq = Instructions[rsqIndex];
                if (rsq is not { Name: "rsq", Saturate: false } || !IsPlainTemp(rsq.Operands[1])) continue;
                if (ReachingDefinition(rsqIndex, rsq.Operands[1], rsqComponent) is not { } dotIndex) continue;
                var dot = Instructions[dotIndex];
                if (dot.Saturate || dot.Name != $"dp{components.Count}") continue;
                var matches = true;
                for (var position = 0; position < components.Count && matches; position++)
                    matches = SameValue(vector, index, components[position], dot.Operands[1], dotIndex, position) &&
                              SameValue(vector, index, components[position], dot.Operands[2], dotIndex, position);
                var parts = new HashSet<int> { dotIndex, rsqIndex, index };
                if (!matches || new[] { rsqIndex, dotIndex }.Any(part => !_readers.TryGetValue(part, out var readers) || !readers.IsSubsetOf(parts))) continue;

                _idiomExpressions[index] = sourceComponents => $"normalize({Source(vector, sourceComponents, ValueType.Float)})";
                _skippedInstructions.UnionWith([rsqIndex, dotIndex]);
                break;
            }
        }
    }

    private void TranslateIdiom(int index)
    {
        _currentReader = index;
        var instruction = Instructions[index];
        if (_matrixDeclarations.TryGetValue(index, out var declaration))
            foreach (var declarationLine in declaration().Split('\n')) Line(declarationLine);
        if (_skippedInstructions.Contains(index)) return;
        if (_idiomExpressions.TryGetValue(index, out var expression))
        {
            var destination = _idiomDestinations.GetValueOrDefault(index, instruction.Operands[0]);
            var components = DestinationComponents(destination);
            EmitAssignment(instruction, destination, components, expression(components), ValueType.Float);
        }
        else if (!TranslateClip(index)) Translate(instruction);

        if (!_namedCopies.TryGetValue(index, out var copies)) return;
        foreach (var (name, temp, component) in copies)
        {
            var fallback = TempDestinationText(index, temp, [component]);
            Line($"float {name} = {TempReference(temp, [component], Gw2Sm5OperandModifier.None, ValueType.Float, false, fallback)};");
        }
    }
}
