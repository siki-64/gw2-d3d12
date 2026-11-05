using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Gw2.Formats;

public static class InputBindingLoadout
{
    public static IReadOnlyList<InputControlBinding> Parse(string xml, InputBindingLayout layout)
    {
        var bindings = new List<InputControlBinding>();
        foreach (var action in XDocument.Parse(xml).Root?.Elements("action") ?? [])
        {
            if (Number(action, "id") is not { } packed || packed > byte.MaxValue || !layout.ControlIdByPackedControl.TryGetValue((byte)packed, out var controlId)) continue;
            bindings.Add(new InputControlBinding(controlId, Bind(action, "device", "button", "mod"), Bind(action, "device2", "button2", "mod2")));
        }
        return bindings;
    }

    private static InputBind Bind(XElement action, string deviceName, string buttonName, string modifiersName) =>
        Enum.TryParse<InputDevice>((string?)action.Attribute(deviceName), out var device) && device != InputDevice.None
            ? new InputBind(device, Number(action, buttonName) ?? 0, (InputModifiers)(Number(action, modifiersName) ?? 0))
            : InputBind.Unbound;

    private static uint? Number(XElement action, string name) =>
        uint.TryParse((string?)action.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
}
