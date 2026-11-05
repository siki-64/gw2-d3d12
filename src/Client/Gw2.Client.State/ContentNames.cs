using System;
using System.Collections.Generic;
using Gw2.Client.Net.Game;
using Gw2.Dat.Archive;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.State;

public sealed class ContentNames
{
    private readonly Func<Gw2ContentReader?> _reader;
    private readonly WorldState _world;
    private readonly Dictionary<uint, string?> _speciesNames = [];
    private readonly Dictionary<uint, uint?> _speciesModelFiles = [];
    private readonly Dictionary<uint, string> _traitNames = [];
    private readonly Dictionary<uint, string?> _itemNames = [];
    private readonly Dictionary<string, string?> _gadgetNames = [];
    private Gw2Items? _items;
    private Gw2SpeciesDefinitions? _species;
    private Gw2Traits? _traits;

    public ContentNames(Func<Gw2ContentReader?> reader, WorldState world)
    {
        _reader = reader;
        _world = world;
    }

    public string AgentLabelOf(uint agentId) =>
        _world.Agents.TryGet(agentId, out var agent) ? _world.Agents.PlayerNameOf(agent) ?? SpeciesNameOf(agent) ?? $"agent {agentId}" : $"agent {agentId}";

    public string? SpeciesNameOf(GameAgent agent)
    {
        if (agent.SpeciesId is not { } speciesId || _reader() is not { } reader) return null;
        if (_speciesNames.TryGetValue(speciesId, out var known)) return known;
        _species ??= new Gw2SpeciesDefinitions(reader.ContentIndex, SpeciesContentLayout.Current);
        var name = _species.NameTextIdOf(speciesId) is { } textId ? PlainTextOf(reader, textId) : null;
        _speciesNames[speciesId] = name;
        return name;
    }

    public uint? SpeciesModelFileIdOf(GameAgent agent)
    {
        if (agent.SpeciesId is not { } speciesId || _reader() is not { } reader) return null;
        if (_speciesModelFiles.TryGetValue(speciesId, out var known)) return known;
        _species ??= new Gw2SpeciesDefinitions(reader.ContentIndex, SpeciesContentLayout.Current);
        var modelFileId = _species.ModelFileIdOf(speciesId);
        _speciesModelFiles[speciesId] = modelFileId;
        return modelFileId;
    }

    public string? ItemNameOf(GameKeyframedAgent agent)
    {
        if (agent.AgentType != GameAgentType.Item || _world.Equipment.ItemDataIdOfInstance(agent.ContentInstanceId) is not { } itemDataId || _reader() is not { } reader) return null;
        if (_itemNames.TryGetValue(itemDataId, out var known)) return known;
        _items ??= new Gw2Items(reader.ContentIndex, ItemContentLayout.Current);
        var name = _items.NameTextIdOf(itemDataId) is { } textId ? PlainTextOf(reader, textId) : null;
        if (name is not null) _itemNames[itemDataId] = name;
        return name;
    }

    public string TraitNameOf(uint traitDataId)
    {
        if (_traitNames.TryGetValue(traitDataId, out var known)) return known;
        string name = $"trait {traitDataId}";
        if (_reader() is { } reader)
        {
            _traits ??= new Gw2Traits(reader.ContentIndex, SkillPaletteContentLayout.Current, TraitContentLayout.Current);
            if (_traits.NameTextIdOf(traitDataId) is { } textId && PlainTextOf(reader, textId) is { } text) name = text;
        }
        _traitNames[traitDataId] = name;
        return name;
    }

    public string? GadgetNameOf(GameKeyframedAgent agent)
    {
        if (agent.Gadget is not { HasNameplate: true, NameIsTemplated: false } gadget || _reader() is not { } reader) return null;
        if (gadget.NameTextId != 0) return PlainTextOf(reader, gadget.NameTextId);
        if (!_gadgetNames.TryGetValue(gadget.CodedName, out var name))
            _gadgetNames[gadget.CodedName] = name = CodedNpcName.TryToTextId(gadget.CodedName, out var textId, out var key) ? PlainTextOf(reader.ReadText(textId, Gw2Language.English, key)) : null;
        return name;
    }

    private static string? PlainTextOf(Gw2ContentReader reader, uint textId) => PlainTextOf(reader.ReadKnownText(textId, Gw2Language.English));

    private static string? PlainTextOf((Gw2TextStatus Status, string Text) read)
    {
        if (read.Status is not (Gw2TextStatus.Plain or Gw2TextStatus.Decrypted)) return null;
        var text = Gw2TextTemplate.WithoutTags(read.Text);
        return text.Length == 0 ? null : text;
    }
}
