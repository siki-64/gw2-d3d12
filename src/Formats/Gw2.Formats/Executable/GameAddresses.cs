using System.Buffers.Binary;

namespace Gw2.Formats;

public readonly record struct UiMaterialPack(int Shader, uint Rva, int Size);

public sealed class GameAddresses
{
    private static GameAddresses? _current;

    private GameAddresses(ExecutableImage executable)
    {
        Build = ExecutableIdentity.Build(executable);
        GameConnectionMessageConnectionRva = Rva(executable, CodeAnchor.Reference("C7 05 ?? ?? ?? ?? 00 00 00 00 E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 33 C9", 18, 22));
        RenderFrameCallRva = Rva(executable, CodeAnchor.Site("E8 ?? ?? ?? ?? 48 8D 4E 40 E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 0F 2E D5 7A ??", 9));
        RenderFrameRva = Rva(executable, CodeAnchor.Reference("E8 ?? ?? ?? ?? 48 8D 4E 40 E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 0F 2E D5 7A ??", 10, 14));
        TlsIndexRva = (uint)(executable.TlsIndexAddress - executable.ImageBase);
        ChCliContextVirtualTableRva = Rva(executable, CodeAnchor.Reference("57 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B D9 48 89 01 48 8D 51 30", 8, 12));
        CoCharVirtualTableRva = Rva(executable, CodeAnchor.Reference("48 8D 05 ?? ?? ?? ?? F3 44 0F 10 BD C0 01 00 00", 3, 7));
        CoCharSimpleCliVirtualTableRva = Rva(executable, CodeAnchor.Reference("48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18 57 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B D9 48 89 01 33 ED 48 8D 05 ?? ?? ?? ?? 8B F2 48 89 41 10 48 8D 05 ?? ?? ?? ?? 48 89 41 18 48 8B 89 60 01 00 00", 23, 27));
        CoKeyFramedVirtualTableRva = Rva(executable, CodeAnchor.Reference("40 53 48 83 EC 20 48 89 6C 24 30 48 8D 05 ?? ?? ?? ?? 48 89 01 8B EA 48 8D 05 ?? ?? ?? ?? 48 89 74 24 38 48 89 41 10", 14, 18));
        ViewSingletonRva = Rva(executable, CodeAnchor.Reference("E8 ?? ?? ?? ?? 48 83 3D ?? ?? ?? ?? 00 75 ?? 41 B8 84 01 00 00", 8, 13));
        WorldViewContextRva = Rva(executable, CodeAnchor.Reference("FF 90 90 02 00 00 48 C7 05 ?? ?? ?? ?? 00 00 00 00 48 83 C4 28", 9, 17));
        WorldViewContextVirtualTableRva = Rva(executable, CodeAnchor.Reference("0F 84 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 5C 24 58 48 8D 4F 08", 9, 13));
        FrameIdManagerRva = Rva(executable, CodeAnchor.Reference("74 ?? 8B D1 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 83 F8 FF 74 ??", 7, 11));
        CharacterArrayRva = Rva(executable, CodeAnchor.Reference("48 8B 0D ?? ?? ?? ?? 33 FF 48 89 3D ?? ?? ?? ?? 48 89 3D ?? ?? ?? ?? 48 85 C9", 12, 16));
        TextStyleArrayRva = Rva(executable, CodeAnchor.Reference("E8 ?? ?? ?? ?? 8B 15 ?? ?? ?? ?? 4C 8B 05 ?? ?? ?? ?? 89 5C 24 48 3B DA", 14, 18));
        FontContextRva = Rva(executable, CodeAnchor.Reference("E8 ?? ?? ?? ?? F3 0F 11 05 ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F 28 CE 48 8B 08", 14, 18).ThenReference(3, 7));
        SetFrameRateRva = Rva(executable, CodeAnchor.Reference("41 0F 28 73 F0 45 0F 28 43 D0 49 8B E3 5F E9 ?? ?? ?? ??", 15, 19));
        FrameBudgetMicrosecondsRva = Rva(executable, CodeAnchor.Reference("E8 ?? ?? ?? ?? 48 8B C8 E8 ?? ?? ?? ?? 44 8B 05 ?? ?? ?? ?? 48 8B D0 48 2B D3 49 3B D0 72 ??", 16, 20));
        PlayCommandsRva = Rva(executable, CodeAnchor.Reference("48 8B D7 8B CE E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 33 D2 48 8D 8B 70 D2 14 00", 6, 10));
        FrameRootRelationRva = Rva(executable, CodeAnchor.Reference("C3 48 8B 05 ?? ?? ?? ?? 48 83 C0 A0 48 83 C4 28", 4, 8));
        FrameWindowListRva = Rva(executable, CodeAnchor.Reference("75 ?? 48 89 5C 24 30 48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 48 89 7C 24 38", 10, 14));
        FrameWindowLinkOffsetRva = Rva(executable, CodeAnchor.Reference("48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 33 C9 48 8D 0D ?? ?? ?? ?? 41 B8 02 00 00 00 48 8B D7 E8 ?? ?? ?? ?? E8 ?? ?? ?? ??", 18, 22));
        FrameTopRva = Rva(executable, CodeAnchor.Reference("F3 0F 11 94 24 98 00 00 00 E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 48 8D 54 24 20 E8 ?? ?? ?? ??", 17, 21));
        FrameScreenSizeRva = Rva(executable, CodeAnchor.Reference("F3 0F 59 CA F3 0F 10 15 ?? ?? ?? ?? F3 0F 59 10", 8, 12));
        FrameUiScaleRva = Rva(executable, CodeAnchor.Reference("F3 0F 11 15 ?? ?? ?? ?? F3 0F 11 05 ?? ?? ?? ?? F3 0F 10 05 ?? ?? ?? ??", 12, 16));
        FrameDrawWalkFilterRva = Rva(executable, CodeAnchor.Reference("EB ?? FF C8 89 05 ?? ?? ?? ?? F7 D0 C1 E8 1F 3B D0", 6, 10));
        TextureFileCacheRva = Rva(executable, CodeAnchor.Reference("89 7B 7C 48 8B 0D ?? ?? ?? ?? 48 83 C1 48 48 63 11", 6, 10));
        EffectContextRva = Rva(executable, CodeAnchor.Reference("41 89 46 28 41 8B 07 41 89 46 2C E8 ?? ?? ?? ?? 4C 8B F8 48 8B 08", 12, 16).ThenReference(3, 7));
        MaterialCacheRva = Rva(executable, CodeAnchor.Reference("48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 74 ?? 41 B8 19 05 00 00", 10, 14));
        CompassControlProcedureRva = Rva(executable, CodeAnchor.Reference("75 ?? 8B 4F 08 4C 8D 0D ?? ?? ?? ?? BA 30 00 00 00 4C 89 74 24 20", 8, 12));
        CompassMarkerControlProcedureRva = Rva(executable, CodeAnchor.Reference("83 0B 01 48 8D 05 ?? ?? ?? ?? 48 8B 5C 24 30 48 8B 74 24 38", 6, 10));
        CompassWaypointControlProcedureRva = Rva(executable, CodeAnchor.Reference("C7 03 11 00 01 04 C7 00 01 01 00 00 48 8D 05 ?? ?? ?? ?? 48 8B 5C 24 30 48 8B 74 24 38", 15, 19));
        CompassAgentModelVirtualTableRva = Rva(executable, CodeAnchor.Reference("48 89 01 48 8B D9 48 8D 05 ?? ?? ?? ?? 48 89 41 08 48 8D 05 ?? ?? ?? ?? 48 89 41 70", 9, 13));
        CloudVirtualTableRva = Rva(executable, CodeAnchor.Reference("48 8B 99 F8 00 00 00 48 8D 05 ?? ?? ?? ?? 48 89 01 8B EA", 10, 14));
        WorldMapControlProcedureRva = Rva(executable, CodeAnchor.Reference("48 89 44 24 20 4C 8D 0D ?? ?? ?? ?? 89 5D 28 45 33 C0", 8, 12));
        WorldMapLabelModelInterfaceVirtualTableRva = Rva(executable, CodeAnchor.Reference("48 8D 05 ?? ?? ?? ?? 48 89 03 48 8D 05 ?? ?? ?? ?? 48 89 43 08 48 8D 05 ?? ?? ?? ?? 48 89 83 88 00 00 00", 13, 17));
        LevelConfidenceTableRva = Rva(executable, CodeAnchor.Reference("B8 0A 00 00 00 48 8D 0D ?? ?? ?? ?? 3B D0 0F 4F D0", 8, 12).Shifted(-0xC));
        WorldMapLabelFontKeyRva = Rva(executable, CodeAnchor.Reference("75 ?? 48 8B 1D ?? ?? ?? ?? 45 8B B7 9C 00 00 00 E8 ?? ?? ?? ??", 5, 9));
        WorldMapEmphasisedLabelFontKeyRva = Rva(executable, CodeAnchor.Reference("74 ?? F6 80 98 00 00 00 80 48 8B 1D ?? ?? ?? ?? 75 ?? 48 8B 1D ?? ?? ?? ?? 45 8B B7 9C 00 00 00", 12, 16));
        TextControlProcedureRva = Rva(executable, CodeAnchor.Reference("48 89 5C 24 30 8B 49 18 48 8D 44 24 30 4C 8D 0D ?? ?? ?? ?? 48 89 44 24 20 BA 10 02 00 00 48 89 5C 24 38 41 B8 02 00 00 00", 16, 20));
        TextControlThunkRva = Rva(executable, CodeAnchor.Reference("8B 4F 08 4C 8D 0D ?? ?? ?? ?? 33 D2 48 89 44 24 20", 6, 10));
        DefaultTextStyleRva = Rva(executable, CodeAnchor.Reference("44 0F 29 AC 24 D0 00 00 00 0F 44 0D ?? ?? ?? ?? 48 85 FF", 12, 16));
        TextBuildRva = Rva(executable, CodeAnchor.Reference("89 44 24 20 E8 ?? ?? ?? ?? 48 8B 5C 24 68 F3 0F 10 0B", 5, 9));
        RequestedFrameRateRva = Rva(executable, CodeAnchor.Reference("89 0D ?? ?? ?? ?? 85 C9 75 ?? 89 0D ?? ?? ?? ?? C3 33 D2 B8 40 42 0F 00", 2, 6));
        UnlimitedFrameRateReadRva = Rva(executable, CodeAnchor.Site("89 44 24 58 E8 ?? ?? ?? ?? 8B C8 0F 28 7C 24 70", 4));
        ModelGeometryAbsentBranchRva = Rva(executable, CodeAnchor.Site("89 8E A0 02 00 00 83 78 70 05 75 0C C7 86 A0 02 00 00 00 00 00 00 33 FF", 10));
        ModelPaletteRenderModelBranchRva = Rva(executable, CodeAnchor.Site("48 8B 8F 00 02 00 00 48 8D 9F 90 00 00 00 48 85 C9 0F 84 ?? ?? ?? ?? 48 B8 00 00 00 00 00 00 00 01", 17));
        ModelCreateRva = Rva(executable, CodeAnchor.Site("48 89 5C 24 08 48 89 74 24 18 48 89 7C 24 20 41 54 41 56 41 57 48 83 EC 60 48 8B B4 24 B8 00 00 00 33 FF", 0));
        ModelParticleCloudUpdateRva = Rva(executable, CodeAnchor.Site("48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 60 0F 29 74 24 50 0F 28 F2 49 8B F9 48 8B DA 48 8B F1 48 85 D2 75 19 41 B8 B4 00 00 00", 0));
        AgentShadowUpdateRva = Rva(executable, CodeAnchor.Site("48 8B C4 48 89 70 18 48 89 78 20 55 41 54 41 55 41 56 41 57 48 8D A8 E8 FD FF FF 48 81 EC F0 02 00 00", 0));
        GameRenderPostProcessCallsRva = Rva(executable, CodeAnchor.Site("B9 00 04 00 00 E8 ?? ?? ?? ?? 0F 28 C6 E8 ?? ?? ?? ?? 66 0F 6F 05 ?? ?? ?? ?? 48 8D 4C 24 78 0F 11 44 24 78 E8 ?? ?? ?? ?? B9 01 00 00 00 E8 ?? ?? ?? ?? 8B D3 89 9C 24 D8 00 00 00 B9 01 00 00 00 E8 ?? ?? ?? ?? 48 8B 06 4C 8D 84 24 D8 00 00 00 48 8D 54 24 40 48 8B CE FF 50 10 E8 ?? ?? ?? ?? F3 0F 10 94 24 D8 00 00 00 F3 0F 10 4C 24 40 48 8B 08 48 8B 91 80 00 00 00 48 8B C8 FF D2 E8 ?? ?? ?? ?? 33 D2 48 8B 08 4C 8B 41 40 48 8B C8 41 FF D0 85 FF 74 07 E8 ?? ?? ?? ?? EB 19 B9 01 00 00 00 E8 ?? ?? ?? ?? 45 85 F6 74 0A B9 00 04 00 00 E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? B9 18 00 00 00 E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? BA 01 00 00 00 48 8B 08 4C 8B 41 40 48 8B C8 41 FF D0 33 C9 E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ??", 0));
        WorldAgentModelCreateReturnRvas =
        [
            Rva(executable, CodeAnchor.Site("48 8D 4D 10 48 89 7C 24 30 48 89 7C 24 28 89 7C 24 20 E8 ?? ?? ?? ??", 23)),
            Rva(executable, CodeAnchor.Site("BA 00 00 10 00 4C 89 6C 24 28 44 89 6C 24 20 E8 ?? ?? ?? ??", 20)),
            Rva(executable, CodeAnchor.Site("BA 08 00 00 00 4C 89 64 24 28 44 89 64 24 20 E8 ?? ?? ?? ??", 20)),
            Rva(executable, CodeAnchor.Site("48 89 4C 24 28 89 4C 24 20 49 8B CD E8 ?? ?? ?? ??", 17)),
            Rva(executable, CodeAnchor.Site("44 88 74 24 53 8B 44 24 50 89 44 24 50 48 8D 44 24 50 48 89 44 24 38 48 89 5C 24 30 48 89 5C 24 28 89 5C 24 20 E8 ?? ?? ?? ??", 42)),
            Rva(executable, CodeAnchor.Site("48 89 44 24 38 4C 89 6C 24 30 4C 89 6C 24 28 44 89 6C 24 20 E8 ?? ?? ?? ??", 25)),
            Rva(executable, CodeAnchor.Site("48 8D 84 24 A0 00 00 00 48 89 44 24 38 33 C0 48 89 44 24 30 48 89 44 24 28 89 44 24 20 E8 ?? ?? ?? ??", 34)),
            Rva(executable, CodeAnchor.Site("48 8D 84 24 90 00 00 00 48 89 44 24 38 33 C0 48 89 44 24 30 48 89 44 24 28 89 44 24 20 E8 ?? ?? ?? ??", 34)),
            Rva(executable, CodeAnchor.Site("48 8D 84 24 A0 00 00 00 48 89 44 24 38 4C 89 64 24 30 4C 89 64 24 28 44 89 64 24 20 E8 ?? ?? ?? ??", 33)),
            Rva(executable, CodeAnchor.Site("8B D5 4C 89 64 24 28 44 89 64 24 20 E8 ?? ?? ?? ??", 17)),
            Rva(executable, CodeAnchor.Site("48 8D 84 24 90 00 00 00 48 89 44 24 38 48 89 5C 24 30 48 89 5C 24 28 89 5C 24 20 E8 ?? ?? ?? ??", 32))
        ];
        ModelPlaceholderFlagClearRva = Rva(executable, CodeAnchor.Site("48 B9 FF FF FF FF FF FF FF FE 48 23 C1 48 89 86 90 00 00 00 85 FF 75", 0));
        GraphicsSettingsRva = Rva(executable, CodeAnchor.Reference("48 83 EC 28 48 8B 0D ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 48 85 C9 74 ?? 83 79 08 00", 21, 25));
        PostProcessEnableMaskRva = Rva(executable, CodeAnchor.Reference("B9 07 00 00 00 E8 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? F6 C1 10 74 ??", 12, 16));
        AgentViewContextRva = Rva(executable, CodeAnchor.Reference("F6 05 ?? ?? ?? ?? 01 48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ??", 17, 21));
        CharacterViewVirtualTableRva = Rva(executable, CodeAnchor.Reference("48 89 5C 24 08 57 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B D9 48 89 01 8B FA 48 8D 05 ?? ?? ?? ?? 48 89 41 08 48 8D 05 ?? ?? ?? ?? 48 89 41 10 48 8D 05 ?? ?? ?? ?? 48 89 41 18 48 8D 05 ?? ?? ?? ?? 48 89 41 20 48 8D 05 ?? ?? ?? ?? 48 89 41 28", 13, 17));
        MountViewVirtualTableRva = Rva(executable, CodeAnchor.Reference("45 8B C1 48 8B F9 E8 ?? ?? ?? ?? 48 89 9F F8 0B 00 00 48 8D 05 ?? ?? ?? ?? 48 89 07", 21, 25));
        PortraitManagerRva = Rva(executable, CodeAnchor.Reference("40 53 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ??", 16, 20));
        HeroPaperDollControlProcedureRva = Rva(executable, CodeAnchor.Reference("48 89 4C 24 30 4C 8D 0D ?? ?? ?? ?? 8B 4F A0 41 B8 07 00 00 00", 8, 12));
        CharacterTableRva = Rva(executable, CodeAnchor.Reference("41 57 48 83 EC 20 48 8D 05 ?? ?? ?? ?? 48 8B F9 48 89 01 48 8D 59 10", 9, 13));
        UiMaterialPacks = UiMaterialPacksOf(executable, CodeAnchor.Site("48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 49 8B D9 49 8B F8 48 8B F2 83 F9 ?? 0F 87 ?? ?? ?? ?? 8B C1 48 8D 15 ?? ?? ?? ?? 8B 8C 82 ?? ?? ?? ??", 0).Locate(executable));
        AsContextRva = Rva(executable, CodeAnchor.Reference("EB ?? 48 8B 05 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? 85 FF 74 ?? FF 50 40", 12, 16));
        StreamCipherRva = Rva(executable, CodeAnchor.Reference("49 8D 8F 2C 01 00 00 8B D6 48 8D 5C 24 20 4C 8B CB E8 ?? ?? ?? ?? 49 8D 4F 58", 18, 22));
        OutboundStreamCipherCallRva = Rva(executable, CodeAnchor.Site("48 8D B3 98 03 00 00 48 8D 8B 34 02 00 00 4C 8B C6 8B D7 4C 8D 74 24 20 4D 8B CE E8 ?? ?? ?? ??", 27));
        SocketWriteRva = Rva(executable, CodeAnchor.Reference("C6 84 24 90 00 00 00 00 44 0F B6 C1 48 8D 94 24 90 00 00 00 48 8B 4F 08 44 88 84 24 91 00 00 00 E8 ?? ?? ?? ??", 33, 37));
        HandshakeSocketWriteCallRva = Rva(executable, CodeAnchor.Site("C6 84 24 90 00 00 00 00 44 0F B6 C1 48 8D 94 24 90 00 00 00 48 8B 4F 08 44 88 84 24 91 00 00 00 E8 ?? ?? ?? ??", 32));
        MillisecondsOverrideRva = Rva(executable, CodeAnchor.Reference("48 8B 05 ?? ?? ?? ?? 48 85 C0 74 03 48 FF E0 48 FF 25", 3, 7));
        SystemFileTimeOverrideRva = Rva(executable, CodeAnchor.Reference("48 83 EC 28 48 8B 05 ?? ?? ?? ?? 48 85 C0 74 07 48 83 C4 28 48 FF E0 48 8D 4C 24 30 FF 15", 7, 11));
        EntropyGenerateRva = Rva(executable, CodeAnchor.Reference("48 8D A8 18 01 00 00 8B CE 48 8B D5 E8 ?? ?? ?? ?? 48 8D 4C 24 60", 13, 17));
        EntropyGenerateCallRvas =
        [
            Rva(executable, CodeAnchor.Site("48 8D A8 18 01 00 00 8B CE 48 8B D5 E8 ?? ?? ?? ?? 48 8D 4C 24 60", 12)),
            Rva(executable, CodeAnchor.Site("8B 47 50 89 44 24 6C 8B 47 10 89 44 24 70 E8 ?? ?? ?? ?? 0F 10 47 6C", 14)),
        ];
        SendMessageLookupRva = Rva(executable, CodeAnchor.Reference("48 8B 4B 18 48 89 BC 24 A8 00 00 00 4C 89 B4 24 A0 00 00 00 44 0F B7 36 41 8B D6 E8 ?? ?? ?? ??", 28, 32));
        SendMessageLookupCallRva = Rva(executable, CodeAnchor.Site("48 8B 4B 18 48 89 BC 24 A8 00 00 00 4C 89 B4 24 A0 00 00 00 44 0F B7 36 41 8B D6 E8 ?? ?? ?? ??", 27));
        EventTickMillisecondsCallRva = Rva(executable, CodeAnchor.Site("48 83 3D ?? ?? ?? ?? 00 75 08 33 C0 48 83 C4 40 5B C3 E8 ?? ?? ?? ?? 8B D8 E8 ?? ?? ?? ?? 90 44 8B C3", 18));
        EventTickFileTimeCallRva = Rva(executable, CodeAnchor.Site("48 83 3D ?? ?? ?? ?? 00 75 08 33 C0 48 83 C4 40 5B C3 E8 ?? ?? ?? ?? 8B D8 E8 ?? ?? ?? ?? 90 44 8B C3", 25));
        TransportKeyScheduleRva = Rva(executable, CodeAnchor.Reference("C7 86 08 01 00 00 03 00 00 00 E8 ?? ?? ?? ?? 48 8D 8E 34 02 00 00", 11, 15));
        TransportKeyScheduleCallRva = Rva(executable, CodeAnchor.Site("C7 86 08 01 00 00 03 00 00 00 E8 ?? ?? ?? ?? 48 8D 8E 34 02 00 00", 10));
        InboundStreamCipherCallRva = Rva(executable, CodeAnchor.Site("49 8D 8F 2C 01 00 00 8B D6 48 8D 5C 24 20 4C 8B CB E8 ?? ?? ?? ?? 49 8D 4F 58", 17));
        TextBuildDrawCallRvas =
        [
            Rva(executable, CodeAnchor.Site("48 89 5C 24 60 48 89 5C 24 58 C7 44 24 68 20 00 00 00 E8 ?? ?? ?? ?? 8B 54 24 64 85 D2 74 ??", 18)),
            Rva(executable, CodeAnchor.Site("44 89 64 24 20 48 89 5C 24 58 C7 44 24 68 20 00 00 00 E8 ?? ?? ?? ?? 8B 54 24 64 85 D2 74 ??", 18)),
        ];
    }

