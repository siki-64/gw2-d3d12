using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed class Gw2SpeciesDefinitions
{
    public const uint SpeciesContentType = 0x43;

    private readonly Gw2ContentIndex _index;
    private readonly SpeciesContentLayout _layout;

    public Gw2SpeciesDefinitions(Gw2ContentIndex index, SpeciesContentLayout layout)
    {
        _index = index;
        _layout = layout;
    }

    public uint? NameTextIdOf(uint speciesId) =>
        _index.FindTyped(SpeciesContentType, speciesId) is { } species && _index.ReadUInt32(species, _layout.NameTextOffset) is var textId and not 0 ? textId : null;

    public uint? ModelFileIdOf(uint speciesId) =>
        _index.FindTyped(SpeciesContentType, speciesId) is { } species && _index.ReadFileId(species, _layout.ModelFileOffset) is { } fileId and not 0 ? fileId : null;
}
