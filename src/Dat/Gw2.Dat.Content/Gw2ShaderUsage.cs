namespace Gw2.Dat.Content;

public sealed record Gw2ShaderOutputUsage(int OutputRegister, IReadOnlyList<int> ResourceIndices);

public enum Gw2ShaderResourceRole
{
    Unknown,
    BaseColor,
    Direction,
    Normal,
}

public sealed record Gw2ShaderResourceUse(
    int Resource,
    bool ReachesOutput,
    bool ReachesDotProduct,
    bool RemapsToSigned,
    Gw2ShaderResourceRole Role);

public static class Gw2ShaderUsage
{
    public static IReadOnlyList<Gw2ShaderOutputUsage> ResourcesByOutput(Gw2ShaderProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var definitions = new Dictionary<(Gw2ShaderRegisterType Type, int Index), List<int>>();
        for (var i = 0; i < program.Instructions.Count; i++)
        {
            var instruction = program.Instructions[i];
            if (!IsValueOp(instruction.OpCode) || Destination(instruction) is not { } destination) continue;
            var key = (destination.RegisterType, Index(destination));
            if (!definitions.TryGetValue(key, out var list)) definitions[key] = list = [];
            list.Add(i);
        }

        var outputs = new List<Gw2ShaderOutputUsage>();
        for (var i = 0; i < program.Instructions.Count; i++)
        {
            if (Destination(program.Instructions[i]) is not { RegisterType: Gw2ShaderRegisterType.Output } output)
                continue;

            var resources = new HashSet<int>();
            var visited = new HashSet<int>();
            var pending = new Stack<(Gw2ShaderRegisterType Type, int Index, int Before)>();
            pending.Push((output.RegisterType, Index(output), i + 1));
            while (pending.TryPop(out var use))
            {
                if (!definitions.TryGetValue((use.Type, use.Index), out var candidates)) continue;
                var definition = NearestBefore(candidates, use.Before);
                if (definition < 0 || !visited.Add(definition)) continue;

                var instruction = program.Instructions[definition];
                if (Gw2ShaderProgram.IsTextureRead(instruction.OpCode) &&
                    instruction.Operand(Gw2ShaderRegisterType.Resource) is { } resource)
                {
                    resources.Add(resource.RegisterIndex);
                }

                foreach (var source in Sources(instruction))
                    pending.Push((source.RegisterType, Index(source), definition));
            }

            outputs.Add(new(Index(output), resources.OrderBy(value => value).ToList()));
        }

        return outputs;
    }

    private static int NearestBefore(List<int> candidates, int before)
    {
        var found = -1;
        foreach (var candidate in candidates)
        {
            if (candidate >= before) break;
            found = candidate;
        }

        return found;
    }

    public static IReadOnlyList<Gw2ShaderResourceUse> ClassifyResources(Gw2ShaderProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        var uses = new List<Gw2ShaderResourceUse>();
        for (var i = 0; i < program.Instructions.Count; i++)
        {
            var instruction = program.Instructions[i];
            if (!Gw2ShaderProgram.IsTextureRead(instruction.OpCode)) continue;
            if (instruction.Operand(Gw2ShaderRegisterType.Resource) is not { } resource) continue;
            if (Destination(instruction) is not { } destination) continue;

            var (reachesOutput, reachesDotProduct, remapsToSigned) = Forward(program, i, destination);
            var role = reachesDotProduct
                ? remapsToSigned ? Gw2ShaderResourceRole.Normal : Gw2ShaderResourceRole.Direction
                : reachesOutput
                    ? Gw2ShaderResourceRole.BaseColor
                    : Gw2ShaderResourceRole.Unknown;
            uses.Add(new(resource.RegisterIndex, reachesOutput, reachesDotProduct, remapsToSigned, role));
        }

        return uses;
    }

