namespace Gw2.Dat.Identity;

public readonly record struct Gw2FileId(uint Value)
{
    public override string ToString() => $"0x{Value:X8}";
}
