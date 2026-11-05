using System.Globalization;
using Gw2.Dat.Content;

namespace Gw2.Dat.Shaders;

public sealed partial class ShaderDecompiler
{
    private readonly Dictionary<int, Gw2Sm5Operand> _idiomDestinations = [];

    private sealed record MatrixTerm(int Reader, Gw2Sm5Operand Matrix, Gw2Sm5Operand? Weight);

    private sealed record MatrixColumn(string Key, int Column, IReadOnlyList<MatrixTerm> Terms, IReadOnlyList<int> Chain);

    private sealed record DotMember(int Index, int Component, MatrixColumn Column);

    private void RecogniseMatrixMultiplies()
    {
        var grouped = new HashSet<int>();
        var groups = new List<(List<DotMember> Members, int Size, int VectorPosition)>();
        for (var first = 0; first < Instructions.Count; first++)
        {
            if (grouped.Contains(first) || DotSize(first) is not { } size) continue;
            foreach (var vectorPosition in new[] { 1, 2 })
            {
                var members = DotGroup(first, size, vectorPosition, grouped);
                if (members is null) continue;
                groups.Add((members, size, vectorPosition));
                grouped.UnionWith(members.Select(member => member.Index));
                break;
            }
        }

        while (groups.FirstOrDefault(group => !BlendReadersGrouped(group.Members, grouped)) is { Members: not null } unsupported)
        {
            groups.Remove(unsupported);
            grouped.ExceptWith(unsupported.Members.Select(member => member.Index));
        }

        var sharedBlends = groups.SelectMany(group => group.Members.Select(member => member.Column).Where(column => column.Chain.Count > 0).Select(column => column.Key).Distinct())
            .GroupBy(key => key).Where(uses => uses.Count() > 1).Select(uses => uses.Key).ToHashSet();
        var declaredBlends = new Dictionary<string, string>();
        foreach (var (members, size, vectorPosition) in groups)
        {
            var key = members[0].Column.Key;
            if (sharedBlends.Contains(key) && !declaredBlends.ContainsKey(key))
            {
                var name = $"blendedMatrix{declaredBlends.Count}";
                declaredBlends[key] = name;
                var declaringColumn = members[0].Column;
                var declaration = members[^1].Index;
                var previous = _matrixDeclarations.GetValueOrDefault(declaration);
                _matrixDeclarations[declaration] = () => (previous is null ? string.Empty : previous() + "\n") + $"float4x4 {name} = {MatrixText(declaringColumn)};";
            }

            RegisterMatrixMultiply(members, size, vectorPosition, declaredBlends.GetValueOrDefault(key));
        }

        _pendingReferences.Clear();
    }

    private bool BlendReadersGrouped(List<DotMember> members, HashSet<int> grouped) =>
        members.All(member => member.Column.Chain.Count == 0 || (_readers.TryGetValue(member.Column.Chain[0], out var readers) && readers.IsSubsetOf(grouped)));

    private int? DotSize(int index)
    {
        var instruction = Instructions[index];
        if (instruction.Saturate || _idiomExpressions.ContainsKey(index) || _skippedInstructions.Contains(index)) return null;
        if (instruction.Name is not ("dp3" or "dp4")) return null;
        var destination = instruction.Operands[0];
        if (destination.RegisterType is not (Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.Output) || destination.ComponentCount != 4 || BitCount(destination.Mask) != 1) return null;
        return instruction.Name == "dp3" ? 3 : 4;
    }

    private List<DotMember>? DotGroup(int first, int size, int vectorPosition, HashSet<int> grouped)
    {
        var destination = Instructions[first].Operands[0];
        var vector = Instructions[first].Operands[vectorPosition];
        if (MatrixColumnOf(first, Instructions[first].Operands[3 - vectorPosition], size) is not { } firstColumn) return null;
        var members = new List<DotMember> { new(first, DestinationComponents(destination)[0], firstColumn) };
        for (var next = first + 1; next < Instructions.Count && members.Count < MatrixColumns; next++)
        {
            var instruction = Instructions[next];
            if (ControlFlowNames.Contains(instruction.Name) || ConditionalControlFlowNames.Contains(instruction.Name)) break;
            if (grouped.Contains(next) || DotSize(next) != size) continue;
            var candidate = instruction.Operands[0];
            if (candidate.RegisterType != destination.RegisterType || candidate.FirstIndex != destination.FirstIndex) continue;
            var component = DestinationComponents(candidate)[0];
            if (members.Any(member => member.Component == component)) break;
            var sameVector = Enumerable.Range(0, size).All(position => SameValue(instruction.Operands[vectorPosition], next, position, vector, first, position));
            if (!sameVector) continue;
            if (MatrixColumnOf(next, instruction.Operands[3 - vectorPosition], size) is not { } column || column.Key != firstColumn.Key) continue;
            if (members.Any(member => member.Column.Column == column.Column)) continue;
            members.Add(new(next, component, column));
        }

        if (members.Count < 2) return null;
        var last = members[^1].Index;
        foreach (var member in members.SkipLast(1))
            if (_readers.TryGetValue(member.Index, out var readers) && readers.Any(reader => reader <= last)) return null;
        return members;
    }

