using System.Buffers.Binary;
using System.Text;

namespace Gw2.Dat.Content;

public sealed record Gw2DxbcChunk(string FourCc, int Offset, int Size, ReadOnlyMemory<byte> Data);

public sealed class Gw2Dxbc
{
    private readonly Dictionary<string, Gw2DxbcChunk> _chunks = [];

    public IReadOnlyCollection<Gw2DxbcChunk> Chunks => _chunks.Values;

    public bool TryGetChunk(string fourCc, out Gw2DxbcChunk chunk) => _chunks.TryGetValue(fourCc, out chunk!);

    public static Gw2Dxbc? TryParse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 0x20 || !span[..4].SequenceEqual("DXBC"u8)) return null;

        var count = BinaryPrimitives.ReadUInt32LittleEndian(span[0x1C..]);
        if (count == 0 || count > 64 || 0x20 + count * 4 > span.Length) return null;

        var result = new Gw2Dxbc();
        for (var c = 0u; c < count; c++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(span[(0x20 + (int)c * 4)..]);
            if (offset > span.Length - 8) return null;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(span[((int)offset + 4)..]);
            if (offset + 8 + size > span.Length) return null;
            var fourCc = Encoding.ASCII.GetString(span.Slice((int)offset, 4));
            result._chunks[fourCc] = new(fourCc, (int)offset, (int)size, data.Slice((int)offset + 8, (int)size));
        }

        return result;
    }

    public static byte[] Rebuild(IReadOnlyList<(string FourCc, ReadOnlyMemory<byte> Data)> chunks)
    {
        var count = chunks.Count;
        var cursor = 0x20 + count * 4;
        var offsets = new int[count];
        for (var i = 0; i < count; i++)
        {
            offsets[i] = cursor;
            cursor += 8 + chunks[i].Data.Length;
            cursor = (cursor + 3) & ~3;
        }

        var buffer = new byte[cursor];
        Encoding.ASCII.GetBytes("DXBC").CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x14), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x18), (uint)cursor);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x1C), (uint)count);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x20 + i * 4), (uint)offsets[i]);
            var at = offsets[i];
            Encoding.ASCII.GetBytes(chunks[i].FourCc).CopyTo(buffer, at);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at + 4), (uint)chunks[i].Data.Length);
            chunks[i].Data.Span.CopyTo(buffer.AsSpan(at + 8));
        }

        Gw2DxbcChecksum.Compute(buffer).CopyTo(buffer, 4);
        return buffer;
    }
}

public enum Gw2ShaderRegisterType
{
    Temp = 0,
    Input = 1,
    Output = 2,
    IndexableTemp = 3,
    Immediate32 = 4,
    Immediate64 = 5,
    Sampler = 6,
    Resource = 7,
    ConstantBuffer = 8,
    ImmediateConstantBuffer = 9,
    Label = 10,
    InputPrimitiveId = 11,
    OutputDepth = 12,
    Null = 13,
    Rasterizer = 14,
    OutputCoverageMask = 15,
    Stream = 16,
    Other = 255,
}

public sealed record Gw2ShaderOperand(
    Gw2ShaderRegisterType RegisterType,
    int RegisterIndex,
    int Components,
    IReadOnlyList<uint> ImmediateValues,
    IReadOnlyList<int> Swizzle)
{
    public bool IsIdentitySwizzle => Swizzle.Count == 4 && Swizzle[0] == 0 && Swizzle[1] == 1 && Swizzle[2] == 2 && Swizzle[3] == 3;
}

public enum Gw2ShaderResourceDimension
{
    Unknown = 0,
    Buffer = 1,
    Texture1D = 2,
    Texture2D = 3,
    Texture2DMultisampled = 4,
    Texture3D = 5,
    TextureCube = 6,
    Texture1DArray = 7,
    Texture2DArray = 8,
    Texture2DMultisampledArray = 9,
    TextureCubeArray = 10,
}

public sealed record Gw2ShaderResourceDeclaration(int Register, Gw2ShaderResourceDimension Dimension);

public sealed record Gw2ShaderConstantBufferDeclaration(int Register, int RegisterCount);

public sealed record Gw2ShaderInstruction(
    uint Token,
    int OpCode,
    IReadOnlyList<uint> Operands,
    IReadOnlyList<Gw2ShaderOperand> Decoded)
{
    public Gw2ShaderOperand? Operand(Gw2ShaderRegisterType type)
    {
        foreach (var operand in Decoded)
            if (operand.RegisterType == type)
                return operand;
        return null;
    }
}

public sealed class Gw2ShaderProgram
{
    private const int ResourceDeclarationOpCode = 88;
    private const int ConstantBufferDeclarationOpCode = 89;
    private const int SamplerDeclarationOpCode = 90;
    private const int CustomDataOpCode = 53;

    private Gw2ShaderProgram(uint version, IReadOnlyList<Gw2ShaderInstruction> instructions)
    {
        Version = version;
        Instructions = instructions;
    }

    public uint Version { get; }
    public IReadOnlyList<Gw2ShaderInstruction> Instructions { get; }

    public bool HasOpcode(int opCode) => Instructions.Any(instruction => instruction.OpCode == opCode);

    public IReadOnlyList<int> SampledResourceIndices()
    {
        var seen = new HashSet<int>();
        var result = new List<int>();
        foreach (var instruction in Instructions)
        {
            if (!IsTextureRead(instruction.OpCode)) continue;
            if (instruction.Operand(Gw2ShaderRegisterType.Resource) is not { } resource) continue;
            if (seen.Add(resource.RegisterIndex)) result.Add(resource.RegisterIndex);
        }

        return result;
    }

