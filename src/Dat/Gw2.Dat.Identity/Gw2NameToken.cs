namespace Gw2.Dat.Identity;

public readonly record struct Gw2NameToken(uint Value)
{
    public static Gw2NameToken FromName(string name) => new(Gw2NameHash.Compute(name));

    public bool TrySuggestName(out string candidate) => Gw2NameHash.TryDecode(Value, out candidate);

    public override string ToString() => $"0x{Value:X8}";
}