    private static (bool ReachesOutput, bool ReachesDotProduct, bool RemapsToSigned) Forward(
        Gw2ShaderProgram program,
        int start,
        Gw2ShaderOperand destination)
    {
        var live = new Dictionary<(Gw2ShaderRegisterType, int), int>
        {
            [(destination.RegisterType, Index(destination))] = start,
        };
        var reachesOutput = false;
        var reachesDotProduct = false;
        var remapsToSigned = false;
        for (var j = start + 1; j < program.Instructions.Count; j++)
        {
            var instruction = program.Instructions[j];
            var uses = UsesLive(instruction, live, j);

            if (uses)
            {
                if (instruction.OpCode is 15 or 16 or 17)
                    reachesDotProduct = true;
                else if (RemapsSigned(instruction))
                    remapsToSigned = true;
            }

            var defined = Destination(instruction);
            if (defined is null) continue;
            var key = (defined.RegisterType, Index(defined));
            if (uses && defined.RegisterType == Gw2ShaderRegisterType.Output)
                reachesOutput = true;
            else if (uses && defined.RegisterType is Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.IndexableTemp)
                live[key] = j;
            else
                live.Remove(key);
        }

        return (reachesOutput, reachesDotProduct, remapsToSigned);
    }

    private static bool RemapsSigned(Gw2ShaderInstruction instruction)
    {
        if (instruction.OpCode is not (0 or 50)) return false;
        var two = false;
        var minusOne = false;
        foreach (var operand in instruction.Decoded)
        {
            if (operand.RegisterType != Gw2ShaderRegisterType.Immediate32) continue;
            foreach (var value in operand.ImmediateValues)
            {
                if (value == 0x40000000u) two = true;
                else if (value == 0xBF800000u) minusOne = true;
            }
        }

        return two && minusOne;
    }

    private static bool UsesLive(
        Gw2ShaderInstruction instruction,
        Dictionary<(Gw2ShaderRegisterType, int), int> live,
        int before)
    {
        var first = true;
        foreach (var operand in instruction.Decoded)
        {
            if (first) { first = false; continue; }
            if (operand.RegisterType is Gw2ShaderRegisterType.Resource or Gw2ShaderRegisterType.Sampler or Gw2ShaderRegisterType.ImmediateConstantBuffer)
                break;
            if (operand.RegisterType is Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.IndexableTemp
                && live.TryGetValue((operand.RegisterType, Index(operand)), out var definition) && definition < before)
                return true;
        }

        return false;
    }

    private static bool IsValueOp(int opCode)
    {
        if (Gw2ShaderProgram.IsTextureRead(opCode)) return true;
        if (opCode is >= 88 and <= 106) return false;
        return opCode switch
        {
            2 or 3 or 4 or 5 or 6 or 7 or 8 or 10 or 13 or 18 or 20 or 21 or 22 or 31 or 44 or 62
                or 63 or 64 or 76 or 77 or 78 or 207 or 208 => false,
            _ => true,
        };
    }

    private static Gw2ShaderOperand? Destination(Gw2ShaderInstruction instruction)
    {
        foreach (var operand in instruction.Decoded)
            if (operand.RegisterType is Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.Output
                or Gw2ShaderRegisterType.IndexableTemp)
                return operand;
        return null;
    }

    private static IEnumerable<Gw2ShaderOperand> Sources(Gw2ShaderInstruction instruction)
    {
        var first = true;
        foreach (var operand in instruction.Decoded)
        {
            if (operand.RegisterType is Gw2ShaderRegisterType.Resource or Gw2ShaderRegisterType.Sampler or Gw2ShaderRegisterType.ImmediateConstantBuffer)
                yield break;
            if (first) { first = false; continue; }
            if (operand.RegisterType is Gw2ShaderRegisterType.Temp or Gw2ShaderRegisterType.Input
                or Gw2ShaderRegisterType.ConstantBuffer or Gw2ShaderRegisterType.Immediate32)
                yield return operand;
        }
    }

    private static int Index(Gw2ShaderOperand operand) => operand.RegisterIndex < 0 ? 0 : operand.RegisterIndex;
}