    public static GameAddresses Current => _current ??= new GameAddresses(ExecutableImage.Current);

    public int Build { get; }

    public uint CharacterTableRva { get; }

    public IReadOnlyList<UiMaterialPack> UiMaterialPacks { get; }

    public uint GameConnectionMessageConnectionRva { get; }

    public uint RenderFrameCallRva { get; }
    public uint RenderFrameRva { get; }
    public uint TlsIndexRva { get; }
    public uint ChCliContextVirtualTableRva { get; }
    public uint CoCharVirtualTableRva { get; }
    public uint CoCharSimpleCliVirtualTableRva { get; }
    public uint CoKeyFramedVirtualTableRva { get; }

    public uint EffectContextRva { get; }
    public uint ViewSingletonRva { get; }
    public uint WorldViewContextRva { get; }
    public uint WorldViewContextVirtualTableRva { get; }
    public uint FrameIdManagerRva { get; }
    public uint CharacterArrayRva { get; }
    public uint TextStyleArrayRva { get; }
    public uint FontContextRva { get; }
    public uint SetFrameRateRva { get; }
    public uint FrameBudgetMicrosecondsRva { get; }
    public uint PlayCommandsRva { get; }
    public uint FrameRootRelationRva { get; }
    public uint FrameWindowListRva { get; }
    public uint FrameWindowLinkOffsetRva { get; }
    public uint FrameTopRva { get; }
    public uint FrameScreenSizeRva { get; }
    public uint FrameUiScaleRva { get; }
    public uint FrameDrawWalkFilterRva { get; }
    public uint TextureFileCacheRva { get; }
    public uint MaterialCacheRva { get; }
    public uint CompassControlProcedureRva { get; }
    public uint CompassMarkerControlProcedureRva { get; }
    public uint CompassWaypointControlProcedureRva { get; }
    public uint CompassAgentModelVirtualTableRva { get; }
    public uint CloudVirtualTableRva { get; }
    public uint WorldMapControlProcedureRva { get; }
    public uint WorldMapLabelModelInterfaceVirtualTableRva { get; }
    public uint LevelConfidenceTableRva { get; }
    public uint WorldMapLabelFontKeyRva { get; }
    public uint WorldMapEmphasisedLabelFontKeyRva { get; }
    public uint TextControlProcedureRva { get; }
    public uint TextControlThunkRva { get; }
    public uint DefaultTextStyleRva { get; }
    public uint TextBuildRva { get; }
    public uint RequestedFrameRateRva { get; }
    public uint UnlimitedFrameRateReadRva { get; }
    public uint ModelGeometryAbsentBranchRva { get; }
    public uint ModelPlaceholderFlagClearRva { get; }
    public uint ModelCreateRva { get; }
    public uint ModelParticleCloudUpdateRva { get; }
    public uint AgentShadowUpdateRva { get; }
    public uint GameRenderPostProcessCallsRva { get; }
    public uint ModelPaletteRenderModelBranchRva { get; }
    public uint[] WorldAgentModelCreateReturnRvas { get; }
    public uint GraphicsSettingsRva { get; }
    public uint PostProcessEnableMaskRva { get; }
    public uint AgentViewContextRva { get; }
    public uint CharacterViewVirtualTableRva { get; }
    public uint MountViewVirtualTableRva { get; }
    public uint PortraitManagerRva { get; }
    public uint HeroPaperDollControlProcedureRva { get; }
    public uint AsContextRva { get; }
    public uint[] TextBuildDrawCallRvas { get; }

