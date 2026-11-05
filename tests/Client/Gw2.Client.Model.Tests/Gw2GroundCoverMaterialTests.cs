using Gw2.Formats;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2GroundCoverMaterialTests
{
    private const string ReferenceExecutable = "gw2-research/research/208784/Gw2-208784.exe";
    private const ulong ImageBase = 0x140000000;
    private const ulong GroundCoverMaterialTable = 0x142592100;
    private const int GroundCoverMaterialEntrySize = 24;
    private static readonly int[] GroundCoverMaterialIndices = [0, 1, 2, 3, 8, 9];

    [TestMethod]
    public void ExecutableGroundCoverMaterialsDecodeAsAmat()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        var executablePath = FindRepoFile(ReferenceExecutable);
        if (executablePath is null) Assert.Inconclusive($"Place {ReferenceExecutable}.");
        var image = File.ReadAllBytes(executablePath);
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        foreach (var index in GroundCoverMaterialIndices)
        {
            var entry = image.AsSpan(FileOffset(image, GroundCoverMaterialTable + (ulong)(index * GroundCoverMaterialEntrySize)), GroundCoverMaterialEntrySize);
            var size = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var blob = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
            var material = reader.ReadMaterial(image.AsSpan(FileOffset(image, blob), size).ToArray());
            Assert.IsNotNull(material, index.ToString());
            Console.WriteLine($"{index}: shaders {material.Shaders.Count} techniques {material.Techniques.Count} textures {string.Join(",", material.TextureTokens.Select(token => token.ToString("X")))}");
            Assert.IsTrue(material.Shaders.Count > 0, index.ToString());
        }
    }

    private static int FileOffset(byte[] image, ulong address)
    {
        var relative = (uint)(address - ImageBase);
        var header = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(header + 6));
        var optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(header + 20));
        var sections = header + 24 + optionalHeaderSize;
        for (var section = 0; section < sectionCount; section++)
        {
            var entry = image.AsSpan(sections + section * 40);
            var virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            var rawAddress = BinaryPrimitives.ReadUInt32LittleEndian(entry[20..]);
            if (relative >= virtualAddress && relative < virtualAddress + virtualSize) return (int)(rawAddress + relative - virtualAddress);
        }

        throw new InvalidDataException($"0x{address:X} is outside the image sections.");
    }

    private static string? FindRepoFile(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
