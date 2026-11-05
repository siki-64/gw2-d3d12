using System.Collections.Generic;
using System.IO;
using System.Linq;
using Crystal.Input;
using Gw2.Formats;

namespace Gw2.Client.Settings;

public sealed class Hotkeys
{
    public static readonly uint[] WeaponSkillControls = [21, 22, 23, 24, 25];
    public static readonly uint[] BarSkillControls = [26, 27, 28, 29, 30];
    public static readonly uint[] MechanicControls = [32, 33, 34, 35, 36];

    private readonly Dictionary<uint, InputControlBinding> _defaults = [];
    private readonly Dictionary<uint, InputControlBinding> _bindings = [];

    public Hotkeys(IEnumerable<InputControlBinding> defaults)
    {
        foreach (var binding in defaults) _defaults[binding.ControlId] = binding;
        Overlay(_defaults.Values);
    }

    public IEnumerable<uint> ControlIds => _bindings.Keys.Order();

    public void Overlay(IEnumerable<InputControlBinding> bindings)
    {
        foreach (var binding in bindings) _bindings[binding.ControlId] = binding;
    }

    public void EquipFromPreferencePack(byte[] preferencePack, InputBindingLayout layout) => Overlay(InputBindingChunk.Decode(preferencePack, layout));

    public void ImportLoadout(string path, InputBindingLayout layout) => Overlay(InputBindingLoadout.Parse(File.ReadAllText(path), layout));

    public void Rebind(uint controlId, bool secondary, InputBind bind)
    {
        var current = BindingOf(controlId) ?? new InputControlBinding(controlId, InputBind.Unbound, InputBind.Unbound);
        _bindings[controlId] = secondary ? current with { Secondary = bind } : current with { Primary = bind };
    }

    public IReadOnlyList<InputControlBinding> ChangedFromDefaults() =>
        _bindings.Values.Where(binding => !_defaults.TryGetValue(binding.ControlId, out var defaults) || defaults != binding).ToList();

    public InputControlBinding? BindingOf(uint controlId) => _bindings.TryGetValue(controlId, out var binding) ? binding : null;

    public bool IsPressed(InputFrame input, uint controlId) =>
        BindingOf(controlId) is { } binding && (IsBindPressed(input, binding.Primary) || IsBindPressed(input, binding.Secondary));

    public bool IsDown(InputFrame input, uint controlId) =>
        BindingOf(controlId) is { } binding && (IsBindDown(input, binding.Primary) || IsBindDown(input, binding.Secondary));

    public int PressedIndexOf(InputFrame input, uint[] controls)
    {
        for (var index = 0; index < controls.Length; index++)
            if (IsPressed(input, controls[index])) return index;
        return -1;
    }

    public string LabelOf(uint controlId)
    {
        if (BindingOf(controlId) is not { } binding) return "";
        return LabelOf(binding.Primary.IsBound ? binding.Primary : binding.Secondary);
    }

    public static string LabelOf(InputBind bind)
    {
        if (!bind.IsBound) return "";
        var prefix = (bind.Modifiers.HasFlag(InputModifiers.Shift) ? "S" : "") + (bind.Modifiers.HasFlag(InputModifiers.Control) ? "C" : "") + (bind.Modifiers.HasFlag(InputModifiers.Alt) ? "A" : "");
        var name = bind.Device switch
        {
            InputDevice.Keyboard => GameKeys.KeyOf(bind.Button) is { } key ? GameKeys.LabelOf(key) : $"Key {bind.Button}",
            InputDevice.Mouse => bind.Button < MouseButtonLabels.Length ? MouseButtonLabels[bind.Button] : $"Mouse {bind.Button}",
            _ => $"Pad {bind.Button}",
        };
        return prefix.Length == 0 ? name : $"{prefix}-{name}";
    }

    private static readonly string[] MouseButtonLabels = ["M1", "M3", "M2", "M4", "M5"];

    private static bool IsBindPressed(InputFrame input, InputBind bind) => bind.Device switch
    {
        InputDevice.Keyboard => GameKeys.KeyOf(bind.Button) is { } key && input.IsKeyPressed(key) && ModifiersHeld(input, bind.Modifiers),
        InputDevice.Mouse => MouseButtonOf(bind.Button) is { } button && input.IsMouseButtonPressed(button) && ModifiersHeld(input, bind.Modifiers),
        _ => false,
    };

    private static bool IsBindDown(InputFrame input, InputBind bind) => bind.Device switch
    {
        InputDevice.Keyboard => GameKeys.KeyOf(bind.Button) is { } key && input.Keyboard.IsKeyDown(key),
        InputDevice.Mouse => MouseButtonOf(bind.Button) is { } button && input.IsMouseButtonDown(button),
        _ => false,
    };

    private static MouseButton? MouseButtonOf(uint gameButton) => gameButton <= (uint)MouseButton.XButton2 ? (MouseButton)gameButton : null;

    private static bool ModifiersHeld(InputFrame input, InputModifiers modifiers) =>
        HeldMatches(input, modifiers, InputModifiers.Shift, Keys.LeftShift, Keys.RightShift)
        && HeldMatches(input, modifiers, InputModifiers.Control, Keys.LeftControl, Keys.RightControl)
        && HeldMatches(input, modifiers, InputModifiers.Alt, Keys.LeftAlt, Keys.RightAlt);

    private static bool HeldMatches(InputFrame input, InputModifiers modifiers, InputModifiers modifier, Keys left, Keys right) =>
        modifiers.HasFlag(modifier) == (input.Keyboard.IsKeyDown(left) || input.Keyboard.IsKeyDown(right));
}
