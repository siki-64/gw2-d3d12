using Gw2.Formats;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;

namespace Gw2.Client.Scene;

public static class Gw2CharacterSkinPattern
{
    public static Gw2CompositeSkinPattern? Select(Gw2CompositeRace race, ushort patternIndex) =>
        patternIndex == 0 || race.SkinPatterns.Count == 0 ? null : race.SkinPatterns[(patternIndex - 1) % race.SkinPatterns.Count];

    public static PackFileReference TextureFor(Gw2CompositeSkinPattern pattern, uint partType) => partType switch
    {
        0 => pattern.Chest,
        5 => pattern.Face,
        1 => pattern.Feet,
        2 => pattern.Hands,
        3 => pattern.Legs,
        7 => pattern.Ears,
        _ => default,
    };
}
