namespace Gw2.Dat.Content;

public enum Gw2SamplerAddress
{
    Wrap,
    Mirror,
    Clamp,
    Border,
}

public enum Gw2SamplerFilter
{
    Linear,
    Point,
    Anisotropic,
}

public readonly record struct Gw2AmatSamplerState(uint Flags)
{
    private const int AddressUShift = 0;
    private const int AddressVShift = 2;
    private const int AddressWShift = 4;
    private const int MinifyShift = 6;
    private const int MagnifyShift = 8;
    private const int MipShift = 10;
    private const uint AddressMask = 0x3;
    private const uint FilterMask = 0x3;
    private const uint MipPointFlag = 0x1;

    public Gw2SamplerAddress AddressU => (Gw2SamplerAddress)((Flags >> AddressUShift) & AddressMask);

    public Gw2SamplerAddress AddressV => (Gw2SamplerAddress)((Flags >> AddressVShift) & AddressMask);

    public Gw2SamplerAddress AddressW => (Gw2SamplerAddress)((Flags >> AddressWShift) & AddressMask);

    public Gw2SamplerFilter Minify => FilterOf(Flags >> MinifyShift);

    public Gw2SamplerFilter Magnify => FilterOf(Flags >> MagnifyShift);

    public Gw2AmatSamplerState WithAnisotropicFiltering()
    {
        var flags = Flags;
        if (((flags >> MinifyShift) & FilterMask) == 0) flags |= 2u << MinifyShift;
        if (((flags >> MagnifyShift) & FilterMask) == 0) flags |= 2u << MagnifyShift;
        return new Gw2AmatSamplerState(flags);
    }

    public Gw2SamplerFilter Mip => ((Flags >> MipShift) & MipPointFlag) != 0 ? Gw2SamplerFilter.Point : Gw2SamplerFilter.Linear;

    private static Gw2SamplerFilter FilterOf(uint bits) => (bits & FilterMask) switch
    {
        1 => Gw2SamplerFilter.Point,
        2 => Gw2SamplerFilter.Anisotropic,
        _ => Gw2SamplerFilter.Linear,
    };
}
