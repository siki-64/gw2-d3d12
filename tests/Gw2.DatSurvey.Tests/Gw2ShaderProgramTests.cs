using System.Buffers.Binary;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.DatSurvey.Tests;

[TestClass]
public sealed class Gw2ShaderProgramTests
{
    private const uint PixelShaderModel50 = 0x00000050;
    private const uint CustomDataImmediateConstantBuffer = 53 | (3u << 11);
    private const uint SamplerDeclaration = 90 | (3u << 24);
    private const uint SamplerOperand = 0x00106000;
    private const uint Return = 62 | (1u << 24);

    [TestMethod]
    public void DeclarationsAfterAnImmediateConstantBufferAreFound()
    {
        uint[] body =
        [
            CustomDataImmediateConstantBuffer, 6, 0x3F800000, 0, 0, 0,
            SamplerDeclaration, SamplerOperand, 0,
            SamplerDeclaration, SamplerOperand, 1,
            Return,
        ];
        var program = Gw2ShaderProgram.TryParse(Program(body))!;

        CollectionAssert.AreEqual(new[] { 0, 1 }, program.SamplerDeclarations().ToArray());
    }

    private static byte[] Program(uint[] body)
    {
        var bytes = new byte[(body.Length + 2) * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, PixelShaderModel50);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(body.Length + 2));
        for (var index = 0; index < body.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((index + 2) * 4), body[index]);
        return bytes;
    }
}
