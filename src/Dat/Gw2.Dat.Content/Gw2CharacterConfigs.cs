using Gw2.Dat.Archive;
using Gw2.Formats;

namespace Gw2.Dat.Content;

public sealed class Gw2CharacterConfigs
{
    private readonly Gw2ContentIndex _index;
    private readonly CharacterConfigTable _table;

    public Gw2CharacterConfigs(Gw2ContentIndex index, CharacterConfigTable table)
    {
        _index = index;
        _table = table;
    }

    public float? Number(int config)
    {
        if (_table.Entry(config) is not { } entry) return null;
        if (_index.FindByContentKey(_table.ConfigurationContentType, entry.ContentKey) is not { } configuration) return null;
        if (_index.ReadUInt32(configuration, _table.ValueTypeOffset) != entry.ValueContentType) return null;
        if (_index.ReadUInt32(configuration, _table.ValueOffset) != entry.ValueContentType) return null;
        return _index.ReadSingle(configuration, _table.NumberOffset);
    }
}
