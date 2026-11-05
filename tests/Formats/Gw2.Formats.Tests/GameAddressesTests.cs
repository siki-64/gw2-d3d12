using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Formats.Tests;

[TestClass]
public sealed class GameAddressesTests
{
    private const int AnchoredBuild = 209026;

    private static string ExecutablePath()
    {
        var path = Gw2Installation.ExecutablePath();
        if (path is null || !File.Exists(path)) Assert.Inconclusive("Gw2-64.exe is not installed.");
        return path;
    }

    [TestMethod]
    public void LocatesTheGameAddressesOfTheAnchoredBuild()
    {
        var addresses = GameAddresses.FromExecutable(ExecutablePath());
        if (addresses.Build != AnchoredBuild) Assert.Inconclusive($"the installed exe is build {addresses.Build}");
        Assert.AreEqual(0x2160BE0u, addresses.CharacterTableRva, "CharacterTableRva");
        CollectionAssert.IsSubsetOf(new[] { new UiMaterialPack(0, 0x1940110, 22716), new UiMaterialPack(6, 0x195F960, 29268), new UiMaterialPack(52, 0x1AAB5A0, 32476) }, addresses.UiMaterialPacks.ToArray());
        Assert.AreEqual(0x266C1D0u, addresses.GameConnectionMessageConnectionRva, "GameConnectionMessageConnectionRva");
        Assert.AreEqual(0x94D968u, addresses.RenderFrameCallRva, "RenderFrameCallRva");
        Assert.AreEqual(0x9548C0u, addresses.RenderFrameRva, "RenderFrameRva");
        Assert.AreEqual(0x28155C0u, addresses.TlsIndexRva, "TlsIndexRva");
        Assert.AreEqual(0x215DFC8u, addresses.ChCliContextVirtualTableRva, "ChCliContextVirtualTableRva");
        Assert.AreEqual(0x212C908u, addresses.CoCharVirtualTableRva, "CoCharVirtualTableRva");
        Assert.AreEqual(0x212D1C8u, addresses.CoCharSimpleCliVirtualTableRva, "CoCharSimpleCliVirtualTableRva");
        Assert.AreEqual(0x212D910u, addresses.CoKeyFramedVirtualTableRva, "CoKeyFramedVirtualTableRva");
        Assert.AreEqual(0x267F878u, addresses.ViewSingletonRva, "ViewSingletonRva");
        Assert.AreEqual(0x2680290u, addresses.WorldViewContextRva, "WorldViewContextRva");
        Assert.AreEqual(0x1B8B580u, addresses.WorldViewContextVirtualTableRva, "WorldViewContextVirtualTableRva");
        Assert.AreEqual(0x289DD80u, addresses.FrameIdManagerRva, "FrameIdManagerRva");
        Assert.AreEqual(0x266BF98u, addresses.CharacterArrayRva, "CharacterArrayRva");
        Assert.AreEqual(0x289C978u, addresses.TextStyleArrayRva, "TextStyleArrayRva");
        Assert.AreEqual(0x268AF70u, addresses.FontContextRva, "FontContextRva");
        Assert.AreEqual(0x1069EB0u, addresses.SetFrameRateRva, "SetFrameRateRva");
        Assert.AreEqual(0x289C798u, addresses.FrameBudgetMicrosecondsRva, "FrameBudgetMicrosecondsRva");
        Assert.AreEqual(0xB011D0u, addresses.PlayCommandsRva, "PlayCommandsRva");
        Assert.AreEqual(0x289DE20u, addresses.FrameRootRelationRva, "FrameRootRelationRva");
        Assert.AreEqual(0x289DE70u, addresses.FrameWindowListRva, "FrameWindowListRva");
        Assert.AreEqual(0x289DE60u, addresses.FrameWindowLinkOffsetRva, "FrameWindowLinkOffsetRva");
        Assert.AreEqual(0x289DEF8u, addresses.FrameTopRva, "FrameTopRva");
        Assert.AreEqual(0x289DEA8u, addresses.FrameScreenSizeRva, "FrameScreenSizeRva");
        Assert.AreEqual(0x289DEB0u, addresses.FrameUiScaleRva, "FrameUiScaleRva");
        Assert.AreEqual(0x289CB2Cu, addresses.FrameDrawWalkFilterRva, "FrameDrawWalkFilterRva");
        Assert.AreEqual(0x268B840u, addresses.TextureFileCacheRva, "TextureFileCacheRva");
        Assert.AreEqual(0x2671DB0u, addresses.MaterialCacheRva, "MaterialCacheRva");
        Assert.AreEqual(0x28B4510u, addresses.EffectContextRva, "EffectContextRva");
        Assert.AreEqual(0x465500u, addresses.CompassControlProcedureRva, "CompassControlProcedureRva");
        Assert.AreEqual(0x46D840u, addresses.CompassMarkerControlProcedureRva, "CompassMarkerControlProcedureRva");
        Assert.AreEqual(0x470680u, addresses.CompassWaypointControlProcedureRva, "CompassWaypointControlProcedureRva");
        Assert.AreEqual(0x1B85028u, addresses.CompassAgentModelVirtualTableRva, "CompassAgentModelVirtualTableRva");
        Assert.AreEqual(0x1C7D4E8u, addresses.CloudVirtualTableRva, "CloudVirtualTableRva");
        Assert.AreEqual(0x8F9D60u, addresses.WorldMapControlProcedureRva, "WorldMapControlProcedureRva");
        Assert.AreEqual(0x1B863E0u, addresses.WorldMapLabelModelInterfaceVirtualTableRva, "WorldMapLabelModelInterfaceVirtualTableRva");
        Assert.AreEqual(0x25BDE7Cu, addresses.LevelConfidenceTableRva, "LevelConfidenceTableRva");
        Assert.AreEqual(0x2680270u, addresses.WorldMapLabelFontKeyRva, "WorldMapLabelFontKeyRva");
        Assert.AreEqual(0x2680278u, addresses.WorldMapEmphasisedLabelFontKeyRva, "WorldMapEmphasisedLabelFontKeyRva");
        Assert.AreEqual(0x1047DE0u, addresses.TextControlProcedureRva, "TextControlProcedureRva");
        Assert.AreEqual(0x290600u, addresses.TextControlThunkRva, "TextControlThunkRva");
        Assert.AreEqual(0x289C9DCu, addresses.DefaultTextStyleRva, "DefaultTextStyleRva");
        Assert.AreEqual(0x1076CF0u, addresses.TextBuildRva, "TextBuildRva");
        CollectionAssert.AreEqual(new uint[] { 0x107037C, 0x10704CE }, addresses.TextBuildDrawCallRvas);
        Assert.AreEqual(0xFF4DC0u, addresses.StreamCipherRva, "StreamCipherRva");
        Assert.AreEqual(0xFEF7B8u, addresses.OutboundStreamCipherCallRva, "OutboundStreamCipherCallRva");
        Assert.AreEqual(0xFF0069u, addresses.InboundStreamCipherCallRva, "InboundStreamCipherCallRva");
        Assert.AreEqual(0xFE9E60u, addresses.SocketWriteRva, "SocketWriteRva");
        Assert.AreEqual(0xFEFD11u, addresses.HandshakeSocketWriteCallRva, "HandshakeSocketWriteCallRva");
        Assert.AreEqual(0x2682360u, addresses.MillisecondsOverrideRva, "MillisecondsOverrideRva");
        Assert.AreEqual(0x2682368u, addresses.SystemFileTimeOverrideRva, "SystemFileTimeOverrideRva");
        Assert.AreEqual(0xFE52D0u, addresses.EntropyGenerateRva, "EntropyGenerateRva");
        CollectionAssert.AreEqual(new uint[] { 0xFEFBDC, 0x243863 }, addresses.EntropyGenerateCallRvas);
        Assert.AreEqual(0xFF39D0u, addresses.SendMessageLookupRva, "SendMessageLookupRva");
        Assert.AreEqual(0xFF01AEu, addresses.SendMessageLookupCallRva, "SendMessageLookupCallRva");
        Assert.AreEqual(0x106A158u, addresses.EventTickMillisecondsCallRva, "EventTickMillisecondsCallRva");
        Assert.AreEqual(0x106A15Fu, addresses.EventTickFileTimeCallRva, "EventTickFileTimeCallRva");
        Assert.AreEqual(0xFF5050u, addresses.TransportKeyScheduleRva, "TransportKeyScheduleRva");
        Assert.AreEqual(0xFEE9E1u, addresses.TransportKeyScheduleCallRva, "TransportKeyScheduleCallRva");
        Assert.AreEqual(0x289C79Cu, addresses.RequestedFrameRateRva, "RequestedFrameRateRva");
        Assert.AreEqual(0x954D3Au, addresses.UnlimitedFrameRateReadRva, "UnlimitedFrameRateReadRva");
        Assert.AreEqual(0xCED7A5u, addresses.ModelGeometryAbsentBranchRva, "ModelGeometryAbsentBranchRva");
        Assert.AreEqual(0xCED881u, addresses.ModelPlaceholderFlagClearRva, "ModelPlaceholderFlagClearRva");
        Assert.AreEqual(0xCF7000u, addresses.ModelCreateRva, "ModelCreateRva");
        Assert.AreEqual(0xAC8540u, addresses.ModelParticleCloudUpdateRva, "ModelParticleCloudUpdateRva");
        Assert.AreEqual(0x1156E30u, addresses.AgentShadowUpdateRva, "AgentShadowUpdateRva");
        Assert.AreEqual(0x9A30B0u, addresses.GameRenderPostProcessCallsRva, "GameRenderPostProcessCallsRva");
        Assert.AreEqual(0xCF828Eu, addresses.ModelPaletteRenderModelBranchRva, "ModelPaletteRenderModelBranchRva");
        CollectionAssert.AreEqual(new uint[] { 0x1118368, 0x1118B7D, 0x113BC19, 0x113F4C7, 0x11544D8, 0x1165B11, 0x1166EEF, 0x11671EC, 0x11699FD, 0x116CFBC, 0x116D380 }, addresses.WorldAgentModelCreateReturnRvas);
        Assert.AreEqual(0x25FE600u, addresses.GraphicsSettingsRva, "GraphicsSettingsRva");
        Assert.AreEqual(0x27F95C8u, addresses.PostProcessEnableMaskRva, "PostProcessEnableMaskRva");
        Assert.AreEqual(0x25BA680u, addresses.AgentViewContextRva, "AgentViewContextRva");
        Assert.AreEqual(0x2149138u, addresses.CharacterViewVirtualTableRva, "CharacterViewVirtualTableRva");
        Assert.AreEqual(0x255AA60u, addresses.PortraitManagerRva, "PortraitManagerRva");
        Assert.AreEqual(0x56F220u, addresses.HeroPaperDollControlProcedureRva, "HeroPaperDollControlProcedureRva");
        Assert.AreEqual(0x28A6B00u, addresses.AsContextRva, "AsContextRva");
    }

