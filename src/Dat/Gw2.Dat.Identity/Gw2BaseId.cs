namespace Gw2.Dat.Identity;

public readonly record struct Gw2BaseId(uint Value)
{
    public override string ToString() => $"0x{Value:X8}";
}
