using System.Buffers.Binary;

namespace Gw2.Dat.Content;

public enum Gw2AudioOpcode : byte
{
    End = 0x00,
    LoadLocal = 0x01,
    StoreLocal = 0x02,
    PushNumber = 0x03,
    PushWide = 0x04,
    Pop = 0x05,
    Add = 0x06,
    Subtract = 0x07,
    Multiply = 0x08,
    Divide = 0x09,
    Less = 0x0A,
    Equal = 0x0B,
    NotEqual = 0x0C,
    And = 0x0D,
    Or = 0x0E,
    JumpIfFalse = 0x0F,
    JumpIfTrue = 0x10,
    Jump = 0x11,
    CallFunction = 0x12,
    Modulo = 0x13,
}

public enum Gw2AudioValueType : byte
{
    None = 0,
    Number = 1,
    Token = 2,
    Identifier = 3,
}

public enum Gw2AudioScriptError
{
    None = 0,
    InvalidInstruction = 1,
    InvalidArguments = 2,
    StackOverflow = 3,
    StackUnderflow = 4,
    HeapAccessViolation = 5,
    InfiniteLoop = 6,
    UnknownFunction = 7,
    Recursion = 8,
}

public readonly record struct Gw2AudioValue(Gw2AudioValueType Type, float Number, ulong Wide);

public readonly record struct Gw2AudioInstruction(int Offset, Gw2AudioOpcode Opcode, int Operand, Gw2AudioValue Literal);

public static class Gw2AudioByteCode
{
    private static readonly int[] InstructionSizes = [1, 2, 2, 6, 10, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 3, 3, 3, 3, 1];

    public static int InstructionSize(Gw2AudioOpcode opcode) => InstructionSizes[(int)opcode];

    public static IReadOnlyList<Gw2AudioInstruction>? TryDisassemble(ReadOnlySpan<byte> byteCode)
    {
        var instructions = new List<Gw2AudioInstruction>();
        var offset = 0;
        while (offset < byteCode.Length)
        {
            var raw = byteCode[offset];
            if (raw >= InstructionSizes.Length) return null;
            var opcode = (Gw2AudioOpcode)raw;
            var size = InstructionSizes[raw];
            if (offset + size > byteCode.Length) return null;
            var operands = byteCode.Slice(offset + 1, size - 1);
            var instruction = opcode switch
            {
                Gw2AudioOpcode.LoadLocal or Gw2AudioOpcode.StoreLocal => new Gw2AudioInstruction(offset, opcode, operands[0], default),
                Gw2AudioOpcode.PushNumber or Gw2AudioOpcode.PushWide => new Gw2AudioInstruction(offset, opcode, 0, ReadLiteral(operands)),
                Gw2AudioOpcode.JumpIfFalse or Gw2AudioOpcode.JumpIfTrue or Gw2AudioOpcode.Jump or Gw2AudioOpcode.CallFunction =>
                    new Gw2AudioInstruction(offset, opcode, BinaryPrimitives.ReadUInt16LittleEndian(operands), default),
                _ => new Gw2AudioInstruction(offset, opcode, 0, default),
            };
            instructions.Add(instruction);
            offset += size;
        }

        return instructions;
    }

    private static Gw2AudioValue ReadLiteral(ReadOnlySpan<byte> operands)
    {
        var type = (Gw2AudioValueType)operands[0];
        return type switch
        {
            Gw2AudioValueType.Number => new Gw2AudioValue(type, Gw2Binary.ReadSingleLittleEndian(operands[1..]), 0),
            Gw2AudioValueType.Token or Gw2AudioValueType.Identifier => new Gw2AudioValue(type, 0, BinaryPrimitives.ReadUInt64LittleEndian(operands[1..])),
            _ => new Gw2AudioValue(type, 0, 0),
        };
    }
}
