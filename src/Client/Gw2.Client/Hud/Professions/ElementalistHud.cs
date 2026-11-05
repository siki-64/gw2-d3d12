using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Gw2.Client.Graphics;

namespace Gw2.Client.Hud.Professions;

public sealed class ElementalistHud : HudProfessionMechanic
{
    private const float ActiveSize = 44f;
    private const float SecondarySize = 38f;
    private const float InactiveSize = 32f;
    private const float AttunementGap = 2f;
    private const float SlotBottom = 2f;
    private const int AttunementCount = 4;
    private const int SlotCount = 5;
    private const int F5Index = 4;
    private const float F5RightPadding = 42f;
    private const float WideF5RightPadding = 53f;
    private static readonly uint[] SwirlFiles = [0x18C962, 0x18C963, 0x18C960, 0x18C961];
    private const uint PrimarySwirlMaskFile = 0x18C95A;
    private const uint SecondarySwirlMaskFile = 0x18C95B;
    private const int PrimarySwirlShader = 0x22;
    private static readonly HudPanelRect PrimarySwirl = new(0f, -9f, 140f, 8f);
    private static readonly HudPanelRect SecondarySwirl = new(125f, -7f, 140f, 8f);
    private const float SwirlScrollPerSecond = 100f;
    private const float SwirlWindowWidth = 256f;
    private const float SwirlWindowHeight = 128f;
    private static readonly Stopwatch SwirlClock = Stopwatch.StartNew();
    private const uint CatalystIconFile = 0x26068E;
    private const uint CatalystEnergyBackingFile = 0x26068A;
    private const uint CatalystEnergyFlowFile = 0x26068B;
    private const uint CatalystEnergyRampFile = 0x26068D;
    private static readonly HudPanelRect CatalystIcon = new(151f, -2f, 20f, 40f);
    private static readonly HudPanelRect CatalystEnergyBar = new(176f, 40f, 36f, 6f);
    private static readonly uint[] CatalystEnergyColorByAttunement = [0xFFFFFFFF, 0xFFFC7C02, 0xFF00AAFF, 0xFFA769FB, 0xFFE8C519];

    public override IReadOnlyList<HudPanelRect?> SlotRectsOf(HudState state)
    {
        var rects = new HudPanelRect?[SlotCount];
        var left = 0f;
        for (var index = 0; index < AttunementCount; index++)
        {
            var attunement = (uint)(index + 1);
            var size = state.ProfessionState == attunement ? ActiveSize : state.SecondaryProfessionState == attunement ? SecondarySize : InactiveSize;
            rects[index] = new HudPanelRect(left, SlotBottom, size, size);
            left += size + AttunementGap;
        }
        var elite = state.Elite;
        var hasF5Container = elite is EliteSpecialization.Weaver or EliteSpecialization.Catalyst or EliteSpecialization.ElementalistWithWideF5;
        if (hasF5Container && state.ProfessionSlotHasSkill(F5Index))
        {
            var rightPadding = elite == EliteSpecialization.ElementalistWithWideF5 ? WideF5RightPadding : F5RightPadding;
            rects[F5Index] = new HudPanelRect(PanelWidth - rightPadding - SecondarySize, SlotBottom, SecondarySize, SecondarySize);
        }
        return rects;
    }

    public override void AddArt(HudProfessionArt art)
    {
        if (art.Elite == EliteSpecialization.Weaver) AddWeaverSwirls(art);
        else if (art.Elite == EliteSpecialization.Catalyst) AddCatalystEnergy(art);
    }

    private static void AddWeaverSwirls(HudProfessionArt art)
    {
        var scroll = (float)(SwirlClock.Elapsed.TotalSeconds * SwirlScrollPerSecond % SwirlWindowWidth);
        var primary = art.State.ProfessionState;
        var secondary = art.State.SecondaryProfessionState is null or 0 ? primary : art.State.SecondaryProfessionState;
        if (SwirlFileOf(primary) is { } primaryFile)
            art.Add(PrimarySwirl, PrimarySwirlShader, [primaryFile, PrimarySwirlMaskFile], Vector4.Zero, transforms: [SwirlWindowOf(scroll, -scroll)]);
        if (SwirlFileOf(secondary) is { } secondaryFile)
            art.Add(SecondarySwirl, HudProfessionArt.MaskedColourShader, [secondaryFile, SecondarySwirlMaskFile], Vector4.Zero, transforms: [SwirlWindowOf(-scroll, scroll)]);
    }

    private static uint? SwirlFileOf(uint? attunement) =>
        attunement is >= 1 and <= AttunementCount ? SwirlFiles[attunement.Value - 1] : null;

    private static GrUiTextureTransform SwirlWindowOf(float windowLeft, float windowTop) =>
        new(new Vector3(1f, 0f, -windowLeft / SwirlWindowWidth), new Vector3(0f, 1f, -windowTop / SwirlWindowHeight));

    private static void AddCatalystEnergy(HudProfessionArt art)
    {
        art.Image(CatalystIcon, CatalystIconFile);
        art.Image(CatalystEnergyBar, CatalystEnergyBackingFile);
        var attunement = art.State.ProfessionState is { } active && active < CatalystEnergyColorByAttunement.Length ? (int)active : 0;
        art.Fill(CatalystEnergyBar, CatalystEnergyFlowFile, CatalystEnergyRampFile, art.Resource.Fraction, CatalystEnergyColorByAttunement[attunement]);
    }
}
