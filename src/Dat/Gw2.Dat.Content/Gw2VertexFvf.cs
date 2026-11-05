namespace Gw2.Dat.Content;

public static class Gw2VertexFvf
{
    public const uint Position = 0x00000001;
    public const uint Weights = 0x00000002;
    public const uint Group = 0x00000004;
    public const uint Normal = 0x00000008;
    public const uint Color = 0x00000010;
    public const uint Tangent = 0x00000020;
    public const uint Bitangent = 0x00000040;
    public const uint TangentFrame = 0x00000080;

    public const uint TexCoord32Mask = 0x0000FF00;

    public const uint TexCoord16Mask = 0x00FF0000;

    public const uint Opaque24 = 0x01000000;
    public const uint Opaque25 = 0x02000000;
    public const uint Opaque26 = 0x04000000;
    public const uint Opaque27 = 0x08000000;
    public const uint PositionCompressed = 0x10000000;
    public const uint Opaque29 = 0x20000000;

    public const uint ReservedMask = 0xC0000000;

    public const uint OrientationMask = Normal | Tangent | Bitangent | TangentFrame;

    public const int PositionSize = 12;
    public const int VectorSize = 12;
    public const int WeightsSize = 4;
    public const int GroupSize = 4;
    public const int ColorSize = 4;
    public const int TexCoord32Size = 8;
    public const int TexCoord16Size = 4;
    public const int PositionCompressedSize = 6;
    public const int Opaque24Size = 48;
    public const int Opaque25Size = 4;
    public const int Opaque26Size = 4;
    public const int Opaque27Size = 16;
    public const int Opaque29Size = 12;

    public static string? SemanticFor(uint flag) => flag switch
    {
        Position or PositionCompressed => "POSITION",
        Weights => "BLENDWEIGHT",
        Group => "BLENDINDICES",
        Normal or TangentFrame => "NORMAL",
        Color => "COLOR",
        Tangent => "TANGENT",
        Bitangent => "BITANGENT",
        _ => null,
    };
}
