using System;

namespace Gw2.Formats;

public enum InputDevice : uint
{
    None = 0,
    Mouse = 1,
    Keyboard = 2,
    Gamepad = 3,
}

[Flags]
public enum InputModifiers : uint
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

public readonly record struct InputBind(InputDevice Device, uint Button, InputModifiers Modifiers)
{
    public static readonly InputBind Unbound = new(InputDevice.None, 0, InputModifiers.None);

    public bool IsBound => Device != InputDevice.None;
}

public sealed record InputControlBinding(uint ControlId, InputBind Primary, InputBind Secondary);
