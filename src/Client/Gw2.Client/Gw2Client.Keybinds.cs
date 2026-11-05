using System;
using System.Collections.Generic;
using System.Linq;
using Crystal.Input;
using Gw2.Client.Settings;
using Gw2.Client.Ui;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client;

public sealed partial class Gw2Client
{
    private const int MaximumKeysHeldAtOnce = 16;

    private static readonly Keys[] ModifierKeys = [Keys.LeftShift, Keys.RightShift, Keys.LeftControl, Keys.RightControl, Keys.LeftAlt, Keys.RightAlt];

    private Dictionary<uint, string>? _controlNames;

    private bool IsKeybindWindowOpen => _perfOverlay?.Keybinds.IsOpen == true;

    private void UpdateKeybindWindow()
    {
        if (_perfOverlay?.Keybinds is not { IsOpen: true } window || _reader is null) return;
        var hotkeys = HotkeysOf();
        var changed = window.Rows.Count == 0;
        if (window.Capturing is { } slot && CapturedBind() is { } bind)
        {
            window.Capturing = null;
            if (bind.IsBound)
            {
                hotkeys.Rebind(slot.ControlId, slot.Secondary, bind);
                UploadInputBindings(hotkeys);
                changed = true;
            }
        }
        if (changed) window.Rows = KeybindRowsOf(hotkeys);
    }

    private void UploadInputBindings(Hotkeys hotkeys)
    {
        if (_login?.AccountPreferencePack is not { } preferencePack) return;
        _login.UploadAccountPreferences(InputBindingChunk.ReplaceIn(preferencePack, hotkeys.ChangedFromDefaults(), InputBindingLayout.Current));
    }

    private InputBind? CapturedBind()
    {
        if (_input.IsKeyPressed(Keys.Escape)) return InputBind.Unbound;
        Span<Keys> held = stackalloc Keys[MaximumKeysHeldAtOnce];
        var count = _input.Keyboard.GetPressedKeys(held);
        foreach (var key in held[..count])
        {
            if (ModifierKeys.Contains(key) || !_input.IsKeyPressed(key) || GameKeys.GameKeyOf(key) is not { } gameKey) continue;
            return new InputBind(InputDevice.Keyboard, gameKey, HeldModifiers());
        }
        return null;
    }

    private InputModifiers HeldModifiers()
    {
        var modifiers = InputModifiers.None;
        if (_input.Keyboard.IsKeyDown(Keys.LeftShift) || _input.Keyboard.IsKeyDown(Keys.RightShift)) modifiers |= InputModifiers.Shift;
        if (_input.Keyboard.IsKeyDown(Keys.LeftControl) || _input.Keyboard.IsKeyDown(Keys.RightControl)) modifiers |= InputModifiers.Control;
        if (_input.Keyboard.IsKeyDown(Keys.LeftAlt) || _input.Keyboard.IsKeyDown(Keys.RightAlt)) modifiers |= InputModifiers.Alt;
        return modifiers;
    }

    private List<UiKeybindRow> KeybindRowsOf(Hotkeys hotkeys)
    {
        _controlNames ??= ControlNamesOf(_reader!);
        var rows = new List<UiKeybindRow>();
        foreach (var controlId in hotkeys.ControlIds)
        {
            if (!_controlNames.TryGetValue(controlId, out var name) || hotkeys.BindingOf(controlId) is not { } binding) continue;
            rows.Add(new UiKeybindRow(controlId, name, Hotkeys.LabelOf(binding.Primary), Hotkeys.LabelOf(binding.Secondary)));
        }
        return rows;
    }

    private static Dictionary<uint, string> ControlNamesOf(Gw2ContentReader reader)
    {
        var names = new Dictionary<uint, string>();
        foreach (var (controlId, textId) in new Gw2InputActions(reader.ContentIndex, InputBindingLayout.Current).NameTextIds())
        {
            var read = reader.ReadKnownText(textId, Gw2Language.English);
            if (read.Status is not (Gw2TextStatus.Plain or Gw2TextStatus.Decrypted)) continue;
            var name = Gw2TextTemplate.WithoutTags(read.Text);
            if (name.Length > 0) names[controlId] = name;
        }
        return names;
    }
}
