using System.Globalization;
using Gw2.Dat.Content;

namespace Gw2.Dat.Shaders;

public sealed partial class ShaderDecompiler
{
    private const char VariableStart = '\u0003';
    private const char VariableEnd = '\u0004';

    private static readonly HashSet<string> ComparisonNames = ["lt", "ge", "eq", "ne", "ilt", "ige", "ieq", "ine", "ult", "uge", "dlt", "dge", "deq", "dne"];

    private readonly Dictionary<int, Dictionary<(int Temp, int Component), HashSet<int>>> _reachingBefore = [];
    private readonly Dictionary<(int Definition, int Temp), (int Definition, int Temp)> _variableParents = [];
    private readonly Dictionary<(int Definition, int Temp), string> _variableNames = [];
    private readonly List<string> _variableBaseNames = [];
    private readonly List<int> _variableWidths = [];
    private readonly Dictionary<(int Definition, int Temp), int[]> _variableLayouts = [];
    private int _currentReader = -1;

    private bool HasVariables => _variableNames.Count > 0;

    private (int Definition, int Temp) VariableRoot((int Definition, int Temp) node)
    {
        if (!_variableParents.TryGetValue(node, out var parent)) _variableParents[node] = parent = node;
        if (parent == node) return node;
        var root = VariableRoot(parent);
        _variableParents[node] = root;
        return root;
    }

    private void JoinVariables((int Definition, int Temp) first, (int Definition, int Temp) second)
    {
        var firstRoot = VariableRoot(first);
        var secondRoot = VariableRoot(second);
        if (firstRoot == secondRoot) return;
        if (firstRoot.Definition < secondRoot.Definition) _variableParents[secondRoot] = firstRoot;
        else _variableParents[firstRoot] = secondRoot;
    }

    private void BuildVariables()
    {
        foreach (var (_, reads) in _readDefinitions)
            foreach (var group in reads.GroupBy(read => read.Key.Temp))
            {
                var nodes = group.SelectMany(read => read.Value.Select(definition => (definition, group.Key))).ToArray();
                foreach (var node in nodes.Skip(1)) JoinVariables(nodes[0], node);
                if (nodes.Length > 0) VariableRoot(nodes[0]);
            }

        for (var index = 0; index < Instructions.Count; index++)
        {
            var instruction = Instructions[index];
            if (Gw2Sm5OpCodes.IsDeclaration(instruction.OpCode)) continue;
            for (var destinationIndex = 0; destinationIndex < DestinationCount(instruction) && destinationIndex < instruction.Operands.Count; destinationIndex++)
                if (instruction.Operands[destinationIndex] is { RegisterType: Gw2ShaderRegisterType.Temp, ComponentCount: 4 } destination)
                    VariableRoot((index, destination.FirstIndex));
        }

        var usedComponents = new Dictionary<(int Definition, int Temp), SortedSet<int>>();
        void Use((int Definition, int Temp) node, int component)
        {
            var root = VariableRoot(node);
            if (!usedComponents.TryGetValue(root, out var components)) usedComponents[root] = components = [];
            components.Add(component);
        }

        foreach (var (_, reads) in _readDefinitions)
            foreach (var ((temp, component), definitions) in reads)
                foreach (var definition in definitions) Use((definition, temp), component);
        for (var index = 0; index < Instructions.Count; index++)
        {
            var instruction = Instructions[index];
            if (Gw2Sm5OpCodes.IsDeclaration(instruction.OpCode)) continue;
            for (var destinationIndex = 0; destinationIndex < DestinationCount(instruction) && destinationIndex < instruction.Operands.Count; destinationIndex++)
                if (instruction.Operands[destinationIndex] is { RegisterType: Gw2ShaderRegisterType.Temp, ComponentCount: 4 } destination)
                    foreach (var component in DestinationComponents(destination)) Use((index, destination.FirstIndex), component);
        }

        var roots = _variableParents.Keys.GroupBy(VariableRoot).OrderBy(group => group.Key.Definition).ThenBy(group => group.Key.Temp);
        foreach (var group in roots)
        {
            var components = usedComponents.TryGetValue(group.Key, out var used) ? used.ToArray() : [0];
            _variableLayouts[group.Key] = components;
            _variableWidths.Add(components.Length);
            _variableTypes.Add(VariableType(group.Select(node => node.Definition).Where(definition => definition >= 0).ToArray()));
            _variableNames[group.Key] = $"{VariableStart}{_variableBaseNames.Count.ToString(CultureInfo.InvariantCulture)}{VariableEnd}";
            _variableBaseNames.Add(VariableBaseName(group.Select(node => node.Definition).Where(definition => definition >= 0).ToArray()));
        }
    }

