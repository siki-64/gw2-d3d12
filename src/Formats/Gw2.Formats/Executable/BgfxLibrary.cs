namespace Gw2.Formats;

public enum BgfxEntryReplacement
{
    Return,
    ReturnZero,
    ReturnOne,
    Init,
    EncoderBegin,
    Frame,
    Stats,
    UniformInfo,
    CreateShader,
    CreateProgram,
    CreateUniform,
    ShaderUniforms,
    TransientIndexSpace,
    TransientVertexSpace,
    TransientIndexBuffer,
    TransientVertexBuffer,
    InstanceDataBuffer,
    NewHandle,
    NewHandleReleasingFirst,
    NewHandleReleasingSixth,
    NewHandleReleasingSeventh,
    ReleaseThird,
    ReleaseNinth,
    CreateTexture2D,
    UpdateTexture2D,
    DestroyTexture,
}

public readonly record struct BgfxEntry(uint Rva, BgfxEntryReplacement Replacement);

public sealed class BgfxLibrary
{
    private static BgfxLibrary? _current;

    private BgfxLibrary(ExecutableImage executable)
    {
        CapsRva = Rva(executable, CodeAnchor.Reference("48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 83 3D ?? ?? ?? ?? 00 74 ?? 8B 05 ?? ?? ?? ?? 85 C0", 14, 19));
        AllocatorRva = Rva(executable, CodeAnchor.Reference("83 E7 F0 E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 45 33 C9 44 8B C7", 11, 15));
        CallbackRva = Rva(executable, CodeAnchor.Reference("89 8C 96 44 B1 EF 04 48 8B 0D ?? ?? ?? ?? 48 8B 01", 10, 14));
        ReleaseRva = Rva(executable, CodeAnchor.Reference("48 8B 8C 24 B0 00 00 00 48 83 C4 68 E9 ?? ?? ?? ??", 13, 17));
        GetRendererTypeRva = Rva(executable, CodeAnchor.Reference("E8 ?? ?? ?? ?? 83 F8 02 74 ?? E8 ?? ?? ?? ?? 83 F8 03 74 ?? 4C 8D 05 ?? ?? ?? ?? BA 87 01 00 00", 11, 15));
        Entries =
        [
            new(Rva(executable, CodeAnchor.Reference("66 44 23 C0 E8 ?? ?? ?? ?? 48 8B 8D 78 01 00 00", 5, 9)), BgfxEntryReplacement.InstanceDataBuffer),
            new(Rva(executable, CodeAnchor.Reference("48 8D 8C 24 C0 00 00 00 8B D3 E8 ?? ?? ?? ?? 48 8B 57 28 4C 8D 04 B6", 11, 15)), BgfxEntryReplacement.TransientIndexBuffer),
            new(Rva(executable, CodeAnchor.Reference("8B D6 E8 ?? ?? ?? ?? 8B 84 24 B0 00 00 00 33 D2", 3, 7)), BgfxEntryReplacement.TransientVertexBuffer),
            new(Rva(executable, CodeAnchor.Reference("33 C9 E8 ?? ?? ?? ?? 48 89 83 78 EB 14 00 48 85 C0", 3, 7)), BgfxEntryReplacement.EncoderBegin),
            new(Rva(executable, CodeAnchor.Reference("66 89 5C 24 20 E8 ?? ?? ?? ?? 48 8D 54 24 60 49 8B CE", 6, 10)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8B 44 24 78 0F B7 08 E8 ?? ?? ?? ?? 0F B7 4E 1E 66 FF C7", 9, 13)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 4E 1E 66 83 C1 64 E8 ?? ?? ?? ?? 84 C0 74 ?? 0F B7 4E 1E", 9, 13)), BgfxEntryReplacement.ReturnOne),
            new(Rva(executable, CodeAnchor.Reference("BA 00 08 00 00 48 8B C8 E8 ?? ?? ?? ?? 66 89 03 89 7B 04", 9, 13)), BgfxEntryReplacement.NewHandleReleasingFirst),
            new(Rva(executable, CodeAnchor.Reference("48 8B C8 E8 ?? ?? ?? ?? 66 89 43 08 48 8B 5C 24 40", 4, 8)), BgfxEntryReplacement.NewHandleReleasingFirst),
            new(Rva(executable, CodeAnchor.Reference("48 8D 0D ?? ?? ?? ?? 0F B7 D8 E8 ?? ?? ?? ?? 41 B0 01 0F B7 D3", 11, 15)), BgfxEntryReplacement.NewHandle),
            new(Rva(executable, CodeAnchor.Reference("48 8D 54 24 50 41 0F B6 CE E8 ?? ?? ?? ?? 66 89 06 85 FF", 10, 14)), BgfxEntryReplacement.NewHandle),
            new(Rva(executable, CodeAnchor.Reference("C7 44 24 20 60 00 00 00 48 8B CF E8 ?? ?? ?? ?? 66 89 03 48 8B C3", 12, 16)), BgfxEntryReplacement.NewHandle),
            new(Rva(executable, CodeAnchor.Reference("48 8B CE E8 ?? ?? ?? ?? 48 8B 74 24 38 66 89 03", 4, 8)), BgfxEntryReplacement.NewHandleReleasingFirst),
            new(Rva(executable, CodeAnchor.Reference("48 83 EC 28 E8 ?? ?? ?? ?? 0F B7 C8 41 B8 FF FF 00 00", 5, 9)), BgfxEntryReplacement.NewHandle),
            new(Rva(executable, CodeAnchor.Reference("41 0F B7 4E 08 E8 ?? ?? ?? ?? 4C 8B 46 10 4C 8D 4E 40", 6, 10)), BgfxEntryReplacement.CreateProgram),
            new(Rva(executable, CodeAnchor.Reference("0F B7 4E 08 E8 ?? ?? ?? ?? 48 8B D5 66 89 47 10", 5, 9)), BgfxEntryReplacement.CreateProgram),
            new(Rva(executable, CodeAnchor.Reference("48 8B C8 E8 ?? ?? ?? ?? 48 8B 7C 24 60 0F B7 D8", 4, 8)), BgfxEntryReplacement.CreateShader),
            new(Rva(executable, CodeAnchor.Reference("48 89 74 24 28 89 44 24 20 E8 ?? ?? ?? ?? 48 8B CB 66 89 03", 10, 14)), BgfxEntryReplacement.CreateTexture2D),
            new(Rva(executable, CodeAnchor.Reference("48 89 74 24 28 89 44 24 20 E8 ?? ?? ?? ?? EB ?? 85 ED", 10, 14)), BgfxEntryReplacement.NewHandleReleasingSeventh),
            new(Rva(executable, CodeAnchor.Reference("48 89 74 24 20 E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 66 83 7B 20 01", 6, 10)), BgfxEntryReplacement.NewHandleReleasingSixth),
            new(Rva(executable, CodeAnchor.Reference("41 B8 01 00 00 00 E8 ?? ?? ?? ?? 41 0F B6 0F 48 8D 15 ?? ?? ?? ??", 7, 11)), BgfxEntryReplacement.CreateUniform),
            new(Rva(executable, CodeAnchor.Reference("48 8B C8 E8 ?? ?? ?? ?? 66 89 43 0C 83 7D B7 00", 4, 8)), BgfxEntryReplacement.NewHandleReleasingFirst),
            new(Rva(executable, CodeAnchor.Reference("66 44 3B 2B 74 ?? 0F B7 0B E8 ?? ?? ?? ?? BA 14 00 00 00 48 8B CB E8 ?? ?? ?? ??", 10, 14)), BgfxEntryReplacement.Return),
            new(Rva(executable, CodeAnchor.Reference("0F B7 4B 08 E8 ?? ?? ?? ?? 66 89 6B 08 8B 56 14", 5, 9)), BgfxEntryReplacement.Return),
            new(Rva(executable, CodeAnchor.Reference("66 44 3B 2F 74 ?? 0F B7 0F E8 ?? ?? ?? ?? BA 38 00 00 00 48 8B CF E8 ?? ?? ?? ?? 48 2B 9D 40 82 14 00", 10, 14)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("66 3B 03 74 ?? 0F B7 0B E8 ?? ?? ?? ?? 33 D2 48 8B CE E8 ?? ?? ?? ??", 9, 13)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("8B FA 8B 49 14 E8 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 03", 6, 10).ThenReference(10, 14)), BgfxEntryReplacement.Return),
            new(Rva(executable, CodeAnchor.Reference("E8 ?? ?? ?? ?? 0F B7 4F 04 E8 ?? ?? ?? ?? 48 8D 4C 24 40 E8 ?? ?? ?? ??", 10, 14)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 4C 8E 08 E8 ?? ?? ?? ?? B8 FF FF 00 00 48 C7 44 24 30 00 00 00 00", 6, 10)), BgfxEntryReplacement.Return),
            new(Rva(executable, CodeAnchor.Reference("0F B7 CB E8 ?? ?? ?? ?? 41 8B C7 4C 8B A4 24 A0 00 00 00", 4, 8)), BgfxEntryReplacement.DestroyTexture),
            new(Rva(executable, CodeAnchor.Reference("0F B7 4B 5A E8 ?? ?? ?? ?? BA 70 00 00 00 48 8B CB", 5, 9)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("66 44 3B 77 0C 74 ?? 0F B7 4F 0C E8 ?? ?? ?? ?? BA 10 00 00 00 48 8B CF E8 ?? ?? ?? ??", 12, 16)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("89 74 24 20 E8 ?? ?? ?? ?? 0F 28 74 24 50 33 C0", 5, 9)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8D 58 10 48 85 DB 74 ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 0B E8 ?? ?? ?? ?? 48 8D 4B F0 45 33 C0 33 D2 E8 ?? ?? ?? ?? EB ?? 48 8B CF", 25, 29)), BgfxEntryReplacement.Return),
            new(Rva(executable, CodeAnchor.Reference("0F 95 C1 E8 ?? ?? ?? ?? 33 C9 89 87 98 D2 14 00", 4, 8)), BgfxEntryReplacement.Frame),
            new(Rva(executable, CodeAnchor.Reference("01 45 5C 8B CB E8 ?? ?? ?? ?? 3B C3 0F 85 ?? ?? ?? ?? 45 33 C0", 6, 10)), BgfxEntryReplacement.TransientIndexSpace),
            new(Rva(executable, CodeAnchor.Reference("8B CE 89 74 24 60 E8 ?? ?? ?? ?? 3B C6 0F 85 ?? ?? ?? ?? 4C 8B C5", 7, 11)), BgfxEntryReplacement.TransientVertexSpace),
            new(Rva(executable, CodeAnchor.Reference("48 8D 54 24 38 44 89 7C 24 38 E8 ?? ?? ?? ?? 85 C0 75 ??", 11, 15)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8D 54 24 44 E8 ?? ?? ?? ?? 0F B7 D8 66 89 5C 24 40", 6, 10)), BgfxEntryReplacement.ShaderUniforms),
            new(Rva(executable, CodeAnchor.Reference("48 83 EC 20 E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? BA 30 00 00 00", 5, 9)), BgfxEntryReplacement.Stats),
            new(Rva(executable, CodeAnchor.Reference("48 8D 55 C0 0F B7 CF E8 ?? ?? ?? ?? 83 BD C0 00 00 00 00 75 ?? 41 0F B6 07", 8, 12)), BgfxEntryReplacement.UniformInfo),
            new(Rva(executable, CodeAnchor.Reference("48 8D 8B A0 D0 14 00 C6 83 B1 D0 14 00 01 E8 ?? ?? ?? ?? 84 C0 75 ??", 15, 19)), BgfxEntryReplacement.Init),
            new(Rva(executable, CodeAnchor.Reference("41 0F B7 0F E8 ?? ?? ?? ?? 44 3B E0 4D 8D 76 08", 5, 9)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("FF 83 A8 D2 14 00 E8 ?? ?? ?? ?? 48 8B 44 24 68", 7, 11)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 89 74 24 78 E8 ?? ?? ?? ?? 83 BB A0 D0 14 00 0A", 6, 10)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8B CE E8 ?? ?? ?? ?? 48 8B 94 24 B0 00 00 00", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("89 44 24 20 E8 ?? ?? ?? ?? 49 83 C6 10 4C 3B F5", 5, 9)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8D 14 C8 48 8B CE E8 ?? ?? ?? ?? 48 8B 44 24 78 44 8B 77 38", 8, 12)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 13 48 8B CE E8 ?? ?? ?? ?? 48 8B 44 24 78 44 8B 77 38", 7, 11)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("49 8B CF E8 ?? ?? ?? ?? 45 33 C0 48 BA 07 00 00 00 00 00 01 00", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("41 0F B7 16 48 8B CE E8 ?? ?? ?? ?? 8B 47 38 EB ??", 8, 12)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("8B 54 24 54 E8 ?? ?? ?? ?? 0F B7 4C 24 52 41 B1 FF", 5, 9)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 8F AC B2 14 00 E8 ?? ?? ?? ?? 33 D2 48 B9 07 00 00 00 00 00 01 00", 8, 12)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8B CE E8 ?? ?? ?? ?? 41 BD FF FF 00 00 48 8B 47 20", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("41 B8 FF FF FF 7F 0F B7 09 E8 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ??", 10, 14)), BgfxEntryReplacement.Return),
            new(Rva(executable, CodeAnchor.Reference("0F B7 D5 E8 ?? ?? ?? ?? 48 8B 5C 24 40 48 8B 6C 24 48", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("66 44 2B CA E8 ?? ?? ?? ?? 0F 10 36 66 89 44 24 50", 5, 9)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 4C 24 50 E8 ?? ?? ?? ?? 4C 8B B4 24 80 00 00 00", 6, 10)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("45 33 C0 48 8B CE E8 ?? ?? ?? ?? 66 44 3B 6F 18 74 ??", 7, 11)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("33 D2 E8 ?? ?? ?? ?? 0F B7 55 52 41 B9 FF FF FF FF", 3, 7)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("89 44 24 20 E8 ?? ?? ?? ?? 41 0B DF 44 8B 8C 24 98 00 00 00", 5, 9)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("33 C9 E8 ?? ?? ?? ?? 44 8B 4C 24 60 48 8D 94 24 A8 00 00 00", 3, 7)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8B CE E8 ?? ?? ?? ?? 44 0F 28 BC 24 50 06 00 00", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("41 B9 40 00 00 00 0F B7 D3 E8 ?? ?? ?? ?? 4C 8B 74 24 58 E9 ?? ?? ?? ??", 10, 14)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("F3 0F 11 84 24 D8 00 00 00 E8 ?? ?? ?? ?? EB ?? 48 8B 6C 24 68", 10, 14)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("4C 8D 04 C8 48 8B CE E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 44 0F B7 40 08", 8, 12)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8B CE 44 89 74 24 20 E8 ?? ?? ?? ?? EB ?? 4C 8B 77 30", 9, 13)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("49 8B CF E8 ?? ?? ?? ?? 0F B7 93 AC B2 14 00 49 8B CF", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8B CE E8 ?? ?? ?? ?? F3 41 0F 10 4F 20 0F 57 C0", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("33 C9 E8 ?? ?? ?? ?? 44 8B 46 28 48 8D 8C 24 C0 00 00 00", 3, 7)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 97 AA B2 14 00 33 C9 E8 ?? ?? ?? ?? 0F B7 8F AC B2 14 00 E8 ?? ?? ?? ??", 10, 14)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 8B F8 0F B7 08 E8 ?? ?? ?? ?? 48 8B D3 48 8B CF", 7, 11)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("48 89 5F 08 0F B7 13 E8 ?? ?? ?? ?? 33 C0 48 8D 54 24 20", 8, 12)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 CB E8 ?? ?? ?? ?? 4C 8D 84 24 F0 00 00 00", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("41 0F B7 D7 E8 ?? ?? ?? ?? 48 8B 5C 24 50 48 8B 6C 24 58", 5, 9)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("33 D2 0F B7 CB E8 ?? ?? ?? ?? 49 63 46 48 45 8B EF", 6, 10)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 8F 20 D1 14 00 48 8B 5C 24 30", 6, 10)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("C7 46 24 01 00 00 00 E8 ?? ?? ?? ?? 48 8B 5C 24 40", 8, 12)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 D3 E8 ?? ?? ?? ?? 48 8B 84 24 88 00 00 00", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("0F B7 09 E8 ?? ?? ?? ?? 48 8B 83 20 B3 14 00 48 8B 5C 24 48", 4, 8)), BgfxEntryReplacement.ReturnZero),
            new(Rva(executable, CodeAnchor.Reference("4C 8B C0 33 D2 E8 ?? ?? ?? ?? 89 7B 04 48 8B 5C 24 48", 6, 10)), BgfxEntryReplacement.ReleaseThird),
            new(Rva(executable, CodeAnchor.Reference("48 83 C4 30 5F E9 ?? ?? ?? ?? 48 8B 13 41 B8 00 08 00 00", 6, 10)), BgfxEntryReplacement.ReleaseThird),
            new(Rva(executable, CodeAnchor.Reference("66 89 6C 24 20 E8 ?? ?? ?? ?? 48 83 C3 10 48 83 6C 24 58 01", 6, 10)), BgfxEntryReplacement.UpdateTexture2D),
            new(Rva(executable, CodeAnchor.Reference("66 89 6C 24 20 E8 ?? ?? ?? ?? 48 83 C3 18 48 83 6C 24 58 01", 6, 10)), BgfxEntryReplacement.ReleaseNinth),
            new(Rva(executable, CodeAnchor.Reference("66 89 7C 24 20 E8 ?? ?? ?? ?? 48 8B 74 24 58 48 83 C3 10", 6, 10)), BgfxEntryReplacement.ReleaseNinth),
        ];
    }

    public static BgfxLibrary Current => _current ??= new BgfxLibrary(ExecutableImage.Current);

    public uint CapsRva { get; }

    public uint AllocatorRva { get; }

    public uint CallbackRva { get; }

    public uint ReleaseRva { get; }

    public uint GetRendererTypeRva { get; }

    public BgfxEntry[] Entries { get; }

    public static BgfxLibrary FromExecutable(string path) => new(ExecutableImage.Open(path));

    private static uint Rva(ExecutableImage executable, CodeAnchor anchor) => (uint)(anchor.Locate(executable) - executable.ImageBase);
}
