using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Dat.Content;

namespace Gw2.Client.Scene;

public static class Gw2CharacterBodyParts
{
    public static readonly IReadOnlyList<Gw2CompositePartType> BareBody =
    [
        Gw2CompositePartType.SkinChest,
        Gw2CompositePartType.SkinFeet,
        Gw2CompositePartType.SkinHands,
        Gw2CompositePartType.SkinLegs,
        Gw2CompositePartType.Face,
        Gw2CompositePartType.Ears,
    ];

    public static Gw2CompositeRace? FindRace(Gw2Composite composite, string raceSexName)
    {
        ArgumentNullException.ThrowIfNull(composite);
        var token = Token64.Encode(raceSexName.ToLowerInvariant());
        return composite.Races.FirstOrDefault(race => Token64.Encode(race.Name.ToLowerInvariant()) == token);
    }

    public const uint FacePartType = (uint)Gw2CompositePartType.Face;
    public const uint HairPartType = 6;
    public const uint ArmorChestPartType = 9;
    public const uint ArmorLegsPartType = 12;
    public const int FemaleSex = 1;
    public const string ChestUnderwearName = "underwearic";
    public const string LegsUnderwearName = "underwearil";

    public const uint FirstArmorPartType = 8;
    private static readonly uint[] CompositeWalkOrder = [0, 1, 2, 3, 5, 6, 7, 4, 8, 9, 10, 11, 14, 13, 12];

    public static IReadOnlyList<Gw2CompositeFileData> PartsOf(Gw2CompositeRace race, int? faceIndex, int? hairStyleIndex, int sex) =>
        PartsOf(race, faceIndex, hairStyleIndex, sex, []);

    public static Gw2CompositeFileData? ArmorPartOf(Gw2Composite composite, Gw2CompositeRace race, ulong partToken)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(race);
        for (var searched = race; searched is not null; searched = VariantReferenceRaceOf(composite, searched))
            if (searched.FileData.FirstOrDefault(fileData => fileData.Type >= FirstArmorPartType && Token64.Encode(fileData.Name) == partToken && fileData.MeshBase.TryGetFileId(out var id) && id != 0) is { } part)
                return part;
        return null;
    }

    private static Gw2CompositeRace? VariantReferenceRaceOf(Gw2Composite composite, Gw2CompositeRace race) =>
        race.VariantRefRace == 0 || race.VariantRefRace == race.NameToken ? null : composite.Races.FirstOrDefault(candidate => candidate.NameToken == race.VariantRefRace);

    public static IReadOnlyList<Gw2CompositeFileData> PartsOf(Gw2CompositeRace race, int? faceIndex, int? hairStyleIndex, int sex, IReadOnlyList<Gw2CompositeFileData> armor)
    {
        ArgumentNullException.ThrowIfNull(armor);
        ArgumentNullException.ThrowIfNull(race);
        var parts = FirstPartOfEachType(race, BareBody.Where(type => type != Gw2CompositePartType.Face)).ToList();
        var face = NamedPart(race, FacePartType, race.Faces, faceIndex) ?? FirstPartOfEachType(race, [Gw2CompositePartType.Face]).FirstOrDefault();
        if (face is not null) parts.Add(face);
        if (NamedPart(race, HairPartType, race.HairStyles, hairStyleIndex) is { } hair) parts.Add(hair);
        var wornArmor = armor.GroupBy(part => part.Type).Select(group => group.Last()).ToList();
        parts.AddRange(wornArmor);
        bool Wears(uint partType) => wornArmor.Any(part => part.Type == partType);
        if (sex == FemaleSex && !Wears(ArmorChestPartType) && PartNamed(race, ArmorChestPartType, ChestUnderwearName) is { } chestUnderwear) parts.Add(chestUnderwear);
        if (!Wears(ArmorLegsPartType) && PartNamed(race, ArmorLegsPartType, LegsUnderwearName) is { } legsUnderwear) parts.Add(legsUnderwear);
        return parts.OrderBy(part => Array.IndexOf(CompositeWalkOrder, part.Type)).ToList();
    }

    public static IReadOnlyList<(Gw2CompositeFileData Part, Gw2CompositeVariantComponent Component)> VariantComponentPartsOf(Gw2Composite composite, Gw2CompositeRace race, Gw2CompositeVariant variant)
    {
        ArgumentNullException.ThrowIfNull(composite);
        ArgumentNullException.ThrowIfNull(race);
        ArgumentNullException.ThrowIfNull(variant);
        var resolved = new List<(Gw2CompositeFileData Part, Gw2CompositeVariantComponent Component)>();
        foreach (var component in variant.Components)
            if (ComponentPartOf(composite, race, component.NameToken) is { } part)
                resolved.Add((part, component));
        return resolved.GroupBy(entry => entry.Part.Type).Select(group => group.Last()).ToList();
    }

    public static IReadOnlyList<Gw2CompositeFileData> VariantPartsOf(Gw2CompositeRace race, IReadOnlyList<Gw2CompositeFileData> componentParts)
    {
        ArgumentNullException.ThrowIfNull(race);
        ArgumentNullException.ThrowIfNull(componentParts);
        var coveredTypes = componentParts.Select(part => part.Type).ToHashSet();
        var parts = FirstPartOfEachType(race, BareBody.Where(type => !coveredTypes.Contains((uint)type))).ToList();
        parts.AddRange(componentParts);
        return parts.OrderBy(part => Array.IndexOf(CompositeWalkOrder, part.Type)).ToList();
    }

    private static Gw2CompositeFileData? ComponentPartOf(Gw2Composite composite, Gw2CompositeRace race, ulong nameToken)
    {
        for (var searched = race; searched is not null; searched = VariantReferenceRaceOf(composite, searched))
            if (searched.FileData.FirstOrDefault(fileData => Token64.Encode(fileData.Name) == nameToken && fileData.MeshBase.TryGetFileId(out var id) && id != 0) is { } part)
                return part;
        return null;
    }

    private static Gw2CompositeFileData? PartNamed(Gw2CompositeRace race, uint partType, string name)
    {
        var token = Token64.Encode(name);
        return race.FileData.FirstOrDefault(fileData => fileData.Type == partType && Token64.Encode(fileData.Name) == token && fileData.MeshBase.TryGetFileId(out var id) && id != 0);
    }

    private static Gw2CompositeFileData? NamedPart(Gw2CompositeRace race, uint partType, IReadOnlyList<ulong> nameTokens, int? index)
    {
        if (index is not { } chosen || chosen < 0 || chosen >= nameTokens.Count) return null;
        var token = nameTokens[chosen];
        return race.FileData.FirstOrDefault(fileData => fileData.Type == partType && Token64.Encode(fileData.Name) == token && fileData.MeshBase.TryGetFileId(out var id) && id != 0);
    }

    public static IReadOnlyList<Gw2CompositeFileData> FirstPartOfEachType(Gw2CompositeRace race, IEnumerable<Gw2CompositePartType> partTypes)
    {
        ArgumentNullException.ThrowIfNull(race);
        var parts = new List<Gw2CompositeFileData>();
        foreach (var partType in partTypes)
        {
            var part = race.FileData.FirstOrDefault(fileData => fileData.Type == (uint)partType && fileData.MeshBase.TryGetFileId(out var id) && id != 0);
            if (part is not null) parts.Add(part);
        }

        return parts;
    }
}
