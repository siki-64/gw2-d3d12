namespace Gw2.Dat.Content;

public abstract record ContentFunctionReport;

public sealed record ContentFunctionInstanceStarted(uint InstanceId, uint TargetAgentId, uint Source, uint FunctionUid, uint InputId) : ContentFunctionReport;

public sealed record ContentFunctionInputReleased(uint FunctionUid) : ContentFunctionReport;

public sealed record ContentFunctionConditionEvaluated(uint InstanceId, uint RootFunctionUid, uint FunctionUid, int NodeIndex, bool Result) : ContentFunctionReport;

public sealed record ContentFunctionSkillActivated(uint FunctionUid, int NodeIndex, uint? SkillDataId) : ContentFunctionReport;

public interface IContentFunctionHost
{
    bool HasClientInitiative => true;
    float Endurance => 0;
    float MaximumEndurance => 0;
    float Energy(uint kind) => 0;
    byte LocaleState => 0;
    float MovementParameter(uint parameter) => 0;
    uint AdvancedMovementInputFlags => 0;
    bool HasBuff(uint buffDataId, bool onSelf) => false;
    uint BuffCount(uint buffDataId) => 0;
    bool HasMovementModifier(uint movementModifierUid) => false;
    uint SkillRechargeRemainingMilliseconds(uint skillDataId) => 0;
    bool IsMount => false;
    bool TargetWithinFacingAngle(float degrees) => false;
    uint MapMode => 0;
    bool MoverStateHasFlag(uint flag) => false;
    void RemoveMovementModifier(uint movementModifierUid) { }
    void ApplyMovementModifier(uint movementModifierUid, uint? durationMilliseconds) { }
    void AdjustEndurance(int amount) { }
    void Perform(ContentFunctionEffectNode effect) { }
    void SetAdvancedMovementInput(uint input, bool active) { }
    void StartEnergyDrain(uint kind, float drainPerSecond) { }
    void StopEnergyDrain(uint kind, float drainPerSecond, bool restoresDefaultRate) { }
    void ApplyForce(float x, float y, float z) { }
}

public static class ContentFunctionConditions
{
    private static readonly uint[] AdvancedMovementInputBits = [2, 4, 8, 16, 128, 256];

    public static bool LocaleMatches(uint localeKind, byte localeState) => localeKind switch
    {
        0 => localeState <= 3,
        1 => localeState == 5,
        2 => localeState == 4,
        3 => localeState is 13 or 14,
        4 => localeState is 6 or 9,
        5 => localeState is >= 10 and <= 12,
        6 => localeState == 7,
        7 => localeState == 8,
        8 => true,
        _ => false,
    };

    public static bool Compare(float current, ContentFunctionComparison comparison, float value) => comparison switch
    {
        ContentFunctionComparison.Equal => current == value,
        ContentFunctionComparison.Greater => current > value,
        ContentFunctionComparison.GreaterOrEqual => current >= value,
        ContentFunctionComparison.Less => value > current,
        ContentFunctionComparison.LessOrEqual => value >= current,
        _ => false,
    };

    public static bool Evaluate(ContentFunctionCondition condition, IContentFunctionHost host, uint stackInputId, bool stackInputReleased)
    {
        bool Nested(ContentFunctionCondition? nested) => nested is not null && Evaluate(nested, host, stackInputId, stackInputReleased);
        var holds = condition.Type switch
        {
            ContentFunctionConditionType.HasClientInitiative => host.HasClientInitiative,
            ContentFunctionConditionType.HasEndurance => host.Endurance >= condition.Value,
            ContentFunctionConditionType.HasEndurancePercent => host.MaximumEndurance > 0 && host.Endurance / host.MaximumEndurance >= condition.Value * 0.01f,
            ContentFunctionConditionType.HasEnergy => host.Energy(condition.Selector) >= condition.Value,
            ContentFunctionConditionType.Locale => LocaleMatches(condition.LocaleKind, host.LocaleState),
            ContentFunctionConditionType.MovementParameterCompare => Compare(host.MovementParameter(condition.Selector), condition.Comparison, condition.Value),
            ContentFunctionConditionType.PlayerIsUsingAdvancedMovementInput =>
                condition.Selector < AdvancedMovementInputBits.Length && (host.AdvancedMovementInputFlags & AdvancedMovementInputBits[condition.Selector]) != 0,
            ContentFunctionConditionType.StackInputEquals => stackInputId == condition.Selector,
            ContentFunctionConditionType.StackInputReleased => stackInputReleased,
            ContentFunctionConditionType.HasBuff => host.HasBuff(condition.BuffDataId, condition.BuffOnSelf),
            ContentFunctionConditionType.BuffCountCompare => Compare(host.BuffCount(condition.BuffDataId), condition.Comparison, condition.Count),
            ContentFunctionConditionType.HasMovementModifier => host.HasMovementModifier(condition.MovementModifierUid),
            ContentFunctionConditionType.SkillRechargeRemainingCompare =>
                Compare(host.SkillRechargeRemainingMilliseconds(condition.BuffDataId), condition.Comparison, condition.Count),
            ContentFunctionConditionType.IsMount => host.IsMount,
            ContentFunctionConditionType.And => Nested(condition.Left) && Nested(condition.Right),
            ContentFunctionConditionType.Or => Nested(condition.Left) || Nested(condition.Right),
            ContentFunctionConditionType.TargetWithinFacingAngle => host.TargetWithinFacingAngle(condition.Value),
            ContentFunctionConditionType.MapMode => host.MapMode == condition.Selector,
            ContentFunctionConditionType.MoverStateHasFlag => host.MoverStateHasFlag(condition.Selector),
            _ => false,
        };
        return holds != condition.Negated;
    }
}

