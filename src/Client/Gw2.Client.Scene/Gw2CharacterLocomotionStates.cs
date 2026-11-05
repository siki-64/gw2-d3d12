using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Scene;

public sealed record Gw2LocomotionState(int StateId, Gw2AnimationClip Clip, float BlendInSeconds, Quaternion Rotation, bool KeepsClipTime);

public sealed class Gw2CharacterLocomotionStates
{
    public const uint KeepClipTimeActionFlag = 0x8;
    private const char NoSecondLetter = '.';

    private readonly Dictionary<int, Gw2LocomotionState> _statesById;

    private Gw2CharacterLocomotionStates(Dictionary<int, Gw2LocomotionState> statesById, float idleBlendInSeconds)
    {
        _statesById = statesById;
        IdleBlendInSeconds = idleBlendInSeconds;
    }

    public float IdleBlendInSeconds { get; }

    public IReadOnlyDictionary<int, Gw2LocomotionState> StatesById => _statesById;

    public bool Has(int stateId) => _statesById.ContainsKey(stateId);

    public Gw2LocomotionState? Find(int stateId) => _statesById.GetValueOrDefault(stateId);

    public static Gw2CharacterLocomotionStates Decode(Gw2ContentReader reader, uint skeletonFileId)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var statesById = new Dictionary<int, Gw2LocomotionState>();
        var bank = reader.ReadModelAnimations(skeletonFileId);
        var machine = MachineOf(reader.ReadAnimMachines(), skeletonFileId);
        if (bank is null || machine is null) return new Gw2CharacterLocomotionStates(statesById, 0f);
        var skeleton = reader.ReadModelSkeleton(skeletonFileId);
        var clipsByToken = new Dictionary<ulong, Gw2AnimationClip?>();
        foreach (var (stateId, stateName) in Gw2CharacterLocomotion.StateNames)
        {
            if (stateId == Gw2CharacterLocomotion.GroundIdle) continue;
            var state = machine.States.FirstOrDefault(entry => entry.Name == stateName);
            var play = state?.Actions.FirstOrDefault(action => action.Kind == Gw2AnimActionKind.PlayAnimation);
            if (state is null || play is null) continue;
            var token = Token64.FillEmptyLetters(play.Token, Gw2CharacterIdle.UnarmedStanceLetter, NoSecondLetter);
            if (!clipsByToken.TryGetValue(token, out var clip))
            {
                var animation = bank.Animations.FirstOrDefault(entry => entry.Token == token);
                clip = animation is null ? null : Gw2ModelAnimationBuilder.BuildClip(reader.ReadStreamedAnimation(animation), skeleton);
                clipsByToken[token] = clip;
            }
            if (clip is null) continue;
            statesById[stateId] = new Gw2LocomotionState(stateId, clip, MathF.Max(0f, play.BlendInTime), RotationOf(state), (play.Flags & KeepClipTimeActionFlag) != 0);
        }
        var idle = machine.States.FirstOrDefault(entry => entry.Name == Gw2CharacterIdle.GroundIdleStateName)?.Actions
            .FirstOrDefault(action => action.Kind is Gw2AnimActionKind.PlayAnimation or Gw2AnimActionKind.PlayBlendTree);
        return new Gw2CharacterLocomotionStates(statesById, MathF.Max(0f, idle?.BlendInTime ?? 0f));
    }

    private static Quaternion RotationOf(Gw2AnimMachineState state) =>
        state.Actions.FirstOrDefault(action => action.Kind == Gw2AnimActionKind.SetRotation && action.Rotation.Count == 4) is { } rotation
            ? new Quaternion(rotation.Rotation[0], rotation.Rotation[1], rotation.Rotation[2], rotation.Rotation[3])
            : Quaternion.Identity;

    private static Gw2AnimMachine? MachineOf(Gw2AnimMachines? machines, uint skeletonFileId)
    {
        var model = machines?.Models.FirstOrDefault(entry => entry.ModelFileId == skeletonFileId);
        return model is null || model.MachineIndex >= machines!.Machines.Count ? null : machines.Machines[(int)model.MachineIndex];
    }
}
