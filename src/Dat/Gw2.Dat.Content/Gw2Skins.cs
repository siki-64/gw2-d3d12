using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed record Gw2Skin(uint DataId, uint SkinType, uint? DefaultModelFileId, uint Socket, uint? WeaponKind, uint? FirstTypeDataValue, ulong? ArmorPartToken);

public sealed class Gw2Skins
{
    private readonly Gw2ContentIndex _index;
    private readonly SkinContentLayout _layout;

    public Gw2Skins(Gw2ContentIndex index, SkinContentLayout layout)
    {
        _index = index;
        _layout = layout;
    }

    public IEnumerable<Gw2Skin> All() => _index.All(_layout.SkinContentType).Select(Read);

    public Gw2Skin? Find(uint skinDataId) => _index.FindTyped(_layout.SkinContentType, skinDataId) is { } skin ? Read(skin) : null;

    private Gw2Skin Read(Gw2ContentLocation skin)
    {
        uint skinType = _index.ReadUInt32(skin, _layout.SkinTypeOffset);
        var typeData = _index.ReadPointer(skin, _layout.TypeDataOffset);
        return new Gw2Skin(
            _index.DataIdOf(_layout.SkinContentType, skin),
            skinType,
            _index.ReadFileId(skin, _layout.DefaultModelFileOffset) is { } fileId and not 0 ? fileId : null,
            _index.ReadUInt32(skin, _layout.SocketOffset),
            skinType == SkinContentLayout.WeaponSkinType && typeData is { } weapon ? _index.ReadUInt32(weapon, _layout.WeaponKindOffset) : null,
            typeData is { } data ? _index.ReadUInt32(data, 0) : null,
            skinType == SkinContentLayout.ArmorSkinType && typeData is { } armor ? BitConverter.ToUInt64(_index.ReadBytes(armor, 0, sizeof(ulong))) : null);
    }
}
