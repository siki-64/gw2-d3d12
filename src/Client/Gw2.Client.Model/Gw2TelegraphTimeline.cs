namespace Gw2.Client.Model;

public enum Gw2TelegraphClip
{
    Hidden,
    Start,
    Loop,
    End,
}

public sealed class Gw2TelegraphTimeline
{
    private readonly double _startSeconds;
    private readonly double _endSeconds;
    private double _shownAtSeconds;
    private double? _endedAtSeconds;
    private bool _isShown;

    public Gw2TelegraphTimeline(double startSeconds, double endSeconds)
    {
        _startSeconds = startSeconds;
        _endSeconds = endSeconds;
    }

    public void Show(double nowSeconds)
    {
        _shownAtSeconds = nowSeconds;
        _endedAtSeconds = null;
        _isShown = true;
    }

    public void End(double nowSeconds)
    {
        if (!_isShown || _endedAtSeconds.HasValue) return;
        _endedAtSeconds = Math.Max(nowSeconds, _shownAtSeconds + _startSeconds);
    }

    public Gw2TelegraphClip ClipAt(double nowSeconds)
    {
        if (!_isShown || nowSeconds < _shownAtSeconds) return Gw2TelegraphClip.Hidden;
        if (_endedAtSeconds is { } endedAt && nowSeconds >= endedAt)
            return nowSeconds - endedAt < _endSeconds ? Gw2TelegraphClip.End : Gw2TelegraphClip.Hidden;
        return nowSeconds - _shownAtSeconds < _startSeconds ? Gw2TelegraphClip.Start : Gw2TelegraphClip.Loop;
    }

    public float DepthFadeAt(double nowSeconds) => ClipAt(nowSeconds) switch
    {
        Gw2TelegraphClip.Start => _startSeconds > 0.0 ? (float)Math.Clamp((nowSeconds - _shownAtSeconds) / _startSeconds, 0.0, 1.0) : 1f,
        Gw2TelegraphClip.Loop => 1f,
        Gw2TelegraphClip.End => 1f,
        _ => 0f,
    };
}
