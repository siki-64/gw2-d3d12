using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Gw2.Formats;

public static class InputBindingChunk
{
    private const uint ChunkMagic = 0x74706E69;
    private const int ChunkHeaderSizeOffset = 10;
    private const int ActionsOffsetField = 4;
    private const int ActionSize = 7;
    private const int PrimaryOffset = 1;
    private const int SecondaryOffset = 4;
    private const ushort ChunkVersion = 1;
    private const ushort ChunkHeaderSize = 0x10;
    private const int ListHeaderSize = 13;
    private const byte LastAction = 0xDF;
    private const int ActionsAlignment = 8;
    private const int ChunkDataAlignment = 8;

    public static IReadOnlyList<InputControlBinding> Decode(ReadOnlySpan<byte> preferencePack, InputBindingLayout layout)
    {
        var data = FindChunkData(preferencePack);
        if (data.Length < ActionsOffsetField + 8) return [];
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var actionsStart = checked(ActionsOffsetField + (int)BinaryPrimitives.ReadUInt64LittleEndian(data[ActionsOffsetField..]));
        var bindings = new List<InputControlBinding>((int)count);
        for (var index = 0; index < count; index++)
        {
            var action = data.Slice(actionsStart + index * ActionSize, ActionSize);
            if (!layout.ControlIdByPackedControl.TryGetValue(action[0], out var controlId)) continue;
            bindings.Add(new InputControlBinding(controlId, Bind(action[PrimaryOffset..], layout), Bind(action[SecondaryOffset..], layout)));
        }
        return bindings;
    }

    public static byte[] Encode(IEnumerable<InputControlBinding> changedBindings, InputBindingLayout layout)
    {
        var actions = new List<byte[]>();
        foreach (var binding in changedBindings.OrderBy(binding => binding.ControlId))
        {
            if (layout.PackedControlOf(binding.ControlId) is not { } packedControl) continue;
            var action = new byte[ActionSize];
            action[0] = packedControl;
            Pack(binding.Primary, layout, action.AsSpan(PrimaryOffset));
            Pack(binding.Secondary, layout, action.AsSpan(SecondaryOffset));
            actions.Add(action);
        }
        var actionsStart = actions.Count == 0 ? ListHeaderSize : AlignUp(ListHeaderSize, ActionsAlignment);
        var descriptorOffset = actionsStart + actions.Count * ActionSize;
        var data = new byte[AlignUp(descriptorOffset + PointerTableSize, ChunkDataAlignment)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)actions.Count);
        if (actions.Count > 0) BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(ActionsOffsetField), (ulong)(actionsStart - ActionsOffsetField));
        data[ListHeaderSize - 1] = LastAction;
        for (var index = 0; index < actions.Count; index++) actions[index].CopyTo(data, actionsStart + index * ActionSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(descriptorOffset), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(descriptorOffset + 4), ActionsOffsetField);
        return AccountPreferencePack.Chunk(ChunkMagic, ChunkVersion, ChunkHeaderSize, (uint)descriptorOffset, data);
    }

    public static byte[] ReplaceIn(byte[] preferencePack, IEnumerable<InputControlBinding> changedBindings, InputBindingLayout layout) =>
        AccountPreferencePack.ReplaceChunk(preferencePack, ChunkMagic, Encode(changedBindings, layout));

    private const int PointerTableSize = 8;

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

    private static void Pack(InputBind bind, InputBindingLayout layout, Span<byte> destination)
    {
        if (!bind.IsBound) return;
        destination[0] = layout.PackedDeviceOf(bind.Device);
        destination[1] = (byte)bind.Button;
        destination[2] = bind.Device == InputDevice.Gamepad ? (byte)bind.Modifiers : layout.PackModifiers(bind.Modifiers);
    }

    private static InputBind Bind(ReadOnlySpan<byte> packed, InputBindingLayout layout)
    {
        var device = layout.DeviceByPackedDevice.TryGetValue(packed[0], out var unpacked) ? unpacked : InputDevice.None;
        return device switch
        {
            InputDevice.None => InputBind.Unbound,
            InputDevice.Gamepad => new InputBind(device, packed[1], (InputModifiers)packed[2]),
            _ => new InputBind(device, packed[1], layout.UnpackModifiers(packed[2])),
        };
    }

    private static ReadOnlySpan<byte> FindChunkData(ReadOnlySpan<byte> pack)
    {
        if (AccountPreferencePack.FindChunk(pack, ChunkMagic) is not var (offset, length)) return default;
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(pack[(offset + ChunkHeaderSizeOffset)..]);
        return pack.Slice(offset + headerSize, length - headerSize);
    }
}
