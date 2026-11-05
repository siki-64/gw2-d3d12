using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2SceneTests
{
    private const uint AmbientLightSceneFileId = 43670;
    private const uint FileNameReferenceSceneFileId = 51847;
    private const uint ScriptSceneFileId = 105387;
    private const uint TextSceneFileId = 1945974;
    private const uint SpeciesSceneFileId = 45782;
    private const uint TrackGroupPropertySceneFileId = 47718;
    private const uint TriggerFlagSceneFileId = 643007;
    private const uint TriggerValueSceneFileId = 2138169;

    [TestMethod]
    public void ReadsSceneResourcesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        Gw2Scene Scene(uint fileId) => Gw2Scene.TryParse(reader.ReadDocument(fileId))!;

        var light = Scene(AmbientLightSceneFileId).AmbientLights[0];
        Assert.AreEqual(26881480066ul, light.Name);
        Assert.AreEqual(89.91411f, light.AmbientGroundColor.Intensity, 1e-4f);
        CollectionAssert.AreEqual(new byte[] { 0xDA, 0xFC, 0xFF }, light.AmbientGroundColor.Color);

        var references = Scene(FileNameReferenceSceneFileId).FileNameReferences;
        Assert.AreEqual(10, references.Count);
        Assert.AreEqual(14133185006ul, references[0].Name);
        Assert.IsTrue(references[0].FileName.TryGetFileId(out var fileId));
        Assert.AreEqual(45790u, fileId);

        var scripts = Scene(ScriptSceneFileId).Scripts;
        Assert.AreEqual(30, scripts.Count);
        Assert.AreEqual(5610827949427ul, scripts[0].Name);
        Assert.AreEqual(54, scripts[0].ByteCode.Length);

        var texts = Scene(TextSceneFileId).TextResources;
        Assert.AreEqual(51, texts.Count);
        Assert.AreEqual(1u, texts[0].Index);
        Assert.AreEqual(385079u, texts[0].VoiceId);
        StringAssert.StartsWith(texts[0].Texts[0], "Is this");

        var species = Scene(SpeciesSceneFileId).SpeciesResources;
        Assert.AreEqual(10, species.Count);
        Assert.AreEqual(16, species[0].SpeciesId.Length);
        Assert.AreEqual(5911386609249ul, species[0].ModelId);
        Assert.AreEqual(28791115278ul, species[0].ModelVariant);
    }

    [TestMethod]
    public void ReadsTrackGroupPropertiesAndTriggerKeyExtrasWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        Gw2SceneTriggerKey[] TriggerKeys(uint fileId) => Gw2Scene.TryParse(reader.ReadDocument(fileId))!.Sequences
            .SelectMany(sequence => sequence.TrackGroups).SelectMany(group => group.Tracks).SelectMany(track => track.TriggerKeys).ToArray();

        var group = Gw2Scene.TryParse(reader.ReadDocument(TrackGroupPropertySceneFileId))!.Sequences
            .SelectMany(sequence => sequence.TrackGroups).First(trackGroup => trackGroup.Properties.Count > 0);
        Assert.AreEqual((byte)14, group.Type);
        Assert.AreEqual(new Gw2SceneProperty(3460781, default, 68), group.Properties[0]);

        Assert.AreEqual(79, TriggerKeys(TriggerFlagSceneFileId).Count(key => key.Flags1 != 0 || key.Flags2 != 0 || key.Flags3 != 0 || key.Flags4 != 0));
        Assert.AreEqual(137, TriggerKeys(TriggerValueSceneFileId).Count(key => key.Value3 != 0 || key.Value4 != 0));
    }
    [TestMethod]
    public void SceneScriptsCallOnlyCinemaFunctionsWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        const int CinemaFunctionCount = 33;
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var scripts = Gw2Scene.TryParse(reader.ReadDocument(ScriptSceneFileId))!.Scripts;
        var programs = scripts.Select(script => Gw2AudioByteCode.TryDisassemble(script.ByteCode)).ToArray();

        Assert.IsTrue(programs.All(program => program is not null));
        var calls = programs.SelectMany(program => program!).Where(instruction => instruction.Opcode == Gw2AudioOpcode.CallFunction).ToArray();
        Assert.IsTrue(calls.Length > 0);
        Assert.IsTrue(calls.All(call => call.Operand < CinemaFunctionCount));
    }
}
