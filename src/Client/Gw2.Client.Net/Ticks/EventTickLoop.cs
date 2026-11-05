using System;
using System.Collections.Generic;

namespace Gw2.Client.Net.Ticks;

public sealed class EventTickLoop
{
    private readonly IEventTickTimeSource _timeSource;
    private readonly List<ProtocolConnection> _connections = new List<ProtocolConnection>();
    private EventTickClock? _clock;
    private readonly bool _limitsBacklog;

    public EventTickLoop(IEventTickTimeSource timeSource, bool limitsBacklog)
    {
        _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));
        _limitsBacklog = limitsBacklog;
    }

    public event Action<EventTickStep>? Ticked;

    public long TickCount { get; private set; }

    public void Attach(ProtocolConnection connection) => _connections.Add(connection ?? throw new ArgumentNullException(nameof(connection)));

    public void Detach(ProtocolConnection connection) => _connections.Remove(connection);

    public bool RunOnce()
    {
        if (!BeginTick()) return false;
        EndTick();
        return true;
    }

    public bool BeginTick()
    {
        if (!_timeSource.TryRead(out EventTickTime now)) return false;
        _clock ??= new EventTickClock(now.Milliseconds, _limitsBacklog);
        Ticked?.Invoke(_clock.Advance(now));
        return true;
    }

    public void EndTick()
    {
        foreach (var connection in _connections) connection.EndFrame();
        TickCount++;
    }
}
