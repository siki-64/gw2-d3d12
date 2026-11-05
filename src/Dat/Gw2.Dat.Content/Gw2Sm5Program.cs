using System.Buffers.Binary;

namespace Gw2.Dat.Content;

public enum Gw2Sm5OperandModifier
{
    None = 0,
    Negate = 1,
    Absolute = 2,
    AbsoluteNegate = 3,
}

public enum Gw2Sm5ComponentSelection
{
    Mask = 0,
    Swizzle = 1,
    Select1 = 2,
}

public sealed record Gw2Sm5Index(uint Immediate, Gw2Sm5Operand? Relative);

public sealed record Gw2Sm5Operand(
    Gw2ShaderRegisterType RegisterType,
    int ComponentCount,
    Gw2Sm5ComponentSelection Selection,
    int Mask,
    IReadOnlyList<int> Swizzle,
    IReadOnlyList<Gw2Sm5Index> Indices,
    IReadOnlyList<uint> Immediates,
    Gw2Sm5OperandModifier Modifier)
{
    public int FirstIndex => Indices.Count > 0 ? (int)Indices[0].Immediate : -1;
}

public sealed record Gw2Sm5SampleOffsets(int U, int V, int W);

public sealed record Gw2Sm5Instruction(
    int Offset,
    int OpCode,
    uint Token,
    bool Saturate,
    bool TestNonZero,
    Gw2Sm5SampleOffsets? SampleOffsets,
    Gw2ShaderResourceDimension? ResourceDimension,
    IReadOnlyList<int> ResourceReturnTypes,
    IReadOnlyList<Gw2Sm5Operand> Operands,
    IReadOnlyList<uint> Extra)
{
    public string Name => Gw2Sm5OpCodes.NameOf(OpCode);

    public int Controls => (int)((Token >> 11) & 0x1FFF);
}

public sealed class Gw2Sm5Program
{
    private const uint ExtendedBit = 0x80000000;
    private const int ExtendedSampleControls = 1;
    private const int ExtendedResourceDimension = 2;
    private const int ExtendedResourceReturnType = 3;
    private const int ExtendedOperandModifier = 1;
    private const int ImmediateConstantBufferClass = 3;

    private Gw2Sm5Program(Gw2Sm5ProgramType programType, int majorVersion, int minorVersion, IReadOnlyList<Gw2Sm5Instruction> instructions, IReadOnlyList<uint> immediateConstantBuffer)
    {
        ProgramType = programType;
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        Instructions = instructions;
        ImmediateConstantBuffer = immediateConstantBuffer;
    }

    public Gw2Sm5ProgramType ProgramType { get; }

    public int MajorVersion { get; }

    public int MinorVersion { get; }

    public IReadOnlyList<Gw2Sm5Instruction> Instructions { get; }

    public IReadOnlyList<uint> ImmediateConstantBuffer { get; }

    public static Gw2Sm5Program? TryParse(ReadOnlyMemory<byte> shex)
    {
        var span = shex.Span;
        if (span.Length < 8) return null;
        var versionToken = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        if (length < 2 || length * 4L > span.Length) return null;
        var tokens = new uint[length];
        for (var index = 0; index < length; index++) tokens[index] = BinaryPrimitives.ReadUInt32LittleEndian(span[(index * 4)..]);

        var instructions = new List<Gw2Sm5Instruction>();
        var immediateConstantBuffer = new List<uint>();
        var cursor = 2;
        while (cursor < length)
        {
            var start = cursor;
            var token = tokens[cursor];
            var opCode = (int)(token & 0x7FF);
            int instructionLength;
            if (opCode == Gw2Sm5OpCodes.CustomData)
            {
                if (cursor + 1 >= length) return null;
                instructionLength = (int)tokens[cursor + 1];
                if (instructionLength < 2 || start + instructionLength > length) return null;
                if ((token >> 11) == ImmediateConstantBufferClass)
                    for (var index = start + 2; index < start + instructionLength; index++) immediateConstantBuffer.Add(tokens[index]);
                instructions.Add(new(start, opCode, token, false, false, null, null, [], [], tokens[(start + 2)..(start + instructionLength)]));
                cursor = start + instructionLength;
                continue;
            }

            instructionLength = (int)((token >> 24) & 0x7F);
            if (instructionLength == 0 || start + instructionLength > length) return null;
            var end = start + instructionLength;
            cursor++;
            Gw2Sm5SampleOffsets? sampleOffsets = null;
            Gw2ShaderResourceDimension? resourceDimension = null;
            var returnTypes = new List<int>();
            var extended = (token & ExtendedBit) != 0;
            while (extended && cursor < end)
            {
                var extendedToken = tokens[cursor++];
                switch ((int)(extendedToken & 0x3F))
                {
                    case ExtendedSampleControls:
                        sampleOffsets = new(SignedNibble(extendedToken >> 9), SignedNibble(extendedToken >> 13), SignedNibble(extendedToken >> 17));
                        break;
                    case ExtendedResourceDimension:
                        resourceDimension = (Gw2ShaderResourceDimension)((extendedToken >> 6) & 0x1F);
                        break;
                    case ExtendedResourceReturnType:
                        for (var component = 0; component < 4; component++) returnTypes.Add((int)((extendedToken >> (6 + component * 4)) & 0xF));
                        break;
                }

                extended = (extendedToken & ExtendedBit) != 0;
            }

            var operands = new List<Gw2Sm5Operand>();
            var extra = new List<uint>();
            var operandCount = Gw2Sm5OpCodes.OperandCountOf(opCode, token);
            for (var operandIndex = 0; operandIndex < operandCount && cursor < end; operandIndex++)
            {
                if (!TryReadOperand(tokens, ref cursor, end, out var operand)) return null;
                operands.Add(operand);
            }

            while (cursor < end) extra.Add(tokens[cursor++]);
            instructions.Add(new(start, opCode, token, Gw2Sm5OpCodes.HasSaturate(opCode) && ((token >> 13) & 1) != 0, ((token >> 18) & 1) != 0, sampleOffsets, resourceDimension, returnTypes, operands, extra));
            cursor = end;
        }

        return new((Gw2Sm5ProgramType)((versionToken >> 16) & 0xFFFF), (int)((versionToken >> 4) & 0xF), (int)(versionToken & 0xF), instructions, immediateConstantBuffer);
    }

