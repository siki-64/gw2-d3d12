using System;
using System.Collections.Generic;

namespace Gw2.Client.Net.Login;

public sealed record CharacterSummary(uint Datacenter, Guid Id, ulong Flags, string Name, byte[] Appearance)
{
    private const int LocationMapIdOffset = 1;
    private const int RaceOffset = 0x21;
    private const int SexOffset = 0x23;

    public ushort LocationMapId => Appearance.Length >= LocationMapIdOffset + sizeof(ushort) ? BitConverter.ToUInt16(Appearance, LocationMapIdOffset) : (ushort)0;

    public int? Race => Appearance.Length > RaceOffset ? Appearance[RaceOffset] : null;

    public int? Sex => Appearance.Length > SexOffset ? Appearance[SexOffset] : null;
}

public sealed class CharacterList
{
    private readonly List<CharacterSummary> _characters = new List<CharacterSummary>();

    public IReadOnlyList<CharacterSummary> Characters => _characters;

    public void Add(CharacterSummary character) => _characters.Add(character ?? throw new ArgumentNullException(nameof(character)));
}
