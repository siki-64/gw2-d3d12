using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Net.Transport;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Game;

public sealed record GameIdentity(string CharacterName, string AccountLoginName, string Provider, byte TextLanguage, byte AudioLanguage, DeviceReport Device);

public sealed class GameSession : IDisposable
{
    private const uint ContextMapIdMask = 0x3fffffff;
    private const int MapDataContextMapIdField = 2;
    private const int LoadMapMapIdField = 4;
    private const int LoadMapSpawnPositionField = 1;
    private const int PlayerIdField = 1;
    private const int PlayerNameField = 2;
    private const int ControlledPlayerField = 2;
    private const int ControlledAgentField = 3;

    private readonly SocketTransport? _transport;
    private readonly GameIdentity _identity;
    private readonly ConcurrentQueue<DecodedMessage> _received = new();
    private readonly ConcurrentQueue<(int MessageId, object[] Values)> _queuedOutbound = new();
    private volatile bool _mapLoadedReported;
    private bool _mapLoadedSent;
    private bool _mapDataAnswered;
    private bool _agentsLoadedSent;
    private readonly OwnCharacterControl _ownControl = new();
    private readonly object _ownPositionGate = new();
    private Vector3? _ownPositionInGameUnits;
    private OwnMovementInput _ownMovementInput;
    private float _ownSpeedInGameUnits;
    private readonly OwnCharacterMovement _ownMovement = new();
    private long _stepClockStartTimestamp;
    private long _ownPositionReportedTimestamp;
    private static readonly TimeSpan LongestPositionCarry = TimeSpan.FromMilliseconds(100);
    private long _lastAgentCommandDelayStep = long.MinValue;
    private volatile int _framesLastSecond;
    private int _sentOwnStates;
    private const int StepsBetweenAgentCommandDelays = 12;
    private const uint QueuedCommandDelayMilliseconds = 0;
    private const uint FallbackFrameIntervalMilliseconds = 40;
    private const int OwnStatesPerTrafficLine = 25;
    private const int AgentIdField = 1;

