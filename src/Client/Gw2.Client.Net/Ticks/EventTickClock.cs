namespace Gw2.Client.Net.Ticks;

public readonly record struct EventTickTime(uint Milliseconds, ulong FileTime);

public readonly record struct EventTickStep(uint Milliseconds, ulong FileTime, uint AdvancedMilliseconds);

public sealed class EventTickClock
{
    public const uint MaximumStepMilliseconds = 500;

    private readonly uint _backlogLimit;
    private uint _previousMilliseconds;
    private uint _backlogMilliseconds;

    public EventTickClock(uint startMilliseconds, bool limitsBacklog)
    {
        _previousMilliseconds = startMilliseconds;
        _backlogLimit = limitsBacklog ? MaximumStepMilliseconds : uint.MaxValue;
    }

    public uint BacklogMilliseconds => _backlogMilliseconds;

    public EventTickStep Advance(EventTickTime now)
    {
        var elapsed = (int)(now.Milliseconds - _previousMilliseconds);
        _previousMilliseconds = now.Milliseconds;
        var elapsedMilliseconds = elapsed <= 0 ? 0u : (uint)elapsed;

        var step = elapsedMilliseconds < MaximumStepMilliseconds ? elapsedMilliseconds : MaximumStepMilliseconds;
        var overflow = elapsedMilliseconds - step;
        var addedBacklog = overflow < _backlogLimit ? overflow : _backlogLimit;
        var pending = unchecked(addedBacklog + _backlogMilliseconds);
        var room = MaximumStepMilliseconds - step;
        var drained = pending >= room ? room : pending;
        _backlogMilliseconds = pending - drained;
        return new EventTickStep(now.Milliseconds, now.FileTime, step + drained);
    }
}
