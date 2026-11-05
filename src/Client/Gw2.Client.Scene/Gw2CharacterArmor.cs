using System;
using System.Collections.Generic;
using System.Linq;

namespace Gw2.Client.Scene;

public readonly record struct Gw2ArmorPiece(uint SkinDataId, ushort FirstDyeDataId, ushort SecondDyeDataId, ushort ThirdDyeDataId, ushort FourthDyeDataId)
{
    public IReadOnlyList<ushort> DyeDataIds => [FirstDyeDataId, SecondDyeDataId, ThirdDyeDataId, FourthDyeDataId];

    public static Gw2ArmorPiece Of(uint skinDataId, IReadOnlyList<ushort> dyeDataIds)
    {
        ArgumentNullException.ThrowIfNull(dyeDataIds);
        ushort DyeAt(int channel) => channel < dyeDataIds.Count ? dyeDataIds[channel] : (ushort)0;
        return new Gw2ArmorPiece(skinDataId, DyeAt(0), DyeAt(1), DyeAt(2), DyeAt(3));
    }
}

public sealed class Gw2CharacterArmor : IEquatable<Gw2CharacterArmor>
{
    public static readonly Gw2CharacterArmor None = new([]);

    public Gw2CharacterArmor(IReadOnlyList<Gw2ArmorPiece> pieces) => Pieces = pieces ?? throw new ArgumentNullException(nameof(pieces));

    public IReadOnlyList<Gw2ArmorPiece> Pieces { get; }

    public bool Equals(Gw2CharacterArmor? other) => other is not null && Pieces.SequenceEqual(other.Pieces);

    public override bool Equals(object? obj) => Equals(obj as Gw2CharacterArmor);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var piece in Pieces) hash.Add(piece);
        return hash.ToHashCode();
    }

    public override string ToString() => string.Join(";", Pieces.Select(piece => $"{piece.SkinDataId}:{piece.FirstDyeDataId},{piece.SecondDyeDataId},{piece.ThirdDyeDataId},{piece.FourthDyeDataId}"));
}