    private MatrixColumn? MatrixColumnOf(int reader, Gw2Sm5Operand operand, int size)
    {
        if (!Enumerable.Range(0, size).All(position => operand.Swizzle[position] == position) || operand.Modifier != Gw2Sm5OperandModifier.None) return null;
        if (MatrixAt(reader, operand) is var (matrix, column))
            return new MatrixColumn(matrix, column, [new MatrixTerm(reader, operand, null)], []);
        if (!IsPlainTemp(operand)) return null;
        if (Enumerable.Range(0, size).Select(position => ReachingDefinition(reader, operand, position)).Distinct().ToArray() is not [{ } definition]) return null;
        if (!_readers.TryGetValue(definition, out var blendReaders) || !blendReaders.All(blendReader => DotSize(blendReader) is not null)) return null;
        return BlendedColumn(definition, size);
    }

    private MatrixColumn? BlendedColumn(int definition, int size)
    {
        var terms = new List<MatrixTerm>();
        var chain = new List<int>();
        var column = -1;
        var index = definition;
        while (true)
        {
            var instruction = Instructions[index];
            if (instruction.Saturate || instruction.Name is not ("mul" or "mad")) return null;
            if (!DestinationComponents(instruction.Operands[0]).Take(size).SequenceEqual(Enumerable.Range(0, size))) return null;
            MatrixTerm? term = null;
            foreach (var matrixPosition in new[] { 1, 2 })
            {
                var matrixOperand = instruction.Operands[matrixPosition];
                var weight = instruction.Operands[3 - matrixPosition];
                if (matrixOperand.Modifier != Gw2Sm5OperandModifier.None || !Enumerable.Range(0, size).All(position => matrixOperand.Swizzle[position] == position)) continue;
                if (MatrixAt(index, matrixOperand) is not var (_, termColumn) || (column >= 0 && termColumn != column)) continue;
                if (weight.ComponentCount == 4 && Enumerable.Range(0, size).Select(position => weight.Swizzle[position]).Distinct().Count() != 1) continue;
                column = termColumn;
                term = new MatrixTerm(index, matrixOperand, weight);
                break;
            }

            if (term is null) return null;
            terms.Add(term);
            chain.Add(index);
            if (instruction.Name == "mul") break;
            var rest = instruction.Operands[3];
            if (!IsPlainTemp(rest)) return null;
            if (Enumerable.Range(0, size).Select(position => ReachingDefinition(index, rest, position)).Distinct().ToArray() is not [{ } previous]) return null;
            if (!_readers.TryGetValue(previous, out var restReaders) || !restReaders.SetEquals([index])) return null;
            index = previous;
        }

        var key = string.Join(" + ", terms.Select(term => $"{MatrixAt(term.Reader, term.Matrix)!.Value.Matrix}*{WeightKey(term)}"));
        return new MatrixColumn(key, column, terms, chain);
    }

    private string WeightKey(MatrixTerm term)
    {
        var weight = term.Weight!;
        var component = weight.ComponentCount == 4 ? weight.Swizzle[0] : 0;
        var definition = weight.RegisterType == Gw2ShaderRegisterType.Temp ? ReachingDefinition(term.Reader, weight, 0)?.ToString(CultureInfo.InvariantCulture) : string.Empty;
        return weight.RegisterType == Gw2ShaderRegisterType.Immediate32
            ? $"#{weight.Immediates[component]}"
            : $"{weight.RegisterType}{weight.FirstIndex}.{component}@{definition}";
    }

