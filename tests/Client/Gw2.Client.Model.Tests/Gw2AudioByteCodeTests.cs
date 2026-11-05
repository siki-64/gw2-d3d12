using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AudioByteCodeTests
{
    private static readonly uint[] ScriptFileIdsWithByteCode = [184774, 9235, 13359, 13211, 13360, 13524];

    [TestMethod]
    public void DisassemblesLiteralsJumpsAndCalls()
    {
        byte[] byteCode =
        [
            0x03, 0x01, 0x00, 0x00, 0x80, 0x3F,
            0x02, 0x00,
            0x04, 0x02, 0xBA, 0xC8, 0x07, 0xDF, 0x2C, 0x00, 0x00, 0x00,
            0x0F, 0x16, 0x00,
            0x12, 0x05, 0x00,
            0x00,
        ];

        var instructions = Gw2AudioByteCode.TryDisassemble(byteCode)!;

        Assert.AreEqual(new Gw2AudioValue(Gw2AudioValueType.Number, 1f, 0), instructions[0].Literal);
        Assert.AreEqual((Gw2AudioOpcode.StoreLocal, 0), (instructions[1].Opcode, instructions[1].Operand));
        Assert.AreEqual(new Gw2AudioValue(Gw2AudioValueType.Token, 0, 0x2CDF07C8BA), instructions[2].Literal);
        Assert.AreEqual((Gw2AudioOpcode.JumpIfFalse, 22), (instructions[3].Opcode, instructions[3].Operand));
        Assert.AreEqual((Gw2AudioOpcode.CallFunction, 5), (instructions[4].Opcode, instructions[4].Operand));
        Assert.AreEqual(Gw2AudioOpcode.End, instructions[5].Opcode);
        Assert.IsNull(Gw2AudioByteCode.TryDisassemble([0x14]));
        Assert.IsNull(Gw2AudioByteCode.TryDisassemble([0x11, 0x00]));
    }

    [TestMethod]
    public void DisassemblesEveryHandlerAndMusicConditionWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var scriptFileIds = Environment.GetEnvironmentVariable("GW2_SCRIPT_FILE_IDS") is { } idList && File.Exists(idList)
            ? File.ReadAllLines(idList).Where(line => line.Length > 0).Select(uint.Parse).ToArray()
            : ScriptFileIdsWithByteCode;
        var byteCodes = new List<byte[]>();
        foreach (var fileId in scriptFileIds)
        {
            var script = Gw2AudioScript.TryParse(reader.ReadDocument(fileId))!;
            byteCodes.AddRange(script.Handlers.Select(handler => handler.ByteCode));
            if (script.Settings is not null) byteCodes.AddRange(script.Settings.MusicConditions.Select(condition => condition.ByteCode));
        }

        var programs = byteCodes.Where(code => code.Length > 0).Select(code => Gw2AudioByteCode.TryDisassemble(code)).ToArray();
        Assert.IsTrue(programs.Length > 0);
        Assert.IsTrue(programs.All(program => program is not null));
        Assert.IsTrue(programs.All(program => program![^1].Opcode == Gw2AudioOpcode.End));
        Assert.IsTrue(programs.All(program => program!.Where(IsJump).All(jump => program!.Any(target => target.Offset == jump.Operand))));
        Assert.IsTrue(programs.SelectMany(program => program!).Where(instruction => instruction.Opcode == Gw2AudioOpcode.PushNumber).All(instruction => instruction.Literal.Type == Gw2AudioValueType.Number));
        Assert.IsTrue(programs.SelectMany(program => program!).Where(instruction => instruction.Opcode == Gw2AudioOpcode.PushWide).All(instruction => instruction.Literal.Type is Gw2AudioValueType.Token or Gw2AudioValueType.Identifier));
    }

    private static bool IsJump(Gw2AudioInstruction instruction) =>
        instruction.Opcode is Gw2AudioOpcode.JumpIfFalse or Gw2AudioOpcode.JumpIfTrue or Gw2AudioOpcode.Jump;
}
