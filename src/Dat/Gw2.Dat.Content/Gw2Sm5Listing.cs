using System.Globalization;
using System.Text;

namespace Gw2.Dat.Content;

public static class Gw2Sm5Listing
{
    private const string Components = "xyzw";
    private static readonly string[] ReturnTypeNames = ["", "unorm", "snorm", "sint", "uint", "float", "mixed", "double", "continued", "unused"];
    private static readonly HashSet<int> TestedOpCodes = [3, 5, 8, 13, 31, 63];
    private static readonly HashSet<int> BlockOpenOpCodes = [31, 48, 76, 18, 6, 10];
    private static readonly HashSet<int> BlockCloseOpCodes = [21, 22, 23, 18, 6, 10];
    private static readonly HashSet<int> IntegerImmediateOpCodes = [1, 30, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 45, 46, 59, 60, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 134, 135, 136, 137, 138, 139, 140, 141];

    public static string Format(Gw2Sm5Program program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var text = new StringBuilder();
        var depth = 0;
        foreach (var instruction in program.Instructions)
        {
            if (Gw2Sm5OpCodes.IsDeclaration(instruction.OpCode) || instruction.OpCode == Gw2Sm5OpCodes.CustomData) continue;
            if (BlockCloseOpCodes.Contains(instruction.OpCode)) depth = Math.Max(0, depth - 1);
            text.Append(' ', depth * 2).Append(FormatInstruction(instruction)).Append('\n');
            if (BlockOpenOpCodes.Contains(instruction.OpCode)) depth++;
        }

        return text.ToString();
    }

    public static string FormatInstruction(Gw2Sm5Instruction instruction)
    {
        var name = FormatInstructionName(instruction);
        var operands = FormatOperands(instruction);
        return operands.Count == 0 ? name : name + " " + string.Join(", ", operands);
    }

    public static IReadOnlyList<string> FormatOperands(Gw2Sm5Instruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var integerImmediates = IntegerImmediateOpCodes.Contains(instruction.OpCode);
        return instruction.Operands.Select(operand => FormatOperand(operand, integerImmediates)).ToArray();
    }

    public static string FormatInstructionName(Gw2Sm5Instruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var name = new StringBuilder(instruction.Name);
        if (TestedOpCodes.Contains(instruction.OpCode)) name.Append(instruction.TestNonZero ? "_nz" : "_z");
        if (instruction.SampleOffsets is { } offsets && (offsets.U != 0 || offsets.V != 0 || offsets.W != 0))
            name.Append(CultureInfo.InvariantCulture, $"_aoffimmi({offsets.U},{offsets.V},{offsets.W})");
        if (instruction.ResourceDimension is { } dimension)
            name.Append("_indexable(").Append(DimensionName(dimension)).Append(')');
        if (instruction.ResourceReturnTypes.Count == 4)
            name.Append('(').Append(string.Join(",", instruction.ResourceReturnTypes.Select(type => ReturnTypeNames[Math.Min(type, ReturnTypeNames.Length - 1)]))).Append(')');
        if (instruction.Saturate) name.Append("_sat");
        return name.ToString();
    }

    public static string FormatOperand(Gw2Sm5Operand operand, bool integerImmediates)
    {
        ArgumentNullException.ThrowIfNull(operand);
        var body = operand.RegisterType switch
        {
            Gw2ShaderRegisterType.Immediate32 => "l(" + string.Join(", ", operand.Immediates.Select(value => FormatImmediate(value, integerImmediates))) + ")",
            _ => RegisterName(operand) + ComponentSuffix(operand),
        };
        return operand.Modifier switch
        {
            Gw2Sm5OperandModifier.Negate => "-" + body,
            Gw2Sm5OperandModifier.Absolute => "|" + body + "|",
            Gw2Sm5OperandModifier.AbsoluteNegate => "-|" + body + "|",
            _ => body,
        };
    }

    public static string RegisterName(Gw2Sm5Operand operand)
    {
        ArgumentNullException.ThrowIfNull(operand);
        var prefix = operand.RegisterType switch
        {
            Gw2ShaderRegisterType.Temp => "r",
            Gw2ShaderRegisterType.Input => "v",
            Gw2ShaderRegisterType.Output => "o",
            Gw2ShaderRegisterType.IndexableTemp => "x",
            Gw2ShaderRegisterType.Sampler => "s",
            Gw2ShaderRegisterType.Resource => "t",
            Gw2ShaderRegisterType.ConstantBuffer => "cb",
            Gw2ShaderRegisterType.ImmediateConstantBuffer => "icb",
            Gw2ShaderRegisterType.Label => "l",
            Gw2ShaderRegisterType.InputPrimitiveId => "vPrim",
            Gw2ShaderRegisterType.OutputDepth => "oDepth",
            Gw2ShaderRegisterType.Null => "null",
            Gw2ShaderRegisterType.OutputCoverageMask => "oMask",
            _ => $"reg{(int)operand.RegisterType}_",
        };
        if (operand.Indices.Count == 0) return prefix;
        if (operand.RegisterType == Gw2ShaderRegisterType.ImmediateConstantBuffer) return prefix + "[" + FormatIndex(operand.Indices[0]) + "]";
        var name = new StringBuilder(prefix).Append(FormatIndex(operand.Indices[0]));
        for (var index = 1; index < operand.Indices.Count; index++) name.Append('[').Append(FormatIndex(operand.Indices[index])).Append(']');
        return name.ToString();
    }

    private static string FormatIndex(Gw2Sm5Index index)
    {
        if (index.Relative is null) return index.Immediate.ToString(CultureInfo.InvariantCulture);
        var relative = FormatOperand(index.Relative, true);
        return index.Immediate == 0 ? relative : $"{relative} + {index.Immediate}";
    }

    private static string ComponentSuffix(Gw2Sm5Operand operand)
    {
        if (operand.ComponentCount != 4) return string.Empty;
        return operand.Selection switch
        {
            Gw2Sm5ComponentSelection.Mask => operand.Mask is 0 ? string.Empty : "." + string.Concat(Enumerable.Range(0, 4).Where(component => (operand.Mask & (1 << component)) != 0).Select(component => Components[component])),
            Gw2Sm5ComponentSelection.Select1 => "." + Components[operand.Swizzle[0]],
            _ => "." + string.Concat(operand.Swizzle.Select(component => Components[component])),
        };
    }

    private static string FormatImmediate(uint value, bool integer) =>
        integer ? unchecked((int)value).ToString(CultureInfo.InvariantCulture) : BitConverter.UInt32BitsToSingle(value).ToString("F6", CultureInfo.InvariantCulture);

    private static string DimensionName(Gw2ShaderResourceDimension dimension) => dimension switch
    {
        Gw2ShaderResourceDimension.Buffer => "buffer",
        Gw2ShaderResourceDimension.Texture1D => "texture1d",
        Gw2ShaderResourceDimension.Texture2D => "texture2d",
        Gw2ShaderResourceDimension.Texture2DMultisampled => "texture2dms",
        Gw2ShaderResourceDimension.Texture3D => "texture3d",
        Gw2ShaderResourceDimension.TextureCube => "texturecube",
        Gw2ShaderResourceDimension.Texture1DArray => "texture1darray",
        Gw2ShaderResourceDimension.Texture2DArray => "texture2darray",
        Gw2ShaderResourceDimension.Texture2DMultisampledArray => "texture2dmsarray",
        Gw2ShaderResourceDimension.TextureCubeArray => "texturecubearray",
        _ => "unknown",
    };
}