    private static readonly HashSet<string> IntResultNames = ["ftoi", "iadd", "imad", "imax", "imin", "ineg", "ishl", "ishr", "imul", "ibfe", "firstbit_shi"];
    private static readonly HashSet<string> UintResultNames = ["ftou", "umad", "umax", "umin", "and", "or", "xor", "not", "ushr", "umul", "udiv", "countbits", "firstbit_hi", "firstbit_lo", "bfrev", "f32tof16", "ubfe", "bfi"];
    private const float SmallestFloatPatternMagnitude = 1e-6f;
    private const float LargestFloatPatternMagnitude = 1e6f;

    private readonly List<ValueType> _variableTypes = [];

    private ValueType? IntegerResultType(int definition)
    {
        var instruction = Instructions[definition];
        if (ComparisonNames.Contains(instruction.Name)) return ValueType.Uint;
        if (IntResultNames.Contains(instruction.Name)) return ValueType.Int;
        if (!UintResultNames.Contains(instruction.Name)) return null;
        if (instruction.Name == "and" && instruction.Operands.Any(IsFloatPattern)) return null;
        return ValueType.Uint;
    }

    private bool IsNeutralCopy(int definition)
    {
        var instruction = Instructions[definition];
        if (instruction.Name is not ("mov" or "movc") || instruction.Saturate) return false;
        var values = instruction.Name == "mov" ? instruction.Operands.Skip(1) : instruction.Operands.Skip(2);
        return values.All(operand => operand.Modifier == Gw2Sm5OperandModifier.None && (operand.RegisterType == Gw2ShaderRegisterType.Temp || (operand.RegisterType == Gw2ShaderRegisterType.Immediate32 && !IsFloatPattern(operand))));
    }

    private static bool IsFloatPattern(Gw2Sm5Operand operand)
    {
        if (operand.RegisterType != Gw2ShaderRegisterType.Immediate32) return false;
        return operand.Immediates.Any(bits =>
        {
            var magnitude = Math.Abs(BitConverter.UInt32BitsToSingle(bits));
            return float.IsFinite(magnitude) && magnitude >= SmallestFloatPatternMagnitude && magnitude <= LargestFloatPatternMagnitude;
        });
    }

    private ValueType VariableType(IReadOnlyList<int> definitions)
    {
        var deciding = definitions.Where(definition => !IsNeutralCopy(definition)).ToArray();
        if (deciding.Length == 0) return ValueType.Float;
        if (deciding.All(definition => ComparisonNames.Contains(Instructions[definition].Name))) return ValueType.Bool;
        var types = deciding.Select(IntegerResultType).ToArray();
        if (types.Any(type => type is null)) return ValueType.Float;
        return types.All(type => type == ValueType.Int) ? ValueType.Int : ValueType.Uint;
    }

    private ValueType TempValueType(int temp, IReadOnlyList<int> registerComponents)
    {
        if (!HasVariables || _currentReader < 0) return ValueType.Float;
        var reaching = _reachingBefore[_currentReader];
        var definition = registerComponents
            .Select(component => reaching.TryGetValue((temp, component), out var definitions) ? definitions.First() : UnwrittenDefinition)
            .FirstOrDefault(UnwrittenDefinition);
        return RootType(VariableRoot((definition, temp)));
    }

    private ValueType DestinationValueType(Gw2Sm5Operand destination) =>
        HasVariables && _currentReader >= 0 && destination is { RegisterType: Gw2ShaderRegisterType.Temp, ComponentCount: 4 } && !_matrixRowAliases.ContainsKey(VariableRoot((_currentReader, destination.FirstIndex)))
            ? RootType(VariableRoot((_currentReader, destination.FirstIndex)))
            : ValueType.Float;

