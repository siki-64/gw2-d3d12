using Gw2.Dat.Content;

namespace Gw2.Client.Model;

public enum Gw2CastSegmentMode
{
    Waiting,
    Casting,
    Completed,
    Interrupted,
}

public readonly record struct Gw2CastSegmentView(float LayoutStart, float LayoutEnd, int Style, Gw2CastSegmentMode Mode, float ModeSeconds, float Fill);

public sealed class Gw2CastProgress
{
    public const double CompletionFlashSeconds = 0.15;
    public const double CompletionFadeSeconds = 1.5;
    public const double InterruptionFillFadeSeconds = 0.7;
    public const double InterruptionGlowFadeSeconds = 0.4;
    public const double FadeInSeconds = 0.25;
    public const double ChargeFadeInSeconds = 0.15;
    public const double FadeOutDelaySeconds = 0.15;
    public const double FadeOutSeconds = 0.4;
    public const int WarmupStyle = 5;
    public const int ChannelStyle = 0;
    private static readonly int[] ChargeStageStyles = [1, 2, 3, 4];

    private readonly List<Segment> _segments = [];
    private IReadOnlyList<Gw2SkillChargeStage> _chargeStages = [];
    private Gw2SkillProgressWidget _widget;
    private double _beginSeconds;
    private double _firstExecuteSeconds;
    private double _lastExecuteSeconds;
    private double? _fadeOutStartSeconds;
    private int _chargeStageIndex;

    public int? CastingSlot { get; private set; }

    public uint? SkillId { get; private set; }

    public void Begin(int slot, uint skillId, Gw2SkillCastTiming timing, double nowSeconds)
    {
        _segments.Clear();
        _widget = timing.Widget;
        _beginSeconds = nowSeconds;
        _firstExecuteSeconds = timing.FirstExecuteSeconds;
        _lastExecuteSeconds = timing.LastExecuteSeconds;
        _chargeStages = timing.ChargeStages;
        _chargeStageIndex = 0;
        _fadeOutStartSeconds = null;
        CastingSlot = slot;
        SkillId = skillId;
        switch (timing.Widget)
        {
            case Gw2SkillProgressWidget.Warmup:
                _segments.Add(new Segment(0f, 1f, WarmupStyle, timing.FirstExecuteSeconds).Cast(nowSeconds));
                break;
            case Gw2SkillProgressWidget.Channel:
                var split = (float)(timing.FirstExecuteSeconds / timing.LastExecuteSeconds);
                if (timing.FirstExecuteSeconds > 0.0) _segments.Add(new Segment(0f, split, WarmupStyle, timing.FirstExecuteSeconds).Cast(nowSeconds));
                var channel = new Segment(split, 1f, ChannelStyle, timing.LastExecuteSeconds - timing.FirstExecuteSeconds);
                _segments.Add(timing.FirstExecuteSeconds > 0.0 ? channel : channel.Cast(nowSeconds));
                break;
            case Gw2SkillProgressWidget.Charge:
                var total = timing.ChargeStages.Sum(stage => stage.Seconds);
                var start = 0.0;
                for (var index = 0; index < timing.ChargeStages.Count; index++)
                {
                    var stage = timing.ChargeStages[index];
                    var segment = new Segment((float)(start / total), (float)((start + stage.Seconds) / total), ChargeStageStyles[Math.Min(index, ChargeStageStyles.Length - 1)], stage.Seconds);
                    _segments.Add(index == 0 ? segment.Cast(nowSeconds) : segment);
                    start += stage.Seconds;
                }
                break;
        }
    }

    public void Update(double nowSeconds)
    {
        if (_fadeOutStartSeconds is not null) return;
        var elapsed = nowSeconds - _beginSeconds;
        switch (_widget)
        {
            case Gw2SkillProgressWidget.Warmup when elapsed >= _firstExecuteSeconds && _segments[0].Mode == Gw2CastSegmentMode.Casting:
                var executeSeconds = _beginSeconds + _firstExecuteSeconds;
                _segments[0].Complete(executeSeconds);
                FadeOut(executeSeconds);
                break;
            case Gw2SkillProgressWidget.Channel:
                var channel = _segments[^1];
                if (elapsed >= _firstExecuteSeconds && channel.Mode == Gw2CastSegmentMode.Waiting)
                {
                    var firstExecute = _beginSeconds + _firstExecuteSeconds;
                    _segments[0].Complete(firstExecute);
                    channel.Cast(firstExecute);
                }
                if (elapsed >= _lastExecuteSeconds && channel.Mode == Gw2CastSegmentMode.Casting) channel.Complete(_beginSeconds + _lastExecuteSeconds);
                break;
        }
    }

