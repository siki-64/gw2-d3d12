using System;
using System.Collections.Generic;
using Gw2.Client.Hud.Professions;
using Gw2.Formats;

namespace Gw2.Client.Hud;

public readonly record struct HudBar(float Value, float Maximum)
{
    public float Fraction => Maximum > 0f ? Math.Clamp(Value / Maximum, 0f, 1f) : 0f;
}

public readonly record struct HudSlotState(string KeyLabel, uint? SkillId, float RechargeRemainingSeconds, uint? IconFileId, float RechargeElapsedFraction, int? Charges, bool LacksPages);

public sealed record HudState(
    IReadOnlyList<HudSlotState> Slots,
    HudBar? OwnHealth,
    HudBar? OwnEndurance,
    Profession? Profession,
    EliteSpecialization? Elite,
    HudBar? ProfessionResource,
    uint? ProfessionState,
    uint? SecondaryProfessionState,
    HudBar? ProfessionCounter,
    HudBar? ProfessionSecondaryMeter,
    HudBar? PetHealth,
    float PetBarrierFraction,
    HudCastBarState? CastBar = null)
{
    public const int FirstProfessionSlot = 10;

    public bool ProfessionSlotHasSkill(int professionSlot)
    {
        var stateSlot = FirstProfessionSlot + professionSlot;
        return stateSlot < Slots.Count && Slots[stateSlot].SkillId is not null;
    }
}
