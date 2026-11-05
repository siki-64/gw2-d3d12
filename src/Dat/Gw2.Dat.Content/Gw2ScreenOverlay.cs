using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2ScreenEffects(float? Glow, float? Distortion, Gw2EnvironmentGrade? Grade)
{
    public static Gw2ScreenEffects None { get; } = new(null, null, null);

    public static Gw2EnvironmentGrade GradeOf(float tintAmount, uint tintColourBgr, uint tintTargetColourBgr, float tintFocus, float saturation) =>
        new(saturation, tintAmount, Gw2ScreenOverlay.BgrColor(tintColourBgr), Gw2ScreenOverlay.BgrColor(tintTargetColourBgr), tintFocus);
}

public sealed class Gw2ScreenOverlay
{
    private const float EffectFadeOutSeconds = 0.25f;

    private const float SettledAmount = 1f / 512f;
    private const float GroupSpringSeconds = 1f;

    public static Gw2EnvironmentGrade Downed { get; } = new(0.75f, 0.9f, BgrColor(0xFF, 0xF4, 0xE7), BgrColor(0xFF, 0xF4, 0xE7), 0f);

    private readonly Spring _amount = new(1f);
    private readonly Spring[] _grade = [new(GroupSpringSeconds), new(GroupSpringSeconds), new(GroupSpringSeconds)];
    private readonly Spring _glowColor = new(GroupSpringSeconds);
    private readonly Spring _glowAlpha = new(GroupSpringSeconds);
    private readonly Spring _distortion = new(GroupSpringSeconds);
    private bool _hasGrade;
    private bool _hasGlow;
    private bool _hasDistortion;
    private bool _effectsFadedIn;

    public float Amount => _amount.Current.X;

    public bool IsActive => _hasGrade || _hasGlow || _hasDistortion;

    public void FadeIn(float seconds)
    {
        if (seconds == 0f)
        {
            _amount.Snap(Vector3.One);
            return;
        }
        _amount.Seconds = seconds;
        _amount.Target = Vector3.One;
    }

    public void FadeOut(float seconds)
    {
        if (seconds == 0f)
        {
            _amount.Snap(Vector3.Zero);
            return;
        }
        _amount.Seconds = seconds;
        _amount.Target = Vector3.Zero;
    }

    public void ApplyEffects(Gw2ScreenEffects effects, Gw2EnvironmentGrade baseGrade, Vector4 baseGlow)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (effects.Glow is { } glow) SetGlow(glow, baseGlow);
        if (effects.Distortion is { } distortion) SetDistortion(distortion);
        if (effects.Grade is { } grade) SetGrade(grade, baseGrade);
        var anyActive = effects.Glow is not null || effects.Distortion is not null || effects.Grade is not null;
        if (anyActive && !_effectsFadedIn)
        {
            FadeIn(0f);
            _effectsFadedIn = true;
        }
        else if (!anyActive && _effectsFadedIn)
        {
            FadeOut(EffectFadeOutSeconds);
            _effectsFadedIn = false;
        }
    }

    public void SetGrade(Gw2EnvironmentGrade target, Gw2EnvironmentGrade baseGrade)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(baseGrade);
        if (!_hasGrade)
        {
            _grade[0].Snap(Scalars(baseGrade));
            _grade[1].Snap(baseGrade.TintColor);
            _grade[2].Snap(baseGrade.TintTargetColor);
            _hasGrade = true;
        }
        _grade[0].Target = Scalars(target);
        _grade[1].Target = target.TintColor;
        _grade[2].Target = target.TintTargetColor;
    }

    public void SetGlow(float target, Vector4 baseGlow)
    {
        if (!_hasGlow)
        {
            _glowColor.Snap(new Vector3(baseGlow.X, baseGlow.Y, baseGlow.Z));
            _glowAlpha.Snap(new Vector3(baseGlow.W));
            _hasGlow = true;
        }
        _glowColor.Target = new Vector3(target);
        _glowAlpha.Target = new Vector3(target);
    }

    public void SetDistortion(float target)
    {
        if (!_hasDistortion)
        {
            _distortion.Snap(Vector3.Zero);
            _hasDistortion = true;
        }
        _distortion.Target = new Vector3(target);
    }

    public void Advance(float elapsedSeconds)
    {
        _amount.Step(elapsedSeconds);
        if (_amount.Current.X <= SettledAmount && _amount.Target.X <= 0f)
        {
            _hasGrade = false;
            _hasGlow = false;
            _hasDistortion = false;
            return;
        }
        if (_hasDistortion) _distortion.Step(elapsedSeconds);
        if (_hasGlow)
        {
            _glowColor.Step(elapsedSeconds);
            _glowAlpha.Step(elapsedSeconds);
        }
        if (_hasGrade)
            foreach (var group in _grade) group.Step(elapsedSeconds);
    }

    public Gw2EnvironmentGrade Apply(Gw2EnvironmentGrade baseGrade)
    {
        ArgumentNullException.ThrowIfNull(baseGrade);
        if (!_hasGrade) return baseGrade;
        var amount = _amount.Current.X;
        var scalars = Vector3.Lerp(Scalars(baseGrade), _grade[0].Current, amount);
        return new(
            scalars.X,
            scalars.Y,
            Vector3.Lerp(baseGrade.TintColor, _grade[1].Current, amount),
            Vector3.Lerp(baseGrade.TintTargetColor, _grade[2].Current, amount),
            scalars.Z);
    }

    public Vector4 ApplyGlow(Vector4 baseGlow)
    {
        if (!_hasGlow) return baseGlow;
        var glow = new Vector4(_glowColor.Current, _glowAlpha.Current.X);
        return Vector4.Lerp(baseGlow, glow, _amount.Current.X);
    }

    public float ApplyDistortion(float baseDistortion) =>
        _hasDistortion ? (_distortion.Current.X - baseDistortion) * _amount.Current.X + baseDistortion : baseDistortion;

    private static Vector3 Scalars(Gw2EnvironmentGrade grade) => new(grade.Saturation, grade.TintAmount, grade.TintFocus);

    private static Vector3 BgrColor(byte blue, byte green, byte red) => new Vector3(red, green, blue) / 255f;

    internal static Vector3 BgrColor(uint packedBgr) => BgrColor((byte)packedBgr, (byte)(packedBgr >> 8), (byte)(packedBgr >> 16));

    private sealed class Spring(float seconds)
    {
        public float Seconds { get; set; } = seconds;

        public Vector3 Current { get; private set; }

        public Vector3 Target { get; set; }

        private Vector3 _velocity;

        public void Snap(Vector3 value)
        {
            Current = value;
            Target = value;
            _velocity = Vector3.Zero;
        }

        public void Step(float elapsedSeconds)
        {
            var stiffness = 2f / Seconds;
            var scaledStep = stiffness * elapsedSeconds;
            var damping = 1f / (((scaledStep * (1f / 6f) + 0.5f) * scaledStep + 1f) * scaledStep + 1f);
            var offset = Current - Target;
            var change = (offset * stiffness + _velocity) * elapsedSeconds;
            _velocity = (_velocity - change * stiffness) * damping;
            Current = (change + offset) * damping + Target;
        }
    }
}