    public IReadOnlyList<Gw2ShaderResourceDeclaration> ResourceDeclarations() =>
        Instructions
            .Where(instruction => instruction.OpCode == ResourceDeclarationOpCode && instruction.Operands.Count >= 2)
            .Select(instruction => new Gw2ShaderResourceDeclaration(
                (int)instruction.Operands[1],
                (Gw2ShaderResourceDimension)((instruction.Token >> 11) & 0x1F)))
            .ToList();

    public IReadOnlyList<Gw2ShaderConstantBufferDeclaration> ConstantBufferDeclarations() =>
        Instructions
            .Where(instruction => instruction.OpCode == ConstantBufferDeclarationOpCode && instruction.Operands.Count >= 3)
            .Select(instruction => new Gw2ShaderConstantBufferDeclaration((int)instruction.Operands[1], (int)instruction.Operands[2]))
            .ToList();

    public IReadOnlyList<int> SamplerDeclarations() =>
        Instructions
            .Where(instruction => instruction.OpCode == SamplerDeclarationOpCode && instruction.Operands.Count >= 2)
            .Select(instruction => (int)instruction.Operands[1])
            .ToList();

    public static bool IsTextureRead(int opCode) => opCode switch
    {
        45 or 46 => true,
        69 or 70 or 71 or 72 or 73 or 74 => true,
        109 or 126 or 127 or 128 => true,
        163 or 165 or 167 => true,
        _ => false,
    };

    public static Gw2ShaderProgram? TryParse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 8) return null;

        var version = BinaryPrimitives.ReadUInt32LittleEndian(span);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        if (length < 2 || (long)length * 4 > span.Length) return null;

        var end = (int)length * 4;
        var instructions = new List<Gw2ShaderInstruction>();
        var cursor = 8;
        while (cursor + 4 <= end)
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
            cursor += 4;
            var opCode = (int)(token & 0x7FF);
            if (opCode == CustomDataOpCode)
            {
                if (cursor + 4 > end) break;
                var customDataDwords = BinaryPrimitives.ReadUInt32LittleEndian(span[cursor..]);
                if (customDataDwords < 2 || customDataDwords > (end - cursor + 4) / 4) break;
                cursor += ((int)customDataDwords - 1) * 4;
                continue;
            }

            var totalDwords = (int)((token >> 24) & 0x7F);
            var operandDwords = Math.Max(0, totalDwords - 1);
            if (cursor + operandDwords * 4 > end) break;

            var operands = new uint[operandDwords];
            for (var i = 0; i < operandDwords; i++)
            {
                operands[i] = BinaryPrimitives.ReadUInt32LittleEndian(span[(cursor + i * 4)..]);
            }

            cursor += operandDwords * 4;
            instructions.Add(new(token, opCode, operands, DecodeOperands(operands)));
            if (opCode == 62) break;
        }

        return instructions.Count == 0 ? null : new(version, instructions);
    }

    private static IReadOnlyList<Gw2ShaderOperand> DecodeOperands(IReadOnlyList<uint> tokens)
    {
        var operands = new List<Gw2ShaderOperand>(tokens.Count);
        var cursor = 0;
        while (cursor < tokens.Count && tokens[cursor] != 0)
        {
            if (!TryDecodeOperand(tokens, ref cursor, out var operand)) return [];
            operands.Add(operand);
        }

        return operands;
    }

    private static bool TryDecodeOperand(IReadOnlyList<uint> tokens, ref int cursor, out Gw2ShaderOperand operand)
    {
        operand = null!;
        var token0 = tokens[cursor++];
        var components = (token0 & 0x3) switch { 0 => 0, 1 => 1, 2 => 4, _ => 0 };
        var registerType = (Gw2ShaderRegisterType)((token0 >> 12) & 0xFF);
        var indexDimension = (int)((token0 >> 20) & 0x3);
        var extended = ((token0 >> 31) & 1) != 0;

        if (extended && cursor < tokens.Count) cursor++;

        var registerIndex = -1;
        for (var dimension = 0; dimension < indexDimension; dimension++)
        {
            var representation = (token0 >> (22 + dimension * 3)) & 0x7;
            switch (representation)
            {
                case 0:
                    if (cursor >= tokens.Count) return false;
                    if (dimension == 0) registerIndex = (int)tokens[cursor];
                    cursor++;
                    break;
                case 1:
                    if (cursor + 1 >= tokens.Count) return false;
                    cursor += 2;
                    break;
                default:
                    return false;
            }
        }

        var immediates = new List<uint>();
        if (registerType == Gw2ShaderRegisterType.Immediate32)
        {
            for (var i = 0; i < components; i++)
            {
                if (cursor >= tokens.Count) return false;
                immediates.Add(tokens[cursor++]);
            }
        }
        else if (registerType == Gw2ShaderRegisterType.Immediate64)
        {
            cursor += components * 2;
            if (cursor > tokens.Count) return false;
        }

        operand = new(registerType, registerIndex, components, immediates, ReadSwizzle(token0, registerType));
        return true;
    }

    private static int[] ReadSwizzle(uint token, Gw2ShaderRegisterType registerType)
    {
        var result = new[] { 0, 1, 2, 3 };
        if (registerType is Gw2ShaderRegisterType.Immediate32 or Gw2ShaderRegisterType.Immediate64)
            return result;

        var mode = token & 0x3;
        if (mode == 0)
        {
            for (var i = 0; i < 4; i++) result[i] = (int)((token >> (4 + i * 2)) & 0x3);
        }
        else if (mode == 1)
        {
            var component = (int)((token >> 2) & 0x3);
            for (var i = 0; i < 4; i++) result[i] = component;
        }

        return result;
    }
}
