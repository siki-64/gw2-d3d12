using System.Globalization;
using System.Text.RegularExpressions;
using Gw2.Dat.Content;

namespace Gw2.DatSurvey;

public sealed record ShaderInstructionMatch(int OriginalInstructions, int RecompiledInstructions, int CommonInstructions)
{
    public bool Identical => CommonInstructions == OriginalInstructions && OriginalInstructions == RecompiledInstructions;

    public double Similarity => Math.Max(OriginalInstructions, RecompiledInstructions) == 0 ? 1 : (double)CommonInstructions / Math.Max(OriginalInstructions, RecompiledInstructions);
}

public sealed record ShaderBytecodeMatch(ShaderInstructionMatch Strict, ShaderInstructionMatch Canonical, IReadOnlyList<string> Original, IReadOnlyList<string> Recompiled);

public static partial class ShaderBytecodeComparison
{
    private static readonly HashSet<string> CommutativeOpCodeNames = ["add", "mul", "min", "max", "dp2", "dp3", "dp4", "eq", "ne", "and", "or", "xor", "iadd", "ieq", "ine", "imax", "imin", "umax", "umin"];
    private static readonly HashSet<string> CommutativeMultiplyAddNames = ["mad", "imad", "umad"];

    public static ShaderBytecodeMatch Compare(ReadOnlyMemory<byte> originalShex, ReadOnlyMemory<byte> recompiledShex)
    {
        var original = NormalizedInstructions(originalShex, sortCommutativeOperands: false);
        var recompiled = NormalizedInstructions(recompiledShex, sortCommutativeOperands: false);
        var originalCanonical = NormalizedInstructions(originalShex, sortCommutativeOperands: true);
        var recompiledCanonical = NormalizedInstructions(recompiledShex, sortCommutativeOperands: true);
        return new(
            new(original.Count, recompiled.Count, LongestCommonSubsequence(original, recompiled)),
            new(originalCanonical.Count, recompiledCanonical.Count, LongestCommonSubsequence(originalCanonical, recompiledCanonical)),
            original,
            recompiled);
    }

    public static ReadOnlyMemory<byte> ShexOf(ReadOnlyMemory<byte> container)
    {
        var dxbc = Gw2Dxbc.TryParse(container) ?? throw new InvalidDataException("no DXBC container");
        if (dxbc.TryGetChunk("SHEX", out var chunk) || dxbc.TryGetChunk("SHDR", out chunk)) return chunk.Data;
        throw new InvalidDataException("no SHEX chunk");
    }

    public static IReadOnlyList<string> NormalizedInstructions(ReadOnlyMemory<byte> shex, bool sortCommutativeOperands)
    {
        var program = Gw2Sm5Program.TryParse(shex) ?? throw new InvalidDataException("SHEX does not decode");
        var temps = new Dictionary<string, string>();
        var lines = new List<string>();
        foreach (var instruction in program.Instructions)
        {
            if (Gw2Sm5OpCodes.IsDeclaration(instruction.OpCode) || instruction.OpCode == Gw2Sm5OpCodes.CustomData) continue;
            var operands = Gw2Sm5Listing.FormatOperands(instruction).Select(operand => RenameTemps(operand, temps)).ToArray();
            if (sortCommutativeOperands && operands.Length >= 3)
            {
                if (CommutativeOpCodeNames.Contains(instruction.Name) || CommutativeMultiplyAddNames.Contains(instruction.Name))
                    SortPair(operands, 1, 2);
            }

            var name = Gw2Sm5Listing.FormatInstructionName(instruction);
            lines.Add(operands.Length == 0 ? name : name + " " + string.Join(", ", operands));
        }

        return lines;
    }

    private static string RenameTemps(string operand, Dictionary<string, string> temps) =>
        TempRegister().Replace(operand, match =>
        {
            if (!temps.TryGetValue(match.Value, out var renamed))
            {
                renamed = "r" + temps.Count.ToString(CultureInfo.InvariantCulture);
                temps[match.Value] = renamed;
            }

            return renamed;
        });

    private static void SortPair(string[] operands, int first, int second)
    {
        if (string.CompareOrdinal(operands[first], operands[second]) > 0) (operands[first], operands[second]) = (operands[second], operands[first]);
    }

    private static int LongestCommonSubsequence(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var previous = new int[right.Count + 1];
        var current = new int[right.Count + 1];
        for (var row = 1; row <= left.Count; row++)
        {
            for (var column = 1; column <= right.Count; column++)
                current[column] = left[row - 1] == right[column - 1] ? previous[column - 1] + 1 : Math.Max(previous[column], current[column - 1]);
            (previous, current) = (current, previous);
        }

        return previous[right.Count];
    }

    [GeneratedRegex(@"\br\d+\b")]
    private static partial Regex TempRegister();
}
