using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2AudioScriptTests
{
    private const uint AudioSettingsScriptFileId = 184774;
    private const uint HandlerAndMetaSoundScriptFileId = 9235;
    private const uint SoundPoolScriptFileId = 3376946;
    private const uint TriggerKeyScriptFileId = 13359;

    private static Gw2ContentReader OpenReader()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        return Gw2ContentReader.Open(dat, verifyChecksums: false);
    }

    [TestMethod]
    public void ReadsAudioSettings()
    {
        using var reader = OpenReader();
        var settings = Gw2AudioScript.TryParse(reader.ReadDocument(AudioSettingsScriptFileId))!.Settings!;

        Assert.AreEqual(21899548836ul, settings.DefaultSnapshot);
        Assert.AreEqual(39.3701f, settings.DistanceScale);
        Assert.AreEqual(120f, settings.MemoryPool);
        Assert.AreEqual((5, 30, 22, 1816, 11, 74, 19),
            (settings.Busses.Count, settings.Categories.Count, settings.Materials.Count, settings.MusicPlaylists.Count,
                settings.Reverbs.Count, settings.Snapshots.Count, settings.MusicExternals.Count));
        Assert.AreEqual((184691u, 184692u, 105242u), (settings.BankIndexFileId, settings.BankScriptFileId, settings.MusicScriptFileId));
        Assert.AreEqual(new Gw2AudioMusicExternal(21950138785, "Ambient"), settings.MusicExternals[0]);

        var buss = settings.Busses[0];
        Assert.AreEqual((4606455ul, 609897517ul, 2u), (buss.Name, buss.Output, buss.Flags));
        Assert.AreEqual((-10f, 100f, 150f, 3f), (buss.CompressorThreshold, buss.CompressorAttack, buss.CompressorRelease, buss.CompressorGainMakeup));

        Assert.AreEqual(new Gw2AudioMaterial(4563, 0, 0.6f, 0.7f, 0.8f, 0.7f), settings.Materials[0]);

        var reverb = settings.Reverbs[0];
        Assert.AreEqual((177636ul, 4f, -500f, 20000f, 250f), (reverb.Name, reverb.DecayTime, reverb.Reflections, reverb.ReferenceHF, reverb.ReferenceLF));

        var category = settings.Categories[0];
        Assert.AreEqual((309ul, 10u, (byte)4), (category.Name, category.MaxAudible, category.Priority));
        Assert.AreEqual(1f, category.Attenuation!.Spread3D.Value);

        var playlist = settings.MusicPlaylists[0];
        Assert.AreEqual(77540u, playlist.Files[0].FileId);
        Assert.AreEqual(new Gw2AudioRange(180, 60, 0), playlist.IntervalSilence);
        Assert.AreEqual(0.7079f, playlist.Volume.Value);

        var snapshot = settings.Snapshots[0];
        Assert.AreEqual(642530ul, snapshot.Name);
        Assert.AreEqual(-10000f, snapshot.Categories[0].ReverbRoom);
    }

    [TestMethod]
    public void ReadsHandlersAndMetaSounds()
    {
        using var reader = OpenReader();
        var script = Gw2AudioScript.TryParse(reader.ReadDocument(HandlerAndMetaSoundScriptFileId))!;

        Assert.AreEqual(140, script.Handlers.Count);
        Assert.AreEqual(828, script.MetaSounds.Count);
        Assert.AreEqual(21275087ul, script.Handlers[0].Name);
        Assert.AreEqual(317, script.Handlers[0].ByteCode.Length);

        var enveloped = script.MetaSounds.First(sound => sound.Name == 5324);
        Assert.AreEqual(959373u, enveloped.Files[0].FileId);
        Assert.AreEqual(0.7079f, enveloped.Volume.Value);
        var lowPass = enveloped.Attenuation!.LowPass;
        Assert.AreEqual((byte)2, lowPass.Type);
        Assert.AreEqual(112f, lowPass.Envelope!.InputScale);
        CollectionAssert.AreEqual(
            new[] { new Gw2AudioEnvelopePoint(0, 0), new Gw2AudioEnvelopePoint(0.25f, 0), new Gw2AudioEnvelopePoint(1, 1) },
            lowPass.Envelope.Points.ToArray());

        var randomized = script.MetaSounds.First(sound => sound.Name == 273785900);
        Assert.AreEqual(new Gw2AudioRange(1.1f, 0.9f, 0), randomized.Pitch.Random!.Value);
        Assert.AreEqual(new Gw2AudioRange(1, 0.5f, 0), randomized.Volume.Random!.Value);
        Assert.AreEqual(new Gw2AudioRange(0.6f, 0.2f, 1), randomized.PlayLength);
    }

    [TestMethod]
    public void ReadsSoundPoolSettings()
    {
        using var reader = OpenReader();
        var script = Gw2AudioScript.TryParse(reader.ReadDocument(SoundPoolScriptFileId))!;

        Assert.AreEqual((32u, 4u, 1f, (byte)2, 15f), (script.Flags, script.SoundPoolCount, script.SoundPoolDelay, script.Interpolation, script.RadialLimit));
        Assert.AreEqual(new Gw2AudioRange(1.75f, 0.7f, 0), script.ScaleBucketRange);
    }

    [TestMethod]
    public void ReadsTriggerKeysAndProperties()
    {
        using var reader = OpenReader();
        var script = Gw2AudioScript.TryParse(reader.ReadDocument(TriggerKeyScriptFileId))!;

        Assert.AreEqual(79, script.TriggerKeys.Count);
        Assert.AreEqual(538775797ul, script.TriggerKeys[0].Name);
        Assert.AreEqual(new Gw2AudioTriggerMarker(15318809616464968, 0, 0.97f, 0), script.TriggerKeys[0].Markers[1]);
        Assert.AreEqual(new Gw2AudioProperty(247654707, 0, 1.25f), script.Properties[0]);
        Assert.AreEqual(new Gw2AudioProperty(6074836395394, 180719266, 0), script.Properties[2]);
    }
}