public sealed class Gw2ContentFunctionRunner
{
    private readonly Gw2ContentFunctions _functions;
    private readonly Dictionary<uint, ContentFunctionBinding> _bindingByUid = [];
    private readonly List<Instance> _instances = [];
    private uint _lastInstanceId;

    public Gw2ContentFunctionRunner(Gw2ContentFunctions functions) => _functions = functions;

    public bool IsRunning => _instances.Count > 0;

    public void SyncBindings(IReadOnlyCollection<uint> activeBindingUids)
    {
        foreach (var removed in _bindingByUid.Keys.Where(uid => !activeBindingUids.Contains(uid)).ToArray()) _bindingByUid.Remove(removed);
        foreach (var added in activeBindingUids)
            if (!_bindingByUid.ContainsKey(added) && _functions.Binding(added) is { } binding) _bindingByUid[added] = binding;
    }

    public ContentFunctionBinding? BindingOfInput(uint inputId) =>
        _bindingByUid.Values.Where(binding => binding.InputId == inputId).OrderBy(binding => binding.Slot).Cast<ContentFunctionBinding?>().FirstOrDefault();

    public IReadOnlyList<ContentFunctionReport> PressInput(uint inputId, uint targetAgentId, long nowMilliseconds, IContentFunctionHost host)
    {
        if (BindingOfInput(inputId) is not { } binding || _functions.Program(binding.FunctionUid) is not { } program) return [];
        var instance = new Instance(++_lastInstanceId, program, inputId);
        var reports = new List<ContentFunctionReport>
        {
            new ContentFunctionInstanceStarted(instance.Id, targetAgentId, _functions.Layout.ServerBindingSource, binding.FunctionUid, inputId),
        };
        _instances.Add(instance);
        Step(instance, nowMilliseconds, host, reports);
        return reports;
    }

    public IReadOnlyList<ContentFunctionReport> ReleaseInput(uint inputId)
    {
        var reports = new List<ContentFunctionReport>();
        foreach (var instance in _instances.Where(running => running.InputId == inputId && !running.InputReleased))
        {
            instance.InputReleased = true;
            reports.Add(new ContentFunctionInputReleased(instance.RootFunctionUid));
        }
        return reports;
    }

    public IReadOnlyList<ContentFunctionReport> Tick(long nowMilliseconds, IContentFunctionHost host)
    {
        if (_instances.Count == 0) return [];
        var reports = new List<ContentFunctionReport>();
        foreach (var instance in _instances) Step(instance, nowMilliseconds, host, reports);
        _instances.RemoveAll(running => running.Frames.Count == 0);
        return reports;
    }

