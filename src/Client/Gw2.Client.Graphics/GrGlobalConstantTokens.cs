using Gw2.Formats;

namespace Gw2.Client.Graphics;

public static class GrGlobalConstantTokens
{
    public static readonly uint LightBuffer = Token.Encode("grltbf");
    public static readonly uint CharLightCurve = Token.Encode("chrltcr");
    public static readonly uint BacklightColor = Token.Encode("grblcol");
    public static readonly uint BacklightDirection = Token.Encode("grbldir");
    public static readonly uint BacklightShadowInfluence = Token.Encode("grblinf");
    public static readonly uint EyeColorRed = Token.Encode("hseyea");
    public static readonly uint EyeColorGreen = Token.Encode("hseyeb");
    public static readonly uint EyeColorBlue = Token.Encode("hseyec");
    public static readonly uint WorldToShadowA = Token.Encode("grsmvpa");
    public static readonly uint WorldToShadowB = Token.Encode("grsmvpb");
    public static readonly uint WorldToShadowC = Token.Encode("grsmvpc");
    public static readonly uint WorldToShadowD = Token.Encode("grsmvpd");
}
