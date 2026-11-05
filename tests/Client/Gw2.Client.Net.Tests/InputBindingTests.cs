using Gw2.Dat.Archive;
using System;
using System.IO;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Formats;
using Gw2.Protocol.Capture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class InputBindingTests
{
    private const uint WeaponSkillOne = 21;
    private const uint MoveForward = 0;
    private const uint TargetPreviousEnemy = 46;
    private const uint KeyOne = 49;
    private const uint KeyF10 = 41;
    private const uint KeyW = 87;
    private const uint KeyUpArrow = 31;
    private const uint KeyTab = 22;
    private const int AccountPreferencesFromServer = 0xA;
    private const int AccountPreferencesToServer = 0xC;
    private const int PreferencePackField = 2;

    [TestMethod]
    public void ReadsTheDefaultBindingsOfEveryControl()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var layout = InputBindingLayout.Current;

        Assert.AreEqual(0x21u, layout.ActionContentType);
        Assert.AreEqual((44, 72, 80), (layout.ControlIdOffset, layout.PrimaryBindOffset, layout.SecondaryBindOffset));
        Assert.AreEqual(222, layout.ControlIdByPackedControl.Count);
        Assert.AreEqual(WeaponSkillOne, layout.ControlIdByPackedControl[0x12]);
        Assert.AreEqual(InputDevice.Keyboard, layout.DeviceByPackedDevice[1]);
        Assert.AreEqual(InputModifiers.Shift, layout.UnpackModifiers(2));

        var defaults = new Gw2InputActions(reader.ContentIndex, layout).Defaults().ToDictionary(binding => binding.ControlId);
        Assert.AreEqual(240, defaults.Count);
        Assert.AreEqual(new InputBind(InputDevice.Keyboard, KeyOne, InputModifiers.None), defaults[WeaponSkillOne].Primary);
        Assert.AreEqual(new InputBind(InputDevice.Keyboard, KeyW, InputModifiers.None), defaults[MoveForward].Primary);
        Assert.AreEqual(new InputBind(InputDevice.Keyboard, KeyUpArrow, InputModifiers.None), defaults[MoveForward].Secondary);
        Assert.AreEqual(new InputBind(InputDevice.Keyboard, KeyTab, InputModifiers.Shift), defaults[TargetPreviousEnemy].Primary);
    }

    [TestMethod]
    public void NamesEachControlWithTheTextTheOptionsPanelShows()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var layout = InputBindingLayout.Current;

        Assert.AreEqual(64, layout.NameTextIdOffset);
        var names = new Gw2InputActions(reader.ContentIndex, layout).NameTextIds();
        Assert.AreEqual("Weapon Skill 2", reader.ReadKnownText(names[WeaponSkillOne + 1], Gw2Language.English).Text);
        Assert.AreEqual("Previous Enemy", reader.ReadKnownText(names[TargetPreviousEnemy], Gw2Language.English).Text);
        Assert.AreEqual("Turn Left", reader.ReadKnownText(names[4], Gw2Language.English).Text);
        Assert.AreEqual("Turn Right", reader.ReadKnownText(names[5], Gw2Language.English).Text);
        Assert.AreEqual("Strafe Left", reader.ReadKnownText(names[2], Gw2Language.English).Text);
        Assert.AreEqual("Strafe Right", reader.ReadKnownText(names[3], Gw2Language.English).Text);
    }

    [TestMethod]
    public void DecodesTheEquippedBindingsFromTheAccountPreferencePack()
    {
        var equippedAtLogin = PreferencePackOf("network-209026-20261102-173407.gw2ncap", CaptureRecordKind.Inbound, AccountPreferencesFromServer);
        var uploadedAfterRebind = PreferencePackOf("network-209026-20261102-173407.gw2ncap", CaptureRecordKind.Outbound, AccountPreferencesToServer);
        var layout = InputBindingLayout.Current;

        Assert.AreEqual(0, InputBindingChunk.Decode(equippedAtLogin, layout).Count);
        var rebound = InputBindingChunk.Decode(uploadedAfterRebind, layout).Single();
        Assert.AreEqual(new InputControlBinding(WeaponSkillOne, new InputBind(InputDevice.Keyboard, KeyF10, InputModifiers.None), InputBind.Unbound), rebound);
    }

    [TestMethod]
    public void EncodesARebindIntoTheSamePackTheGameUploads()
    {
        var equippedAtLogin = PreferencePackOf("network-209026-20261102-173407.gw2ncap", CaptureRecordKind.Inbound, AccountPreferencesFromServer);
        var uploadedAfterRebind = PreferencePackOf("network-209026-20261102-173407.gw2ncap", CaptureRecordKind.Outbound, AccountPreferencesToServer);
        var layout = InputBindingLayout.Current;
        var rebound = new InputControlBinding(WeaponSkillOne, new InputBind(InputDevice.Keyboard, KeyF10, InputModifiers.None), InputBind.Unbound);

        CollectionAssert.AreEqual(uploadedAfterRebind, InputBindingChunk.ReplaceIn(equippedAtLogin, [rebound], layout));
        CollectionAssert.AreEqual(equippedAtLogin, InputBindingChunk.ReplaceIn(uploadedAfterRebind, [], layout));
    }

    [TestMethod]
    public void RoundTripsModifiersAndSecondaryBinds()
    {
        var layout = InputBindingLayout.Current;
        var bindings = new[]
        {
            new InputControlBinding(MoveForward, new InputBind(InputDevice.Keyboard, KeyW, InputModifiers.Shift | InputModifiers.Alt), new InputBind(InputDevice.Mouse, 3, InputModifiers.Control)),
            new InputControlBinding(WeaponSkillOne, new InputBind(InputDevice.Keyboard, KeyF10, InputModifiers.None), InputBind.Unbound),
        };
        var pack = InputBindingChunk.ReplaceIn(PreferencePackOf("network-209026-20261102-173407.gw2ncap", CaptureRecordKind.Inbound, AccountPreferencesFromServer), bindings, layout);

        CollectionAssert.AreEqual(bindings, InputBindingChunk.Decode(pack, layout).ToArray());
    }

    [TestMethod]
    public void ImportsALoadoutExportedByTheGame()
    {
        if (string.IsNullOrEmpty(Gw2Installation.ExecutablePath())) Assert.Inconclusive("Gw2-64.exe is not installed.");
        const string loadout = """
            <?xml version="1.0" encoding="utf-8"?>
            <InputBindings>
                <action name="Weapon Skill 1" id="18" device="Keyboard" button="41" mod="2" device2="Keyboard" button2="49"/>
            </InputBindings>
            """;

        var imported = InputBindingLoadout.Parse(loadout, InputBindingLayout.Current).Single();

        Assert.AreEqual(new InputControlBinding(
            WeaponSkillOne,
            new InputBind(InputDevice.Keyboard, KeyF10, InputModifiers.Control),
            new InputBind(InputDevice.Keyboard, KeyOne, InputModifiers.None)), imported);
    }

    private static byte[] PreferencePackOf(string captureName, CaptureRecordKind direction, int messageId)
    {
        var path = Path.Combine(RepositoryRoot(), "captures", "local", captureName);
        if (!File.Exists(path)) Assert.Inconclusive($"{captureName} is not in captures/local.");
        var capture = NetworkCapture.Read(path);
        if (capture.Build != RecordedSession.Schemas.Build) Assert.Inconclusive($"capture is build {capture.Build}");
        return CaptureDecoding.DecodeAll(capture, RecordedSession.Schemas)
            .Where(record => record.Stream.Direction == direction && record.Corpus.ToString() == "login")
            .SelectMany(record => record.Messages)
            .Where(message => message.MessageId == messageId)
            .Select(message => (byte[])message.Fields[PreferencePackField].Value!)
            .First();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "captures"))) directory = directory.Parent;
        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