    public GameSession(SocketTransport? transport, ProtocolConnection connection, GameIdentity identity)
    {
        _transport = transport;
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public ProtocolConnection Connection { get; }

    public event Action<string>? Traffic;

    public bool Closed => _transport?.Closed ?? false;

    public uint? ContextMapId { get; private set; }

    public uint? LoadMapId { get; private set; }

    public System.Numerics.Vector3? LoadMapSpawnPositionInGameUnits { get; private set; }

    public uint? OwnPlayerId { get; private set; }

    public uint? ControlledAgentId { get; private set; }

    public bool BeganPlaying { get; private set; }

    public void ReceivePending()
    {
        if (_transport is null) return;
        while (_transport.TryTakeReceived(out var bytes)) Receive(bytes);
        SendQueuedMessages();
        SendMapLoadedWhenReported();
        SendOwnCharacterStateOnNewStep();
    }

    public void ReportFramesLastSecond(int frames) => _framesLastSecond = frames;

    public void QueueMessage(int messageId, object[] values) => _queuedOutbound.Enqueue((messageId, values));

    private void SendQueuedMessages()
    {
        while (_queuedOutbound.TryDequeue(out var queued))
        {
            Connection.SendValues(queued.MessageId, queued.Values);
            Traffic?.Invoke($"C>S 0x{queued.MessageId:x} {string.Join(" ", queued.Values)}");
        }
    }

    public void ReportOwnPosition(Vector3 positionInGameUnits, OwnMovementInput movementInGameUnits, float speedInGameUnits)
    {
        lock (_ownPositionGate)
        {
            _ownPositionInGameUnits = positionInGameUnits;
            _ownMovementInput = movementInGameUnits;
            _ownSpeedInGameUnits = speedInGameUnits;
            _ownPositionReportedTimestamp = Stopwatch.GetTimestamp();
        }
    }

    private void SendOwnCharacterStateOnNewStep()
    {
        if (_stepClockStartTimestamp == 0) return;
        Vector3? position;
        OwnMovementInput movement;
        float speed;
        lock (_ownPositionGate)
        {
            position = _ownPositionInGameUnits;
            movement = _ownMovementInput;
            speed = _ownSpeedInGameUnits;
            if (position is { } reported && movement.HeldMovementKeys != 0)
            {
                var sinceReported = Stopwatch.GetElapsedTime(_ownPositionReportedTimestamp);
                if (sinceReported > LongestPositionCarry) sinceReported = LongestPositionCarry;
                position = reported + movement.Direction * speed * (float)sinceReported.TotalSeconds;
            }
        }
        if (position is not { } gameUnits) return;
        var step = Stopwatch.GetElapsedTime(_stepClockStartTimestamp).Ticks / OwnCharacterSteps.StepLength.Ticks;
        foreach (var (messageId, values) in _ownControl.MessagesForStep(step, OwnCharacterSteps.CollisionUnitsOf(gameUnits), movement.IsAirborne, movement.Physics))
        {
            Connection.SendValues(messageId, values);
            if (messageId != GameMessageIds.OwnCharacterState) Traffic?.Invoke($"C>S 0x{messageId:x} step {step}");
            else if (_sentOwnStates++ % OwnStatesPerTrafficLine == 0) Traffic?.Invoke($"C>S 0x12 step {step} at {gameUnits.X:F0},{gameUnits.Y:F0},{gameUnits.Z:F0} ({_sentOwnStates} sent)");
        }
        if (_ownControl.ControlledAgentId is not { } agentId) return;

        var elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(_stepClockStartTimestamp).TotalMilliseconds;
        foreach (var command in _ownMovement.CommandsToSend(agentId, step, elapsedMilliseconds, movement, OwnCharacterSteps.CollisionUnitsOf(gameUnits), speed / GameAgents.GameUnitsPerCollisionUnit))
        {
            var commandId = (int)(ulong)command[0].Value!;
            Connection.Send(commandId, command);
            Traffic?.Invoke($"C>S 0x{commandId:x2} step {step} keys {movement.HeldMovementKeys} at {gameUnits.X:F1},{gameUnits.Y:F1},{gameUnits.Z:F1} ms {elapsedMilliseconds} {DescribeFields(command)}");
        }
    }

    private long CurrentStep => Stopwatch.GetElapsedTime(_stepClockStartTimestamp).Ticks / OwnCharacterSteps.StepLength.Ticks;

    private void AcknowledgeAgentCommandDelay()
    {
        if (_stepClockStartTimestamp == 0) return;
        var step = CurrentStep;
        if (step - _lastAgentCommandDelayStep <= StepsBetweenAgentCommandDelays) return;
        _lastAgentCommandDelayStep = step;
        Connection.SendValues(GameMessageIds.AgentCommandDelay, QueuedCommandDelayMilliseconds);
    }

    public void Receive(byte[] bytes)
    {
        foreach (var message in Connection.Receive(bytes))
        {
            var isOwnAgent = ControlledAgentId is { } own && message.Fields.Count > AgentIdField && message.Fields[AgentIdField].Value is ulong agentId && agentId == own;
            Traffic?.Invoke(isOwnAgent
                ? $"S>C 0x{message.MessageId:x} own agent {DescribeFields(message.Fields)}"
                : message.MessageId is GameAgents.AgentTransform or GameAgents.AgentStop
                    ? $"S>C 0x{message.MessageId:x} {DescribeFields(message.Fields)}"
                    : $"S>C 0x{message.MessageId:x}");
            AcknowledgeAgentCommandDelay();
            Handle(message);
            _ownControl.Handle(message);
            _received.Enqueue(message);
        }
    }

    public void ReportMapLoaded() => _mapLoadedReported = true;

    public void SendMapLoadedWhenReported()
    {
        if (!_mapLoadedReported || LoadMapId is null) return;
        if (!_mapLoadedSent)
        {
            _mapLoadedSent = true;
            Send(GameMessageIds.MapLoaded);
            _stepClockStartTimestamp = Stopwatch.GetTimestamp();
        }
        if (_agentsLoadedSent || ControlledAgentId is null) return;
        _agentsLoadedSent = true;
        Send(GameMessageIds.AgentsLoaded);
    }

    public bool TryTakeMessage(out DecodedMessage message) => _received.TryDequeue(out message!);

    public void Dispose() => _transport?.Dispose();

    private void Send(int messageId, params object[] values)
    {
        Traffic?.Invoke($"C>S 0x{messageId:x}");
        Connection.SendValues(messageId, values);
    }

    private static string DescribeFields(IReadOnlyList<DecodedField> fields) => string.Join(" ", Enumerable.Range(1, fields.Count - 1).Select(index => $"[{index}]={DescribeValue(fields[index].Value)}"));

    private static string DescribeValue(object? value) => value switch
    {
        byte[] bytes => Convert.ToHexString(bytes),
        DecodedField[] nested => "{" + string.Join(" ", nested.Select(field => DescribeValue(field.Value))) + "}",
        null => "-",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case GameMessageIds.FrameIntervalRequest:
                var frames = _framesLastSecond;
                Send(GameMessageIds.FrameInterval, frames > 0 ? (uint)(1000 / frames) : FallbackFrameIntervalMilliseconds);
                break;
            case GameMessageIds.MapData when !_mapDataAnswered:
                _mapDataAnswered = true;
                ContextMapId = (uint)(ulong)message.Fields[MapDataContextMapIdField].Value! & ContextMapIdMask;
                Send(GameMessageIds.DeviceReport, _identity.Device.Values());
                Send(GameMessageIds.AccountLogin, _identity.AccountLoginName, _identity.Provider, _identity.TextLanguage, _identity.AudioLanguage);
                Send(GameMessageIds.Languages, _identity.TextLanguage, _identity.AudioLanguage);
                break;
            case GameMessageIds.MapStateComplete:
                Send(GameMessageIds.MapStateAcknowledged);
                break;
            case GameMessageIds.PlayerCreate when OwnPlayerId is null && message.Fields[PlayerNameField].Value is string name && name == _identity.CharacterName:
                OwnPlayerId = (uint)(ulong)message.Fields[PlayerIdField].Value!;
                break;
            case GameMessageIds.PlayerControl when OwnPlayerId is { } player && (ulong)message.Fields[ControlledPlayerField].Value! == player:
                ControlledAgentId = (uint)(ulong)message.Fields[ControlledAgentField].Value!;
                break;
            case GameMessageIds.BeginPlaying:
                BeganPlaying = true;
                break;
            case GameMessageIds.LoadMap:
                LoadMapId = (uint)(ulong)message.Fields[LoadMapMapIdField].Value!;
                var spawn = (byte[])message.Fields[LoadMapSpawnPositionField].Value!;
                LoadMapSpawnPositionInGameUnits = new System.Numerics.Vector3(BitConverter.ToSingle(spawn, 0), BitConverter.ToSingle(spawn, 4), BitConverter.ToSingle(spawn, 8));
                break;
        }
    }
}