    private void Step(Instance instance, long now, IContentFunctionHost host, List<ContentFunctionReport> reports)
    {
        while (instance.Frames.Count > 0)
        {
            var frame = instance.Frames.Peek();
            if (frame.NodeIndex >= frame.Program.Nodes.Count)
            {
                instance.Frames.Pop();
                continue;
            }
            var node = frame.Program.Nodes[frame.NodeIndex];
            switch (node)
            {
                case ContentFunctionWaitNode wait:
                    frame.BlockedSince ??= now;
                    if (now - frame.BlockedSince < wait.Milliseconds) return;
                    break;
                case ContentFunctionWaitForConditionNode waitFor:
                    frame.BlockedSince ??= now;
                    var timedOut = waitFor.TimeoutMilliseconds > 0 && now - frame.BlockedSince >= waitFor.TimeoutMilliseconds;
                    if (!timedOut && !Evaluate(instance, waitFor.Condition, host)) return;
                    break;
                case ContentFunctionWhileLoopNode loop:
                {
                    if (loop.BodyFunctionUid is not { } body || (frame.Iteration >= loop.MinimumIterations && loop.Condition is { } loopCondition && !Evaluate(instance, loopCondition, host))) break;
                    frame.Iteration++;
                    if (loop.MaximumIterations != 0 && frame.Iteration >= loop.MaximumIterations) Advance(frame);
                    Push(instance, body);
                    continue;
                }
                case ContentFunctionAbortNode:
                    instance.Frames.Clear();
                    return;
                case ContentFunctionConditionalNode conditional:
                {
                    var result = Evaluate(instance, conditional.Condition, host);
                    reports.Add(new ContentFunctionConditionEvaluated(instance.Id, instance.RootFunctionUid, frame.Program.FunctionUid, node.Index, result));
                    Advance(frame);
                    if ((result ? conditional.ThenFunctionUid : conditional.ElseFunctionUid) is { } branch) Push(instance, branch);
                    continue;
                }
                case ContentFunctionRunStackNode runStack:
                    Advance(frame);
                    Push(instance, runStack.FunctionUid);
                    continue;
                case ContentFunctionApplyMovementModifierNode apply:
                    host.ApplyMovementModifier(apply.MovementModifierUid, apply.DurationMilliseconds);
                    break;
                case ContentFunctionApplyMovementModifierFromEnergyNode fueled:
                {
                    var duration = host.Energy(fueled.EnergyKind) * fueled.MillisecondsPerEnergy;
                    if (duration >= 0 && (int)duration != 0) host.ApplyMovementModifier(fueled.MovementModifierUid, (uint)(int)duration);
                    break;
                }
                case ContentFunctionEnduranceAdjustNode endurance:
                    host.AdjustEndurance(endurance.Amount);
                    break;
                case ContentFunctionRemoveMovementModifierNode remove:
                    host.RemoveMovementModifier(remove.MovementModifierUid);
                    break;
                case ContentFunctionEffectNode effect:
                    if (!RunEffect(instance, frame, effect, now, host, reports)) return;
                    if (effect.Kind == ContentFunctionOpcode.Abort)
                    {
                        instance.Frames.Clear();
                        return;
                    }
                    break;
            }
            Advance(frame);
        }
    }

    private const uint ActivateSkillWaitsForRelease = 1;
    private const uint ActivateSkillReportsSkill = 2;
    private const uint ActivateSkillReportsTarget = 4;

