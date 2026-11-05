using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed record Gw2Bundle(uint? MainHandItemDataId, uint? OffHandItemDataId, uint? MainHandPaletteDataId, uint? OffHandPaletteDataId, uint? PaletteDataId, uint? DownedPaletteDataId);

public sealed class Gw2Transformations
{
    private readonly Gw2ContentIndex _index;
    private readonly TransformationContentLayout _layout;
    private readonly ItemContentLayout _items;
    private readonly SkillPaletteContentLayout _palettes;

    public Gw2Transformations(Gw2ContentIndex index, TransformationContentLayout layout, ItemContentLayout items, SkillPaletteContentLayout palettes)
    {
        _index = index;
        _layout = layout;
        _items = items;
        _palettes = palettes;
    }

    public Gw2Bundle? BundleOf(uint transformationUid)
    {
        if (_index.Find(_layout.TransformationContentType, transformationUid) is not { } transformation) return null;
        if (_index.ReadPointer(transformation, _layout.BundleOffset) is not { } bundle) return null;
        var mainHand = _index.ReadPointer(bundle, _layout.BundleMainHandItemOffset);
        var offHand = _index.ReadPointer(bundle, _layout.BundleOffHandItemOffset);
        return new Gw2Bundle(
            mainHand is { } main ? _index.DataIdOf(_items.ItemContentType, main) : null,
            offHand is { } off ? _index.DataIdOf(_items.ItemContentType, off) : null,
            mainHand is { } mainItem ? WeaponPaletteOf(mainItem) : null,
            offHand is { } offItem ? WeaponPaletteOf(offItem) : null,
            PaletteDataIdAt(bundle, _layout.BundlePaletteOffset),
            PaletteDataIdAt(bundle, _layout.BundleDownedPaletteOffset));
    }

    public uint? WeaponPaletteDataIdOfItem(uint itemDataId) =>
        _index.FindTyped(_items.ItemContentType, itemDataId) is { } item ? WeaponPaletteOf(item) : null;

    private uint? WeaponPaletteOf(Gw2ContentLocation item)
    {
        if (_index.ReadUInt32(item, _items.ItemTypeOffset) != _items.WeaponItemType) return null;
        return _index.ReadPointer(item, _items.SubRecordOffset) is { } weapon ? PaletteDataIdAt(weapon, _layout.WeaponPaletteOffset) : null;
    }

    private uint? PaletteDataIdAt(Gw2ContentLocation record, int offset) =>
        _index.ReadPointer(record, offset) is { } palette ? _index.DataIdOf(_palettes.PaletteContentType, palette) : null;
}