    private ValueType RootType((int Definition, int Temp) root) =>
        _variableNames.TryGetValue(root, out var marker) ? _variableTypes[int.Parse(marker.AsSpan(1, marker.Length - 2), CultureInfo.InvariantCulture)] : ValueType.Float;

    private string VariableBaseName(IReadOnlyList<int> definitions)
    {
        foreach (var definition in definitions.Order())
            if (Instructions[definition].Operands.FirstOrDefault(operand => operand.RegisterType == Gw2ShaderRegisterType.Resource) is { } resource &&
                UniformSamplerName(resource.FirstIndex) is { } sampler)
                return sampler + "Sample";
        if (definitions.Count > 0 && definitions.All(definition => ComparisonNames.Contains(Instructions[definition].Name))) return "condition";
        return "value";
    }

    private string TempSourceName(int temp, IEnumerable<int> registerComponents) =>
        TempSourceText(temp, registerComponents.Take(1).ToArray()).Split('.')[0];

    private string TempSourceText(int temp, IReadOnlyList<int> registerComponents)
    {
        if (!HasVariables || _currentReader < 0) return RegisterText(temp, registerComponents);
        var reaching = _reachingBefore[_currentReader];
        var definition = registerComponents
            .Select(component => reaching.TryGetValue((temp, component), out var definitions) ? definitions.First() : UnwrittenDefinition)
            .FirstOrDefault(UnwrittenDefinition);
        return VariableText(VariableRoot((definition, temp)), registerComponents);
    }

    private string TempDestinationText(int definition, int temp, IReadOnlyList<int> registerComponents) =>
        HasVariables ? VariableText(VariableRoot((definition, temp)), registerComponents) : RegisterText(temp, registerComponents);

    private static string RegisterText(int temp, IReadOnlyList<int> registerComponents) =>
        $"r{temp.ToString(CultureInfo.InvariantCulture)}.{string.Concat(registerComponents.Select(component => Components[component]))}";

    private string VariableText((int Definition, int Temp) root, IReadOnlyList<int> registerComponents)
    {
        if (_matrixRowAliases.TryGetValue(root, out var alias))
        {
            var rowText = $"{alias.Name}[{alias.Row}]";
            return registerComponents.SequenceEqual(Enumerable.Range(0, alias.Columns)) ? rowText : $"{rowText}.{string.Concat(registerComponents.Select(component => Components[component]))}";
        }

        var layout = _variableLayouts[root];
        if (registerComponents.SequenceEqual(layout)) return _variableNames[root];
        return $"{_variableNames[root]}.{string.Concat(registerComponents.Select(component => Components[Array.IndexOf(layout, component)]))}";
    }

    private (string Body, IReadOnlyList<(string Name, int Width, ValueType Type)> Variables) NameVariables(string body)
    {
        var names = new Dictionary<int, string>();
        var declared = new List<(string Name, int Width, ValueType Type)>();
        var counts = new Dictionary<string, int>();
        var named = VariableMarker().Replace(body, match =>
        {
            var id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (names.TryGetValue(id, out var name)) return name;
            var baseName = _variableBaseNames[id];
            var count = counts.GetValueOrDefault(baseName);
            counts[baseName] = count + 1;
            names[id] = baseName + count.ToString(CultureInfo.InvariantCulture);
            declared.Add((names[id], _variableWidths[id], _variableTypes[id]));
            return names[id];
        });
        return (named, declared);
    }

    [System.Text.RegularExpressions.GeneratedRegex("\u0003(\\d+)\u0004")]
    private static partial System.Text.RegularExpressions.Regex VariableMarker();

    private string SourceAt(int reader, Gw2Sm5Operand operand, IReadOnlyList<int> components, ValueType type)
    {
        var current = _currentReader;
        _currentReader = reader;
        try
        {
            return Source(operand, components, type);
        }
        finally
        {
            _currentReader = current;
        }
    }
}
