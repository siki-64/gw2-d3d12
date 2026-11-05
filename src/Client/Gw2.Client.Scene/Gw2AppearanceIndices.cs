using System;

namespace Gw2.Client.Scene;

public sealed record Gw2AppearanceIndices(int Race, int Sex, int FaceIndex, int HairColorIndex, int SecondHairColorIndex, int HairStyleIndex, int SkinColorIndex, int SkinPatternIndex, int SkinPatternColorIndex, int EyeColorIndex)
{
    public const int Length = 45;
    public const int FaceAt = 0x0c;
    public const int HairColorAt = 0x0e;
    public const int SecondHairColorAt = 0x0f;
    public const int HairStyleAt = 0x10;
    public const int RaceAt = 0x1b;
    public const int SexAt = 0x1d;
    public const int SkinColorAt = 0x1e;
    public const int SkinPatternAt = 0x1f;
    public const int SkinPatternColorAt = 0x20;
    public const int EyeColorAt = 0x2c;
    public const int NoPart = 0xff;
    public const int SylvariRace = 4;

    public string? CompositeRaceName => Gw2CharacterRaces.CompositeRaceNameOf(Race, Sex);

    public static Gw2AppearanceIndices? Of(ReadOnlySpan<byte> block) =>
        block.Length < Length ? null : new Gw2AppearanceIndices(
            block[RaceAt], block[SexAt], block[FaceAt], block[RaceAt] == SylvariRace ? block[SkinColorAt] : block[HairColorAt], block[SecondHairColorAt], block[HairStyleAt],
            block[SkinColorAt], block[SkinPatternAt], block[SkinPatternColorAt], block[EyeColorAt]);
}