    private (string Matrix, int Column)? MatrixAt(int reader, Gw2Sm5Operand operand)
    {
        var current = _currentReader;
        _currentReader = reader;
        try
        {
            return MatrixRegister(operand);
        }
        finally
        {
            _currentReader = current;
        }
    }

    private void RegisterMatrixMultiply(List<DotMember> members, int size, int vectorPosition, string? blendName)
    {
        var last = members[^1].Index;
        var destination = Instructions[last].Operands[0];
        var mask = members.Aggregate(0, (combined, member) => combined | (1 << member.Component));
        _idiomDestinations[last] = destination with { Mask = mask };
        var vector = Instructions[last].Operands[vectorPosition];
        var columnByComponent = members.ToDictionary(member => member.Component, member => member.Column.Column);
        var column = members[0].Column;
        _idiomExpressions[last] = components =>
        {
            var matrix = blendName ?? MatrixText(column);
            if (size == 3) matrix = $"(float3x3){matrix}";
            var vectorText = Source(vector, Enumerable.Range(0, size).ToArray(), ValueType.Float);
            return $"mul({vectorText}, {matrix}).{string.Concat(components.Select(component => Components[columnByComponent[component]]))}";
        };
        foreach (var member in members)
        {
            if (member.Index != last) _skippedInstructions.Add(member.Index);
            _skippedInstructions.UnionWith(member.Column.Chain);
        }
    }

    private string MatrixText(MatrixColumn column)
    {
        if (column.Terms is [{ Weight: null } direct]) return MatrixAt(direct.Reader, direct.Matrix)!.Value.Matrix;
        var bytecodeOrder = column.Terms.Reverse().ToList();
        var sourceOrder = bytecodeOrder.Count > 1 ? new[] { bytecodeOrder[1], bytecodeOrder[0] }.Concat(bytecodeOrder.Skip(2)).ToList() : bytecodeOrder;
        var terms = sourceOrder.Select(term =>
        {
            return $"{MatrixAt(term.Reader, term.Matrix)!.Value.Matrix} * {SourceAt(term.Reader, term.Weight!, [0], ValueType.Float)}";
        });
        return $"({string.Join(" + ", terms)})";
    }

    private sealed record WeightedRow(IReadOnlyList<int> Members, string Matrix, int Row, string WeightKey, Gw2Sm5Operand Destination, Gw2Sm5Operand Weight, Gw2Sm5Operand MatrixOperand)
    {
        public int Last => Members[^1];
    }

    private readonly Dictionary<(int Definition, int Temp), (string Name, int Row, int Columns)> _matrixRowAliases = [];
    private readonly Dictionary<int, Func<string>> _matrixDeclarations = [];
    private int _weightedMatrixCount;

    private void CombineWeightedMatrices(List<WeightedRow> rows)
    {
        for (var start = 0; start < rows.Count; start++)
        {
            var first = rows[start];
            if (first.Row != 0) continue;
            var group = new List<WeightedRow> { first };
            for (var next = start + 1; next < rows.Count && group.Count < MatrixColumns; next++)
            {
                var row = rows[next];
                if (row.Matrix != first.Matrix || row.WeightKey != first.WeightKey || row.Row != group.Count || row.Members.Count != first.Members.Count) break;
                group.Add(row);
            }

            if (group.Count < 2) continue;
            var roots = group.Select(row => RowRoot(row)).ToList();
            if (roots.Any(root => root is null)) continue;

            var name = $"weightedMatrix{_weightedMatrixCount++}";
            var columns = first.Members.Count;
            var shape = $"float{group.Count}x{columns}";
            for (var position = 0; position < group.Count; position++)
            {
                var row = group[position];
                _idiomExpressions.Remove(row.Last);
                _idiomDestinations.Remove(row.Last);
                _skippedInstructions.Add(row.Last);
                _matrixRowAliases[roots[position]!.Value] = (name, row.Row, columns);
            }

            var last = group[^1];
            _matrixDeclarations[last.Last] = () =>
                $"{shape} {name} = {SourceAt(last.Last, last.Weight, [0], ValueType.Float)} * ({shape}){MatrixAt(last.Last, last.MatrixOperand)!.Value.Matrix};";
            start += group.Count - 1;
        }
    }