    public uint StreamCipherRva { get; }
    public uint OutboundStreamCipherCallRva { get; }
    public uint InboundStreamCipherCallRva { get; }
    public uint SocketWriteRva { get; }
    public uint HandshakeSocketWriteCallRva { get; }
    public uint MillisecondsOverrideRva { get; }
    public uint SystemFileTimeOverrideRva { get; }
    public uint EntropyGenerateRva { get; }
    public uint[] EntropyGenerateCallRvas { get; }
    public uint SendMessageLookupRva { get; }
    public uint SendMessageLookupCallRva { get; }
    public uint EventTickMillisecondsCallRva { get; }
    public uint EventTickFileTimeCallRva { get; }
    public uint TransportKeyScheduleRva { get; }
    public uint TransportKeyScheduleCallRva { get; }

    public static GameAddresses FromExecutable(string path) => new(ExecutableImage.Open(path));

    private static List<UiMaterialPack> UiMaterialPacksOf(ExecutableImage executable, ulong selector)
    {
        const int caseCountOffset = 26;
        const int jumpTableOffset = 45;
        int caseCount = executable.Bytes(selector + caseCountOffset, 1)[0] + 1;
        ulong jumpTable = executable.ImageBase + executable.Dword(selector + jumpTableOffset);
        var packs = new List<UiMaterialPack>();
        for (int shader = 0; shader < caseCount; shader++)
        {
            ulong target = executable.ImageBase + executable.Dword(jumpTable + (ulong)(shader * 4));
            var code = executable.Bytes(target, 16);
            if (code.Length < 16 || code[0] != 0x48 || code[1] != 0x8D || code[2] != 0x05 || code[12] != 0x41 || code[13] != 0xC7 || code[14] != 0x00)
                continue;
            ulong pack = target + 7 + (ulong)(long)BinaryPrimitives.ReadInt32LittleEndian(code[3..]);
            int size = (int)executable.Dword(target + 15);
            packs.Add(new UiMaterialPack(shader, (uint)(pack - executable.ImageBase), size));
        }
        return packs;
    }

    private static uint Rva(ExecutableImage executable, CodeAnchor anchor) => (uint)(anchor.Locate(executable) - executable.ImageBase);
}