    public void ReachChargeLevel(byte level, double nowSeconds)
    {
        if (_widget != Gw2SkillProgressWidget.Charge || _fadeOutStartSeconds is not null) return;
        var reached = 0;
        while (reached < _chargeStages.Count && level >= _chargeStages[reached].EndingChargeLevel) reached++;
        for (; _chargeStageIndex < reached; _chargeStageIndex++)
        {
            _segments[_chargeStageIndex].Complete(nowSeconds);
            if (_chargeStageIndex + 1 < _segments.Count) _segments[_chargeStageIndex + 1].Cast(nowSeconds);
        }
    }

    public void Stop(double nowSeconds)
    {
        if (_segments.Count == 0 || _fadeOutStartSeconds is not null) return;
        if (_widget == Gw2SkillProgressWidget.Warmup) _segments[0].Interrupt(nowSeconds);
        FadeOut(nowSeconds);
    }

    public bool IsVisibleAt(double nowSeconds) =>
        _segments.Count > 0 && (_fadeOutStartSeconds is not { } fadeStart || nowSeconds - fadeStart < FadeOutDelaySeconds + FadeOutSeconds);

    public float OpacityAt(double nowSeconds)
    {
        if (!IsVisibleAt(nowSeconds)) return 0f;
        var fadeIn = Math.Clamp((nowSeconds - _beginSeconds) / (_widget == Gw2SkillProgressWidget.Charge ? ChargeFadeInSeconds : FadeInSeconds), 0.0, 1.0);
        if (_fadeOutStartSeconds is not { } fadeStart) return (float)fadeIn;
        return (float)Math.Min(fadeIn, Math.Clamp(1.0 - (nowSeconds - fadeStart - FadeOutDelaySeconds) / FadeOutSeconds, 0.0, 1.0));
    }

    public IReadOnlyList<Gw2CastSegmentView> SegmentsAt(double nowSeconds) =>
        [.. _segments.Select(segment => segment.ViewAt(nowSeconds))];

    private void FadeOut(double nowSeconds)
    {
        _fadeOutStartSeconds = nowSeconds;
        CastingSlot = null;
    }

    private sealed class Segment(float layoutStart, float layoutEnd, int style, double seconds)
    {
        private double _modeStartSeconds;
        private float _fillAtEnd;

        public Gw2CastSegmentMode Mode { get; private set; } = Gw2CastSegmentMode.Waiting;

        public Segment Cast(double nowSeconds) => Enter(Gw2CastSegmentMode.Casting, nowSeconds, 0f);

        public void Complete(double nowSeconds) => Enter(Gw2CastSegmentMode.Completed, nowSeconds, 1f);

        public void Interrupt(double nowSeconds)
        {
            if (Mode == Gw2CastSegmentMode.Casting) Enter(Gw2CastSegmentMode.Interrupted, nowSeconds, FillAt(nowSeconds));
        }

        public Gw2CastSegmentView ViewAt(double nowSeconds) =>
            new(layoutStart, layoutEnd, style, Mode, (float)Math.Max(0.0, nowSeconds - _modeStartSeconds), FillAt(nowSeconds));

        private float FillAt(double nowSeconds) => Mode switch
        {
            Gw2CastSegmentMode.Casting => seconds <= 0.0 ? 1f : (float)Math.Clamp((nowSeconds - _modeStartSeconds) / seconds, 0.0, 1.0),
            _ => _fillAtEnd,
        };

        private Segment Enter(Gw2CastSegmentMode mode, double nowSeconds, float fillAtEnd)
        {
            Mode = mode;
            _modeStartSeconds = nowSeconds;
            _fillAtEnd = fillAtEnd;
            return this;
        }
    }
}
