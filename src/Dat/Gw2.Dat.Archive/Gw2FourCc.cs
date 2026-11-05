using System.Linq;
using System.Text;

namespace Gw2.Dat.Archive;

public static class Gw2FourCc
{
    public const string Material = "AMAT";
    public const string Model = "MODL";

    public const string Map = "mapc";
    public const string MapCollision = "hvkC";
    public const string MapMetadata = "mMet";
    public const string MapShadow = "mpsd";

    public const string Composite = "comp";
    public const string CompositeArmorColor = "cmaC";
    public const string ContentHashStore = "CDHS";
    public const string ContentStore = "cntc";

    public const string AnimSequences = "anic";
    public const string EmoteAnimations = "emoc";

    public const string AudioBank = "abnk";
    public const string AudioBankIndex = "abix";
    public const string AudioSample = "ASND";
    public const string AudioScript = "asnd";
    public const string AudioSurface = "AMSP";
    public const string OggStream = "OggS";

    public const string StringTable = "strs";
    public const string TextManifest = "txtm";
    public const string TextVariant = "txtv";
    public const string TextVariantManifest = "txtV";

    public const string PagedImageTable = "PIMG";
    public const string TextureAtlas = "ATEX";

    public const string ArapArchive = "ARAP";
    public const string AssetManifest = "ARMF";
    public const string BitmapFont = "AFNT";
    public const string Cinematic = "CINP";
    public const string Dependencies = "DEPS";
    public const string Eula = "eula";
    public const string Geometry = "GEOM";
    public const string LocalConfig = "locl";
    public const string PortalManifest = "prlt";
    public const string RiffContainer = "RIFF";

    public const string Texture3Dcx = "3DCX";
    public const string TextureAtec = "ATEC";
    public const string TextureAtep = "ATEP";
    public const string TextureAtet = "ATET";
    public const string TextureAteu = "ATEU";
    public const string TextureAttx = "ATTX";
    public const string TextureCtex = "CTEX";

    public const string DdsFile = "DDS ";
    public const string Dxt1 = "DXT1";
    public const string Dxt2 = "DXT2";
    public const string Dxt3 = "DXT3";
    public const string Dxt4 = "DXT4";
    public const string Dxt5 = "DXT5";
    public const string DxtAlpha = "DXTA";
    public const string DxtLuminance = "DXTL";
    public const string DxtNormal = "DXTN";

    public const string Bc5 = "BC5X";
    public const string Bc7 = "BC7X";
    public const string Webp = "WEBP";

    public const string AnimSequenceChunk = "seqn";
    public const string ContentChunk = "Main";
    public const string EmoteChunk = "anim";

    public static class Signature
    {
        public const uint Mz = 0x5A4D;
        public const uint TrueType = 0x00000100;
        public const uint Mp3Frame = 0xFBFF;
        public const uint Jpeg = 0xFFD8FF;
        public const uint Id3 = 0x334449;
        public const uint Bink2 = 0x32424B;
        public const uint Utf8Bom = 0xBFBBEF;
        public const uint R32Float = 0x00000072;
        public const uint Png = 0x474E5089;
    }

    private static ReadOnlySpan<byte> PackMagic => "PF"u8;

    public static bool IsPack(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 12 && prefix[..2].SequenceEqual(PackMagic);

    public static string PackType(ReadOnlySpan<byte> prefix) =>
        IsPack(prefix) ? Decode(prefix[8..12]) : string.Empty;

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4) return string.Empty;

        var ascii = Encoding.ASCII.GetString(bytes[..4]);
        return ascii.All(character => character is >= ' ' and <= '~') ? ascii : string.Empty;
    }
}