    private static bool RunEffect(Instance instance, Frame frame, ContentFunctionEffectNode effect, long now, IContentFunctionHost host, List<ContentFunctionReport> reports)
    {
        var started = frame.BlockedSince is null;
        frame.BlockedSince ??= now;
        var elapsed = now - frame.BlockedSince.Value;
        var input = effect.Word(ContentFunctionArgument.Input);
        var kind = effect.Word(ContentFunctionArgument.EnergyKind);
        var duration = effect.Word(ContentFunctionArgument.Duration);
        var timedOut = duration != 0 && elapsed >= duration;
        switch (effect.Kind)
        {
            case ContentFunctionOpcode.ActivateSkill or ContentFunctionOpcode.ActivateSkillAlternate:
            {
                var flags = effect.Word(ContentFunctionArgument.Flags);
                if (started)
                {
                    host.Perform(effect);
                    if ((flags & (ActivateSkillReportsSkill | ActivateSkillReportsTarget)) != 0)
                        reports.Add(new ContentFunctionSkillActivated(frame.Program.FunctionUid, effect.Index,
                            (flags & ActivateSkillReportsSkill) != 0 ? effect.Uid(ContentFunctionArgument.Skill) : null));
                }
                return (flags & ActivateSkillWaitsForRelease) == 0 || instance.InputReleased;
            }
            case ContentFunctionOpcode.ApplyMovementModifierAndDrain:
                if (started)
                {
                    if (effect.Uid(ContentFunctionArgument.MovementModifier) is { } drained) host.ApplyMovementModifier(drained, null);
                    host.StartEnergyDrain(kind, 0);
                }
                if (!timedOut && host.Energy(kind) > 0 && !instance.InputReleased) return false;
                if (effect.Uid(ContentFunctionArgument.MovementModifier) is { } finished) host.RemoveMovementModifier(finished);
                host.StopEnergyDrain(kind, 0, effect.Word(ContentFunctionArgument.UseRateSet) != 0);
                return true;
            case ContentFunctionOpcode.SimulateAdvancedMovementInputBegin:
                host.SetAdvancedMovementInput(input, true);
                return true;
            case ContentFunctionOpcode.SimulateAdvancedMovementInputEnd:
                host.SetAdvancedMovementInput(input, false);
                return true;
            case ContentFunctionOpcode.SimulateAdvancedMovementInputForDuration:
                if (started) host.SetAdvancedMovementInput(input, true);
                if (elapsed < duration) return false;
                host.SetAdvancedMovementInput(input, false);
                return true;
            case ContentFunctionOpcode.SimulateAdvancedMovementInputWhileCondition:
                if (started) host.SetAdvancedMovementInput(input, true);
                if (!timedOut && Holds(instance, effect.Condition(ContentFunctionArgument.Condition), host)) return false;
                host.SetAdvancedMovementInput(input, false);
                return true;
            case ContentFunctionOpcode.JumpImpulseByCharge:
                if (started)
                {
                    if (host.Energy(kind) <= 0) return true;
                    host.StartEnergyDrain(kind, 0);
                }
                if (!instance.InputReleased && host.Energy(kind) > 0) return false;
                host.Perform(effect);
                host.StopEnergyDrain(kind, 0, false);
                return true;
            case ContentFunctionOpcode.ApplyForceWhileConditionAndDrain:
            {
                var x = effect.Single(ContentFunctionArgument.ForceX);
                var y = effect.Single(ContentFunctionArgument.ForceY);
                var z = effect.Single(ContentFunctionArgument.ForceZ);
                var ignoresEnergy = effect.Word(ContentFunctionArgument.IgnoreEnergy) != 0;
                if (started)
                {
                    host.ApplyForce(x, y, z);
                    host.StartEnergyDrain(kind, effect.Single(ContentFunctionArgument.Drain));
                }
                if (!timedOut && Holds(instance, effect.Condition(ContentFunctionArgument.Condition), host) && (ignoresEnergy || host.Energy(kind) > 0)) return false;
                host.ApplyForce(-x, -y, -z);
                host.StopEnergyDrain(kind, effect.Single(ContentFunctionArgument.Drain), effect.Word(ContentFunctionArgument.UseRateSet) != 0);
                return true;
            }
            case ContentFunctionOpcode.ApplyMovementModifierAtCurrentPosition:
                if (effect.Uid(ContentFunctionArgument.MovementModifier) is { } positioned) host.ApplyMovementModifier(positioned, null);
                host.Perform(effect);
                return true;
            case ContentFunctionOpcode.EnduranceAdjustPercent:
                host.AdjustEndurance((int)(host.MaximumEndurance * effect.Single(ContentFunctionArgument.Amount) * 0.01f));
                return true;
            default:
                host.Perform(effect);
                return true;
        }
    }

    private static bool Holds(Instance instance, ContentFunctionCondition? condition, IContentFunctionHost host) =>
        condition is not null && ContentFunctionConditions.Evaluate(condition, host, instance.InputId, instance.InputReleased);

    private void Push(Instance instance, uint functionUid)
    {
        if (_functions.Program(functionUid) is { } program) instance.Frames.Push(new Frame(program));
    }

    private static void Advance(Frame frame)
    {
        frame.NodeIndex++;
        frame.BlockedSince = null;
        frame.Iteration = 0;
    }

    private static bool Evaluate(Instance instance, ContentFunctionCondition condition, IContentFunctionHost host) =>
        ContentFunctionConditions.Evaluate(condition, host, instance.InputId, instance.InputReleased);

    private sealed class Instance
    {
        public Instance(uint id, ContentFunctionProgram program, uint inputId)
        {
            Id = id;
            InputId = inputId;
            RootFunctionUid = program.FunctionUid;
            Frames.Push(new Frame(program));
        }

        public uint Id { get; }

        public uint RootFunctionUid { get; }

        public uint InputId { get; }

        public bool InputReleased { get; set; }

        public Stack<Frame> Frames { get; } = new();
    }

    private sealed class Frame(ContentFunctionProgram program)
    {
        public ContentFunctionProgram Program { get; } = program;

        public int NodeIndex { get; set; }

        public long? BlockedSince { get; set; }

        public uint Iteration { get; set; }
    }
}