    [TestMethod]
    public void LocatesTheBgfxEntryPointsOfTheAnchoredBuild()
    {
        var library = BgfxLibrary.FromExecutable(ExecutablePath());
        if (GameAddresses.FromExecutable(ExecutablePath()).Build != AnchoredBuild) Assert.Inconclusive("the installed exe is another build");
        Assert.AreEqual(0x27BBDD0u, library.CapsRva, "CapsRva");
        Assert.AreEqual(0x27BBDC8u, library.AllocatorRva, "AllocatorRva");
        Assert.AreEqual(0x27BBDC0u, library.CallbackRva, "CallbackRva");
        Assert.AreEqual(0xB71270u, library.ReleaseRva, "ReleaseRva");
        Assert.AreEqual(0xB669D0u, library.GetRendererTypeRva, "GetRendererTypeRva");
        CollectionAssert.AreEqual(new BgfxEntry[]
        {
            new(0xB58CA0, BgfxEntryReplacement.InstanceDataBuffer),
            new(0xB58DE0, BgfxEntryReplacement.TransientIndexBuffer),
            new(0xB58EF0, BgfxEntryReplacement.TransientVertexBuffer),
            new(0xB59780, BgfxEntryReplacement.EncoderBegin),
            new(0xB59B60, BgfxEntryReplacement.ReturnZero),
            new(0xB59BF0, BgfxEntryReplacement.ReturnZero),
            new(0xB5A5C0, BgfxEntryReplacement.ReturnOne),
            new(0xB5DEC0, BgfxEntryReplacement.NewHandleReleasingFirst),
            new(0xB5E200, BgfxEntryReplacement.NewHandleReleasingFirst),
            new(0xB5E2B0, BgfxEntryReplacement.NewHandle),
            new(0xB5EDF0, BgfxEntryReplacement.NewHandle),
            new(0xB5EE90, BgfxEntryReplacement.NewHandle),
            new(0xB5F130, BgfxEntryReplacement.NewHandleReleasingFirst),
            new(0xB5F160, BgfxEntryReplacement.NewHandle),
            new(0xB5F880, BgfxEntryReplacement.CreateProgram),
            new(0xB5F8E0, BgfxEntryReplacement.CreateProgram),
            new(0xB5FF90, BgfxEntryReplacement.CreateShader),
            new(0xB601C0, BgfxEntryReplacement.CreateTexture2D),
            new(0xB604C0, BgfxEntryReplacement.NewHandleReleasingSeventh),
            new(0xB60B00, BgfxEntryReplacement.NewHandleReleasingSixth),
            new(0xB61830, BgfxEntryReplacement.CreateUniform),
            new(0xB61B90, BgfxEntryReplacement.NewHandleReleasingFirst),
            new(0xB62400, BgfxEntryReplacement.Return),
            new(0xB62460, BgfxEntryReplacement.Return),
            new(0xB624C0, BgfxEntryReplacement.ReturnZero),
            new(0xB624D0, BgfxEntryReplacement.ReturnZero),
            new(0xB624E0, BgfxEntryReplacement.Return),
            new(0xB62540, BgfxEntryReplacement.ReturnZero),
            new(0xB62550, BgfxEntryReplacement.Return),
            new(0xB625B0, BgfxEntryReplacement.DestroyTexture),
            new(0xB62610, BgfxEntryReplacement.ReturnZero),
            new(0xB62620, BgfxEntryReplacement.ReturnZero),
            new(0xB632A0, BgfxEntryReplacement.ReturnZero),
            new(0xB641E0, BgfxEntryReplacement.Return),
            new(0xB65600, BgfxEntryReplacement.Frame),
            new(0xB660E0, BgfxEntryReplacement.TransientIndexSpace),
            new(0xB66230, BgfxEntryReplacement.TransientVertexSpace),
            new(0xB669E0, BgfxEntryReplacement.ReturnZero),
            new(0xB66DF0, BgfxEntryReplacement.ShaderUniforms),
            new(0xB66F00, BgfxEntryReplacement.Stats),
            new(0xB67190, BgfxEntryReplacement.UniformInfo),
            new(0xB6AFD0, BgfxEntryReplacement.Init),
            new(0xB710D0, BgfxEntryReplacement.ReturnZero),
            new(0xB73F00, BgfxEntryReplacement.ReturnZero),
            new(0xB750E0, BgfxEntryReplacement.ReturnZero),
            new(0xB750F0, BgfxEntryReplacement.ReturnZero),
            new(0xB756C0, BgfxEntryReplacement.ReturnZero),
            new(0xB75720, BgfxEntryReplacement.ReturnZero),
            new(0xB75780, BgfxEntryReplacement.ReturnZero),
            new(0xB75810, BgfxEntryReplacement.ReturnZero),
            new(0xB75860, BgfxEntryReplacement.ReturnZero),
            new(0xB758A0, BgfxEntryReplacement.ReturnZero),
            new(0xB75960, BgfxEntryReplacement.ReturnZero),
            new(0xB75FE0, BgfxEntryReplacement.ReturnZero),
            new(0xB76170, BgfxEntryReplacement.Return),
            new(0xB76690, BgfxEntryReplacement.ReturnZero),
            new(0xB76740, BgfxEntryReplacement.ReturnZero),
            new(0xB767D0, BgfxEntryReplacement.ReturnZero),
            new(0xB76820, BgfxEntryReplacement.ReturnZero),
            new(0xB76860, BgfxEntryReplacement.ReturnZero),
            new(0xB768F0, BgfxEntryReplacement.ReturnZero),
            new(0xB76960, BgfxEntryReplacement.ReturnZero),
            new(0xB76A30, BgfxEntryReplacement.ReturnZero),
            new(0xB76AB0, BgfxEntryReplacement.ReturnZero),
            new(0xB76BF0, BgfxEntryReplacement.ReturnZero),
            new(0xB76C90, BgfxEntryReplacement.ReturnZero),
            new(0xB76D60, BgfxEntryReplacement.ReturnZero),
            new(0xB76E20, BgfxEntryReplacement.ReturnZero),
            new(0xB76E80, BgfxEntryReplacement.ReturnZero),
            new(0xB76EF0, BgfxEntryReplacement.ReturnZero),
            new(0xB76F80, BgfxEntryReplacement.ReturnZero),
            new(0xB77030, BgfxEntryReplacement.ReturnZero),
            new(0xB770A0, BgfxEntryReplacement.ReturnZero),
            new(0xB770C0, BgfxEntryReplacement.ReturnZero),
            new(0xB770E0, BgfxEntryReplacement.ReturnZero),
            new(0xB77140, BgfxEntryReplacement.ReturnZero),
            new(0xB77D90, BgfxEntryReplacement.ReturnZero),
            new(0xB783C0, BgfxEntryReplacement.ReturnZero),
            new(0xB7B400, BgfxEntryReplacement.ReturnZero),
            new(0xB7C0B0, BgfxEntryReplacement.ReturnZero),
            new(0xB7CF40, BgfxEntryReplacement.ReleaseThird),
            new(0xB7CF60, BgfxEntryReplacement.ReleaseThird),
            new(0xB7D870, BgfxEntryReplacement.UpdateTexture2D),
            new(0xB7D910, BgfxEntryReplacement.ReleaseNinth),
            new(0xB7DCF0, BgfxEntryReplacement.ReleaseNinth),
        }, library.Entries);
    }
}
