using Gw2.Formats;
namespace Gw2.Dat.Content;

public enum Gw2LocomotionKind
{
    None,
    Idle,
    Walk,
    Run,
    Jump,
    Fall,
    Land,
    Dodge,
    Swim,
}

public static class Gw2AnimationRoles
{
    public static string Name(ulong token) => Token64.Decode(token);

    public static ulong RoleFamily(ulong token) => token & ~0x1FUL;

    public static bool SharesRoleFamily(ulong left, ulong right) => RoleFamily(left) == RoleFamily(right);

    public static string ClipName(ulong token) => $"anim_{token:X}";

    public static bool IsMountedStance(ulong token)
    {
        var name = Token64.Decode(token);
        return name.Length > 1 && name[1] == 'm';
    }

    public static bool IsLocomotion(ulong token) => Classify(token) != Gw2LocomotionKind.None;

    public static Gw2LocomotionKind Classify(ulong token)
    {
        var name = Token64.Decode(token);
        if (name.Length == 0) return Gw2LocomotionKind.None;
        if (name.Contains("pose", StringComparison.Ordinal)) return Gw2LocomotionKind.None;
        if (name.Contains("jump", StringComparison.Ordinal)) return Gw2LocomotionKind.Jump;
        if (name.Contains("fall", StringComparison.Ordinal)) return Gw2LocomotionKind.Fall;
        if (name.Contains("land", StringComparison.Ordinal)) return Gw2LocomotionKind.Land;
        if (name.Contains("dodge", StringComparison.Ordinal) || name.Contains("evade", StringComparison.Ordinal)) return Gw2LocomotionKind.Dodge;
        if (name.Contains("swim", StringComparison.Ordinal) || name.Contains("swm", StringComparison.Ordinal)) return Gw2LocomotionKind.Swim;
        if (name.Contains("sprint", StringComparison.Ordinal) || name.Contains("run", StringComparison.Ordinal) || name.Contains("jog", StringComparison.Ordinal))
            return Gw2LocomotionKind.Run;
        if (name.Contains("walk", StringComparison.Ordinal)) return Gw2LocomotionKind.Walk;
        if (name.Contains("idle", StringComparison.Ordinal)) return Gw2LocomotionKind.Idle;
        return Gw2LocomotionKind.None;
    }
}

public sealed record Gw2LocomotionSelection(
    Gw2ModelAnimationData? Idle,
    Gw2ModelAnimationData? Walk,
    Gw2ModelAnimationData? Run,
    Gw2ModelAnimationData? Dodge,
    Gw2ModelAnimationData? Swim);

public sealed record Gw2AirborneSelection(
    Gw2ModelAnimationData? Jump,
    Gw2ModelAnimationData? Fall,
    Gw2ModelAnimationData? Land);

public static class Gw2Locomotion
{
    public static Gw2LocomotionSelection Select(IReadOnlyList<Gw2ModelAnimationData> animations)
    {
        ArgumentNullException.ThrowIfNull(animations);

        Gw2ModelAnimationData? idle = null, walk = null, run = null, dodge = null, swim = null;
        foreach (var animation in animations)
        {
            if (!IsGroundLocomotionCandidate(animation)) continue;
            switch (Gw2AnimationRoles.Classify(animation.Token))
            {
                case Gw2LocomotionKind.Idle when idle is null || Prefer(animation, idle):
                    idle = animation;
                    break;
                case Gw2LocomotionKind.Walk when walk is null || Prefer(animation, walk):
                    walk = animation;
                    break;
                case Gw2LocomotionKind.Run when run is null || Prefer(animation, run):
                    run = animation;
                    break;
                case Gw2LocomotionKind.Dodge when dodge is null || Prefer(animation, dodge):
                    dodge = animation;
                    break;
                case Gw2LocomotionKind.Swim when swim is null || Prefer(animation, swim):
                    swim = animation;
                    break;
            }
        }

        return new(idle, walk, run, dodge, swim);
    }

    public static Gw2AirborneSelection SelectAirborne(IReadOnlyList<Gw2ModelAnimationData> animations)
    {
        ArgumentNullException.ThrowIfNull(animations);

        Gw2ModelAnimationData? jump = null, fall = null, land = null;
        foreach (var animation in animations)
        {
            if (!IsGroundLocomotionCandidate(animation)) continue;
            switch (Gw2AnimationRoles.Classify(animation.Token))
            {
                case Gw2LocomotionKind.Jump when jump is null || Prefer(animation, jump):
                    jump = animation;
                    break;
                case Gw2LocomotionKind.Fall when fall is null || Prefer(animation, fall):
                    fall = animation;
                    break;
                case Gw2LocomotionKind.Land when land is null || Prefer(animation, land):
                    land = animation;
                    break;
            }
        }

        return new(jump, fall, land);
    }

    private static bool IsGroundLocomotionCandidate(Gw2ModelAnimationData animation) =>
        !animation.IsAdditive && !Gw2AnimationRoles.IsMountedStance(animation.Token);

    private static bool Prefer(Gw2ModelAnimationData candidate, Gw2ModelAnimationData current)
    {
        var candidateName = Gw2AnimationRoles.Name(candidate.Token);
        var currentName = Gw2AnimationRoles.Name(current.Token);
        if (candidateName.Contains("dodge", StringComparison.Ordinal) != currentName.Contains("dodge", StringComparison.Ordinal))
            return candidateName.Contains("dodge", StringComparison.Ordinal);
        if (candidateName.Contains("swim", StringComparison.Ordinal) != currentName.Contains("swim", StringComparison.Ordinal))
            return candidateName.Contains("swim", StringComparison.Ordinal);
        var candidateShared = candidateName.StartsWith("uam", StringComparison.Ordinal) || candidateName.StartsWith("ugm", StringComparison.Ordinal);
        var currentShared = currentName.StartsWith("uam", StringComparison.Ordinal) || currentName.StartsWith("ugm", StringComparison.Ordinal);
        if (candidateShared != currentShared) return candidateShared;
        return candidateName.Length < currentName.Length;
    }
}
