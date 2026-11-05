namespace Gw2.Dat.Content;

public enum Gw2ShaderQuality
{
    Invalid = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Ultra = 4,
}

public static class Gw2TechniqueQuality
{
    private const uint Offset = 0xD0000000u;
    private const int Radix = 0x17;
    private const string Alphabet = "abcdefghiklmnopvrstuwxy";

    public static string DecodeName(uint token)
    {
        var value = token + Offset;
        if (value == 0) return string.Empty;

        Span<char> buffer = stackalloc char[8];
        var length = 0;
        while (value != 0 && length < buffer.Length - 1)
        {
            buffer[length++] = Alphabet[(int)(value % Radix)];
            value /= Radix;
        }

        return new string(buffer[..length]);
    }

    public static Gw2ShaderQuality SelectionQualityOf(uint token) => DecodeName(token) is { Length: > 0 } name
        ? name[0] switch
        {
            'u' => Gw2ShaderQuality.Ultra,
            'h' => Gw2ShaderQuality.High,
            'm' => Gw2ShaderQuality.Medium,
            'l' => Gw2ShaderQuality.Low,
            _ => Gw2ShaderQuality.Invalid,
        }
        : Gw2ShaderQuality.Invalid;

    public static Gw2ShaderQuality QualityOf(uint token) => DecodeName(token) switch
    {
        "low" => Gw2ShaderQuality.Low,
        "medium" => Gw2ShaderQuality.Medium,
        "high" => Gw2ShaderQuality.High,
        "ultra" => Gw2ShaderQuality.Ultra,
        _ => Gw2ShaderQuality.Invalid,
    };
}
