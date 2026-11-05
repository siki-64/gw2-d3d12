using Gw2.Formats;

namespace Gw2.Client.Hud.Professions;

public sealed class HudProfessionMechanics(HolosmithHeatThresholds heatThresholds)
{
    private readonly GuardianHud _guardian = new();
    private readonly WarriorHud _warrior = new();
    private readonly EngineerHud _engineer = new(heatThresholds);
    private readonly RangerHud _ranger = new();
    private readonly ThiefHud _thief = new();
    private readonly ElementalistHud _elementalist = new();
    private readonly MesmerHud _mesmer = new();
    private readonly NecromancerHud _necromancer = new();
    private readonly RevenantHud _revenant = new();
    private readonly FallbackHud _fallback = new();

    public HudProfessionMechanic For(HudState state) => state.Profession switch
    {
        Profession.Guardian => _guardian,
        Profession.Warrior => _warrior,
        Profession.Engineer => _engineer,
        Profession.Ranger => _ranger,
        Profession.Thief => _thief,
        Profession.Elementalist => _elementalist,
        Profession.Mesmer => _mesmer,
        Profession.Necromancer => _necromancer,
        Profession.Revenant => _revenant,
        _ => _fallback,
    };
}
