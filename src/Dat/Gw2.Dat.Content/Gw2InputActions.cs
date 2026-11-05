using System.Collections.Generic;
using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed class Gw2InputActions
{
    private const int BindValuesPointerOffset = 8;
    private const int ModifiersOffset = 4;

    private readonly Gw2ContentIndex _index;
    private readonly InputBindingLayout _layout;

    public Gw2InputActions(Gw2ContentIndex index, InputBindingLayout layout)
    {
        _index = index;
        _layout = layout;
    }

    public IReadOnlyList<InputControlBinding> Defaults()
    {
        var defaults = new List<InputControlBinding>();
        foreach (var action in _index.All(_layout.ActionContentType))
            defaults.Add(new InputControlBinding(
                _index.ReadUInt32(action, _layout.ControlIdOffset),
                DefaultBind(_index.ReadPointer(action, _layout.PrimaryBindOffset)),
                DefaultBind(_index.ReadPointer(action, _layout.SecondaryBindOffset))));
        return defaults;
    }

    public IReadOnlyDictionary<uint, uint> NameTextIds()
    {
        var names = new Dictionary<uint, uint>();
        foreach (var action in _index.All(_layout.ActionContentType))
            names[_index.ReadUInt32(action, _layout.ControlIdOffset)] = _index.ReadUInt32(action, _layout.NameTextIdOffset);
        return names;
    }

    private InputBind DefaultBind(Gw2ContentLocation? bind)
    {
        if (bind is not { } location) return InputBind.Unbound;
        var device = (InputDevice)_index.ReadUInt32(location, 0);
        if (device == InputDevice.None || _index.ReadPointer(location, BindValuesPointerOffset) is not { } values) return InputBind.Unbound;
        return new InputBind(device, _index.ReadUInt32(values, 0), (InputModifiers)_index.ReadUInt32(values, ModifiersOffset));
    }
}
