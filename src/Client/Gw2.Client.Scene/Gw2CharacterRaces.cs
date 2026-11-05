namespace Gw2.Client.Scene;

public static class Gw2CharacterRaces
{
    public const int RaceCount = 5;
    public const int SexCount = 2;

    private static readonly string[] CompositeRaceNames =
    [
        "asuramale", "asurafemale",
        "charrmale", "charrfemale",
        "humanmale", "humanfemale",
        "nornmale", "nornfemale",
        "sylvarimale", "sylvarifemal",
    ];

    public static (int Race, int Sex)? RaceAndSexOf(string compositeRaceName)
    {
        var index = System.Array.IndexOf(CompositeRaceNames, compositeRaceName.ToLowerInvariant());
        return index < 0 ? null : (index / SexCount, index % SexCount);
    }

    public static string? CompositeRaceNameOf(int race, int sex) =>
        race is >= 0 and < RaceCount && sex is >= 0 and < SexCount ? CompositeRaceNames[race * SexCount + sex] : null;
}
