using Crystal.Graphics;
using Gw2.Dat.Content;

namespace Gw2.Client.Graphics;

public static class GrSamplerStates
{
    public const int DeviceMaxAnisotropy = 16;
    public const int DefaultMaxAnisotropy = 1;

    public static SamplerDescription From(Gw2AmatSamplerState state) => From(state, DeviceMaxAnisotropy);

    public static SamplerDescription From(Gw2AmatSamplerState state, int maxAnisotropy)
    {
        var anisotropic = state.Minify == Gw2SamplerFilter.Anisotropic || state.Magnify == Gw2SamplerFilter.Anisotropic;
        return new SamplerDescription
        {
            AddressU = AddressOf(state.AddressU),
            AddressV = AddressOf(state.AddressV),
            AddressW = AddressOf(state.AddressW),
            MinFilter = anisotropic ? FilterMode.Linear : FilterOf(state.Minify),
            MagFilter = anisotropic ? FilterMode.Linear : FilterOf(state.Magnify),
            MipFilter = anisotropic ? FilterMode.Linear : FilterOf(state.Mip),
            MaxAnisotropy = anisotropic ? maxAnisotropy : DefaultMaxAnisotropy,
        };
    }

    private static TextureAddressMode AddressOf(Gw2SamplerAddress address) => address switch
    {
        Gw2SamplerAddress.Mirror => TextureAddressMode.Mirror,
        Gw2SamplerAddress.Clamp => TextureAddressMode.Clamp,
        Gw2SamplerAddress.Border => TextureAddressMode.Border,
        _ => TextureAddressMode.Wrap,
    };

    private static FilterMode FilterOf(Gw2SamplerFilter filter) => filter == Gw2SamplerFilter.Point ? FilterMode.Point : FilterMode.Linear;
}