    private (int Definition, int Temp)? RowRoot(WeightedRow row)
    {
        if (row.Destination.RegisterType != Gw2ShaderRegisterType.Temp || !HasVariables) return null;
        var temp = row.Destination.FirstIndex;
        var roots = row.Members.Select(member => VariableRoot((member, temp))).Distinct().ToArray();
        if (roots is not [var root]) return null;
        var web = _variableParents.Keys.Where(node => VariableRoot(node) == root).ToArray();
        return web.All(node => node.Temp == temp && row.Members.Contains(node.Definition)) ? root : null;
    }

    private void RecogniseMatrixRows()
    {
        var grouped = new HashSet<int>();
        var rows = new List<WeightedRow>();
        for (var first = 0; first < Instructions.Count; first++)
        {
            if (grouped.Contains(first) || ScaledMatrixElement(first) is not var (firstMatrix, firstRow, firstWeightPosition)) continue;
            var destination = Instructions[first].Operands[0];
            var weight = Instructions[first].Operands[firstWeightPosition];
            var members = new List<(int Index, int Component, int WeightPosition)> { (first, DestinationComponents(destination)[0], firstWeightPosition) };
            for (var next = first + 1; next < Instructions.Count && members.Count < MatrixColumns; next++)
            {
                if (ScaledMatrixElement(next) is not var (matrix, row, weightPosition) || grouped.Contains(next)) break;
                var candidate = Instructions[next].Operands[0];
                var component = DestinationComponents(candidate)[0];
                if (matrix != firstMatrix || row != firstRow || candidate.RegisterType != destination.RegisterType || candidate.FirstIndex != destination.FirstIndex) break;
                if (members.Any(member => member.Component == component) || MatrixAt(next, Instructions[next].Operands[3 - weightPosition])!.Value.Column != component) break;
                if (!SameValue(Instructions[next].Operands[weightPosition], next, 0, weight, first, 0)) break;
                members.Add((next, component, weightPosition));
            }

            if (members.Count < 2 || MatrixAt(first, Instructions[first].Operands[3 - firstWeightPosition])!.Value.Column != members[0].Component) continue;
            var last = members[^1].Index;
            if (members.SkipLast(1).Any(member => _readers.TryGetValue(member.Index, out var readers) && readers.Any(reader => reader <= last))) continue;

            var mask = members.Aggregate(0, (combined, member) => combined | (1 << member.Component));
            _idiomDestinations[last] = destination with { Mask = mask };
            var matrixOperand = Instructions[last].Operands[3 - members[^1].WeightPosition];
            var lastWeight = Instructions[last].Operands[members[^1].WeightPosition];
            var matrixRow = firstRow;
            _idiomExpressions[last] = components =>
                $"{Source(lastWeight, [components[0]], ValueType.Float)} * {MatrixAt(last, matrixOperand)!.Value.Matrix}[{matrixRow}].{string.Concat(components.Select(component => Components[component]))}";
            rows.Add(new WeightedRow(members.Select(member => member.Index).ToArray(), firstMatrix, firstRow, WeightKey(new MatrixTerm(first, Instructions[first].Operands[3 - firstWeightPosition], weight)), destination, lastWeight, matrixOperand));
            foreach (var member in members.SkipLast(1)) _skippedInstructions.Add(member.Index);
            grouped.UnionWith(members.Select(member => member.Index));
        }

        CombineWeightedMatrices(rows);
        _pendingReferences.Clear();
    }

    private (string Matrix, int Row, int WeightPosition)? ScaledMatrixElement(int index)
    {
        var instruction = Instructions[index];
        if (instruction.Name != "mul" || instruction.Saturate || _idiomExpressions.ContainsKey(index) || _skippedInstructions.Contains(index)) return null;
        var destination = instruction.Operands[0];
        if (destination.RegisterType is not (Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.Output) || destination.ComponentCount != 4 || BitCount(destination.Mask) != 1) return null;
        var component = DestinationComponents(destination)[0];
        foreach (var matrixPosition in new[] { 1, 2 })
        {
            var operand = instruction.Operands[matrixPosition];
            if (operand.Modifier != Gw2Sm5OperandModifier.None || MatrixAt(index, operand) is not var (matrix, _)) continue;
            if (instruction.Operands[3 - matrixPosition].RegisterType == Gw2ShaderRegisterType.ConstantBuffer) continue;
            return (matrix, operand.Swizzle[component], 3 - matrixPosition);
        }

        return null;
    }
}
