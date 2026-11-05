using System;
using System.Collections.Generic;
using System.Linq;
using Gw2.Client.Model;
using Gw2.Dat.Content;
using Gw2.Formats;

namespace Gw2.Client.Scene;

public static class Gw2CharacterIdle
{
    public const string GroundIdleStateName = "Ground Idle";
    public const char UnarmedStanceLetter = 'u';
    private const char NoSecondLetter = '.';

    public static Gw2CharacterIdlePlayback? Decode(Gw2ContentReader reader, uint skeletonFileId)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var bank = reader.ReadModelAnimations(skeletonFileId);
        if (bank is null) return null;
        var skeleton = reader.ReadModelSkeleton(skeletonFileId);

        if (MachineIdleTree(reader.ReadAnimMachines(), skeletonFileId) is { } treeContentKey && TreePlaybackOf(reader, treeContentKey, bank, skeleton) is { } treePlayback)
            return treePlayback;
        var idle = MachineIdle(reader.ReadAnimMachines(), bank, skeletonFileId) ?? Gw2Locomotion.Select(bank.Animations).Idle;
        return idle is null ? null : new Gw2CharacterIdlePlayback(Gw2ModelAnimationBuilder.BuildClip(reader.ReadStreamedAnimation(idle), skeleton));
    }

    public static Gw2CharacterIdlePlayback? DecodePreview(Gw2ContentReader reader, uint skeletonFileId)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var bank = reader.ReadModelAnimations(skeletonFileId);
        var animation = bank?.Animations.FirstOrDefault(candidate => candidate.Token == PreviewIdleToken);
        if (animation is null) return Decode(reader, skeletonFileId);
        return new Gw2CharacterIdlePlayback(Gw2ModelAnimationBuilder.BuildClip(reader.ReadStreamedAnimation(animation), reader.ReadModelSkeleton(skeletonFileId)));
    }

    public static readonly ulong PreviewIdleToken = Token64.Encode("ugmidle");

    public static Guid? MachineIdleTree(Gw2AnimMachines? machines, uint skeletonFileId)
    {
        var action = MachineIdleAction(machines, skeletonFileId);
        return action is { Kind: Gw2AnimActionKind.PlayBlendTree } && action.BlendTree.Length == TreeContentKeySize ? new Guid(action.BlendTree.Span) : null;
    }

    public static Gw2ModelAnimationData? MachineIdle(Gw2AnimMachines? machines, Gw2ModelAnimation bank, uint skeletonFileId, Func<Guid, ulong[]>? blendTreeStartingClips = null)
    {
        ArgumentNullException.ThrowIfNull(bank);
        var action = MachineIdleAction(machines, skeletonFileId);
        if (action is null) return null;
        if (action.Kind == Gw2AnimActionKind.PlayAnimation) return AnimationOf(bank, action.Token);
        if (blendTreeStartingClips is null || action.BlendTree.Length != TreeContentKeySize) return null;
        return blendTreeStartingClips(new Guid(action.BlendTree.Span)).Select(token => AnimationOf(bank, token)).FirstOrDefault(animation => animation is not null);
    }

    private const int TreeContentKeySize = 16;

    private static Gw2AnimAction? MachineIdleAction(Gw2AnimMachines? machines, uint skeletonFileId)
    {
        if (machines is null) return null;
        var model = machines.Models.FirstOrDefault(entry => entry.ModelFileId == skeletonFileId);
        if (model is null || model.MachineIndex >= machines.Machines.Count) return null;
        var state = machines.Machines[(int)model.MachineIndex].States.FirstOrDefault(entry => entry.Name == GroundIdleStateName);
        return state?.Actions.FirstOrDefault(entry => entry.Kind is Gw2AnimActionKind.PlayAnimation or Gw2AnimActionKind.PlayBlendTree);
    }

    private static Gw2CharacterIdlePlayback? TreePlaybackOf(Gw2ContentReader reader, Guid treeContentKey, Gw2ModelAnimation bank, Gw2ModelSkeleton? skeleton)
    {
        var clipsByToken = new Dictionary<ulong, Gw2AnimationClip>();
        if (new Gw2BlendTrees(reader.ContentIndex, BlendTreeContentLayout.Current).Find(treeContentKey) is not { } tree) return null;
        var player = Gw2BlendTreePlayer.Of(tree, token => clipsByToken.TryGetValue(token, out var clip) ? clip.Duration : null, new Random());
        if (player is null) return null;
        foreach (var token in player.ClipTokens)
            if (AnimationOf(bank, token) is { } animation && Gw2ModelAnimationBuilder.BuildClip(reader.ReadStreamedAnimation(animation), skeleton) is { } clip)
                clipsByToken[token] = clip;
        if (clipsByToken.Count == 0) return null;
        player.Start();
        return new Gw2CharacterIdlePlayback(player, clipsByToken);
    }

    private static Gw2ModelAnimationData? AnimationOf(Gw2ModelAnimation bank, ulong token)
    {
        var filled = Token64.FillEmptyLetters(token, UnarmedStanceLetter, NoSecondLetter);
        return bank.Animations.FirstOrDefault(animation => animation.Token == filled);
    }
}