    private static int SignedNibble(uint value)
    {
        var nibble = (int)(value & 0xF);
        return nibble >= 8 ? nibble - 16 : nibble;
    }

    private static bool TryReadOperand(uint[] tokens, ref int cursor, int end, out Gw2Sm5Operand operand)
    {
        operand = null!;
        if (cursor >= end) return false;
        var token = tokens[cursor++];
        var componentCount = (token & 0x3) switch { 0 => 0, 1 => 1, 2 => 4, _ => 0 };
        var selection = (Gw2Sm5ComponentSelection)((token >> 2) & 0x3);
        var mask = 0;
        var swizzle = new[] { 0, 1, 2, 3 };
        if (componentCount == 4)
        {
            switch (selection)
            {
                case Gw2Sm5ComponentSelection.Mask:
                    mask = (int)((token >> 4) & 0xF);
                    break;
                case Gw2Sm5ComponentSelection.Swizzle:
                    for (var component = 0; component < 4; component++) swizzle[component] = (int)((token >> (4 + component * 2)) & 0x3);
                    break;
                case Gw2Sm5ComponentSelection.Select1:
                    var selected = (int)((token >> 4) & 0x3);
                    swizzle = [selected, selected, selected, selected];
                    break;
            }
        }

        var registerType = (Gw2ShaderRegisterType)((token >> 12) & 0xFF);
        var indexDimension = (int)((token >> 20) & 0x3);
        var modifier = Gw2Sm5OperandModifier.None;
        var extended = (token & ExtendedBit) != 0;
        while (extended)
        {
            if (cursor >= end) return false;
            var extendedToken = tokens[cursor++];
            if ((extendedToken & 0x3F) == ExtendedOperandModifier) modifier = (Gw2Sm5OperandModifier)((extendedToken >> 6) & 0xFF);
            extended = (extendedToken & ExtendedBit) != 0;
        }

        var indices = new List<Gw2Sm5Index>();
        for (var dimension = 0; dimension < indexDimension; dimension++)
        {
            var representation = (token >> (22 + dimension * 3)) & 0x7;
            uint immediate = 0;
            Gw2Sm5Operand? relative = null;
            if (representation is 0 or 3)
            {
                if (cursor >= end) return false;
                immediate = tokens[cursor++];
            }
            else if (representation is 1 or 4)
            {
                if (cursor + 1 >= end) return false;
                immediate = tokens[cursor];
                cursor += 2;
            }

            if (representation is 2 or 3 or 4)
            {
                if (!TryReadOperand(tokens, ref cursor, end, out var relativeOperand)) return false;
                relative = relativeOperand;
            }

            indices.Add(new(immediate, relative));
        }

        var immediates = new List<uint>();
        if (registerType is Gw2ShaderRegisterType.Immediate32 or Gw2ShaderRegisterType.Immediate64)
        {
            var count = componentCount * (registerType == Gw2ShaderRegisterType.Immediate64 ? 2 : 1);
            for (var index = 0; index < count; index++)
            {
                if (cursor >= end) return false;
                immediates.Add(tokens[cursor++]);
            }
        }

        operand = new(registerType, componentCount, selection, mask, swizzle, indices, immediates, modifier);
        return true;
    }
}

public enum Gw2Sm5ProgramType
{
    Pixel = 0,
    Vertex = 1,
    Geometry = 2,
    Hull = 3,
    Domain = 4,
    Compute = 5,
}
