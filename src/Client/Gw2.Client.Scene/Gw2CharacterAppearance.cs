using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Gw2.Client.Scene;

public sealed record Gw2CharacterAppearancePart(
    byte Type,
    uint Flags,
    uint Mesh,
    uint MeshOverlap,
    uint DyeMask1,
    uint DyeMask2,
    uint DyeMask3,
    uint DyeMask4,
    uint CutMask,
    uint Texture,
    uint NormalTexture,
    uint HideFlags,
    uint SkinFlags,
    ushort BlitRectIndex,
    IReadOnlyList<Gw2ColorShift> DyeChannels);

public sealed record Gw2CharacterAppearance(
    uint AgentId,
    uint SpeciesModelFile,
    string Race,
    Gw2ColorShift HairColor,
    Gw2ColorShift SecondHairColor,
    Gw2ColorShift SkinPatternColor,
    Gw2ColorShift SkinColor,
    uint SkinPatternIndex,
    IReadOnlyList<Gw2CharacterAppearancePart> Parts)
{
    public bool IsSpeciesModel => Parts.Count == 0;

    public string PartSetKey { get; } = KeyOf(SpeciesModelFile, Race, HairColor, SecondHairColor, SkinPatternColor, SkinColor, SkinPatternIndex, Parts);

    private static string KeyOf(uint speciesModelFile, string race, Gw2ColorShift hairColor, Gw2ColorShift secondHairColor, Gw2ColorShift skinPatternColor, Gw2ColorShift skinColor, uint skinPatternIndex, IReadOnlyList<Gw2CharacterAppearancePart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0) return string.Create(CultureInfo.InvariantCulture, $"species {speciesModelFile}");
        var key = new StringBuilder(race);
        key.Append(CultureInfo.InvariantCulture, $"|{hairColor}|{secondHairColor}|{skinPatternColor}|{skinColor}|{skinPatternIndex}");
        foreach (var part in parts)
        {
            key.Append(CultureInfo.InvariantCulture, $"|{part.Type},{part.Flags},{part.Mesh},{part.MeshOverlap},{part.DyeMask1},{part.DyeMask2},{part.DyeMask3},{part.DyeMask4},{part.CutMask},{part.Texture},{part.NormalTexture},{part.HideFlags},{part.SkinFlags},{part.BlitRectIndex}");
            foreach (var dye in part.DyeChannels) key.Append(CultureInfo.InvariantCulture, $",{dye}");
        }
        return key.ToString();
    }
}
