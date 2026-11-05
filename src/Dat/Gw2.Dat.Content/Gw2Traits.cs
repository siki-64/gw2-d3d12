using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed class Gw2Traits
{
    private readonly Gw2ContentIndex _index;
    private readonly uint _traitContentType;
    private readonly TraitContentLayout _layout;

    public Gw2Traits(Gw2ContentIndex index, SkillPaletteContentLayout palettes, TraitContentLayout layout)
    {
        _index = index;
        _traitContentType = palettes.TraitContentType;
        _layout = layout;
    }

    public uint? NameTextIdOf(uint traitDataId) =>
        _index.FindTyped(_traitContentType, traitDataId) is { } trait && _index.ReadUInt32(trait, _layout.NameTextOffset) is var textId and not 0 ? textId : null;
}
