using System;
using System.Globalization;
using System.Numerics;

namespace Gw2.Client.Hud;

public sealed class HudSkillSlot : HudWidget
{
    private const int PlainShader = 3;
    private const int WindowShader = 0xC;
    private const int IconShader = 0x29;
    private const uint DefaultIconFile = 0x265E2;
    private const uint IconMaskFile = 0x265E4;
    private const uint RechargeShadeFile = 0x265E7;
    private const uint RechargeSweepFile = 0x265E8;
    private const uint FrameFile = 0x265E0;
    private const uint RechargeShadeColor = 0xFF0D0D0D;
    private const float ChargeLabelFraction = 0.4f;
    private const float KeyLabelRiseFraction = 0.1f;
    private const float KeyLabelRise = 5f;

    private const float CountdownTenthsBelowSeconds = 5f;

    private static readonly HudArtwork Artwork = new(128, 128, 16, 16, 112, 112);

    public HudSkillSlot(int stateSlotIndex) => StateSlotIndex = stateSlotIndex;

    public int StateSlotIndex { get; }

    public override void Compose(HudComposer composer, HudState state)
    {
        HudSlotState? slot = StateSlotIndex < state.Slots.Count ? state.Slots[StateSlotIndex] : null;
        var art = Artwork.Covering(composer.ScreenRectOf(this));
        var hasSkill = slot?.SkillId is not null;
        SetLayer(0, hasSkill ? composer.Quad(art, IconShader, [slot!.Value.IconFileId ?? DefaultIconFile, IconMaskFile], Vector4.Zero) : null);
        var shadedFraction = hasSkill && slot!.Value.LacksPages ? 0f : slot?.RechargeElapsedFraction ?? 1f;
        SetLayer(1, hasSkill && shadedFraction < 1f
            ? composer.Quad(art, WindowShader, [RechargeShadeFile, RechargeSweepFile], new Vector4(1f, shadedFraction, 0f, 0f), RechargeShadeColor)
            : null);
        SetLayer(2, composer.Quad(art, PlainShader, [FrameFile], Vector4.Zero));
        if (hasSkill && slot!.Value.RechargeRemainingSeconds > 0f)
            composer.Texts.Add(new HudText(composer.ScreenRectOf(this), CountdownTextOf(slot.Value.RechargeRemainingSeconds), HudTextStyle.Countdown));
        if (slot is { KeyLabel.Length: > 0 } keyed)
        {
            var rect = composer.ScreenRectOf(this);
            var centreY = rect.Bottom - (rect.Bottom - rect.Top) * KeyLabelRiseFraction - KeyLabelRise;
            composer.Texts.Add(new HudText(new Gw2.Client.Graphics.GrScreenRect(rect.Left, centreY, rect.Right, centreY), keyed.KeyLabel, HudTextStyle.SkillKey, HasShadowHalo: true));
        }
        if (hasSkill && slot!.Value.Charges is > 0 and var charges)
        {
            var rect = composer.ScreenRectOf(this);
            var size = rect.Right - rect.Left;
            var corner = new Gw2.Client.Graphics.GrScreenRect(rect.Right - size * ChargeLabelFraction, rect.Bottom - size * ChargeLabelFraction, rect.Right, rect.Bottom);
            composer.Texts.Add(new HudText(corner, charges.ToString(CultureInfo.InvariantCulture), HudTextStyle.SkillCharges));
        }
    }

    public static string CountdownTextOf(float remainingSeconds)
    {
        var wholeSeconds = (int)MathF.Floor(remainingSeconds);
        if (remainingSeconds >= CountdownTenthsBelowSeconds) return wholeSeconds.ToString(CultureInfo.InvariantCulture);
        var tenths = (int)MathF.Floor((remainingSeconds - wholeSeconds) * 10f);
        return string.Create(CultureInfo.InvariantCulture, $"{wholeSeconds}.{tenths}");
    }
}
