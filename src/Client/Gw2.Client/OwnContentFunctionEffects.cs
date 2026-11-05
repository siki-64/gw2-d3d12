using System.Collections.Generic;
using Gw2.Dat.Content;

namespace Gw2.Client;

public sealed class OwnContentFunctionEffects
{
    private static readonly uint[] AdvancedMovementInputBits = [2, 4, 8, 16, 128, 256];

    private readonly Dictionary<ContentFunctionOpcode, ContentFunctionEffectNode> _latestByOpcode = [];

    public uint AdvancedMovementInputFlags { get; private set; }

    public bool NameplatesHidden { get; private set; }

    public bool JumpRequested { get; set; }

    public bool AirToGroundTransitionEnabled { get; private set; } = true;

    public bool StoppingInAirAllowed { get; private set; }

    public bool FallDistanceResetRequested { get; set; }

    public bool OrientToCameraRequested { get; set; }

    public System.Numerics.Vector3 AppliedForce { get; private set; }

    public IReadOnlyDictionary<ContentFunctionOpcode, ContentFunctionEffectNode> LatestByOpcode => _latestByOpcode;

    public void SetAdvancedMovementInput(uint input, bool active)
    {
        if (input >= AdvancedMovementInputBits.Length) return;
        AdvancedMovementInputFlags = active ? AdvancedMovementInputFlags | AdvancedMovementInputBits[input] : AdvancedMovementInputFlags & ~AdvancedMovementInputBits[input];
    }

    public void ApplyForce(float x, float y, float z) => AppliedForce += new System.Numerics.Vector3(x, y, z);

    public void Perform(ContentFunctionEffectNode effect)
    {
        _latestByOpcode[effect.Kind] = effect;
        switch (effect.Kind)
        {
            case ContentFunctionOpcode.HideNameplates:
                NameplatesHidden = true;
                break;
            case ContentFunctionOpcode.ShowNameplates:
                NameplatesHidden = false;
                break;
            case ContentFunctionOpcode.Jump or ContentFunctionOpcode.JumpCustom or ContentFunctionOpcode.JumpScaleByEnergy or ContentFunctionOpcode.JumpImpulse
                or ContentFunctionOpcode.JumpImpulseByCharge or ContentFunctionOpcode.JumpImpulseScaleByEnergy or ContentFunctionOpcode.JumpImpulseScaleByEnergySpeedSlope
                or ContentFunctionOpcode.JumpImpulseScaleByHorizontalSpeed or ContentFunctionOpcode.JumpImpulseInMovementDirectionScaleBySpeed:
                JumpRequested = true;
                break;
            case ContentFunctionOpcode.ResetFallDistance:
                FallDistanceResetRequested = true;
                break;
            case ContentFunctionOpcode.OrientToCamera:
                OrientToCameraRequested = true;
                break;
            case ContentFunctionOpcode.SetAirToGroundTransitionEnabled:
                AirToGroundTransitionEnabled = effect.Word(ContentFunctionArgument.Enabled) != 0;
                break;
            case ContentFunctionOpcode.AllowStoppingInAir:
                StoppingInAirAllowed = effect.Word(ContentFunctionArgument.Enabled) != 0;
                break;
        }
    }
}
