using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed class Gw2Items
{
    private readonly Gw2ContentIndex _index;
    private readonly ItemContentLayout _layout;

    public Gw2Items(Gw2ContentIndex index, ItemContentLayout layout)
    {
        _index = index;
        _layout = layout;
    }

    public uint? NameTextIdOf(uint itemDataId) =>
        _index.FindTyped(_layout.ItemContentType, itemDataId) is { } item && _index.ReadUInt32(item, _layout.NameTextOffset) is var textId and not 0 ? textId : null;

    public WeaponType? WeaponTypeOf(uint itemDataId)
    {
        if (_index.FindTyped(_layout.ItemContentType, itemDataId) is not { } item) return null;
        if (_index.ReadUInt32(item, _layout.ItemTypeOffset) != _layout.WeaponItemType) return null;
        return _index.ReadPointer(item, _layout.SubRecordOffset) is { } weapon ? (WeaponType)_index.ReadUInt32(weapon, _layout.WeaponTypeOffset) : null;
    }

    public uint? DefaultSkinDataIdOf(uint skinContentType, uint itemDataId)
    {
        if (_index.FindTyped(_layout.ItemContentType, itemDataId) is not { } item) return null;
        if (_index.ReadPointer(item, _layout.SubRecordOffset) is not { } subRecord) return null;
        return _index.ReadPointer(subRecord, _layout.DefaultSkinOffset) is { } skin ? _index.DataIdOf(skinContentType, skin) : null;
    }
}
