using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Gw2.Client.Net.Game;
using Gw2.Client.Net.Portal;
using Gw2.Client.Net.Ticks;
using Gw2.Client.Net.Transport;
using Gw2.Formats;
using Gw2.Protocol.Portal;
using Gw2.Protocol.MsgPack;
using Gw2.Protocol.Schema;
using Gw2.Protocol.Session;

namespace Gw2.Client.Net.Login;

public enum LoginMethod
{
    Account,
    RememberedAccount,
    Steam,
}

public enum LoginStage
{
    Idle,
    RequestingSteamTicket,
    PortalLogin,
    WaitingForSecondFactor,
    ConnectingAuthServer,
    CharacterSelect,
    JoiningGame,
    InGame,
    LoggedOut,
    Failed,
}

public sealed record LoginRequest(LoginMethod Method, string Email, string Password, bool RemembersLogin, LoginServerSettings Servers);

public sealed class LoginFlow : IDisposable
{
    public const int ProgramId = 101;
    public const int PortalPort = 6112;
    public const uint ConnectHeaderKind = 0xC0400;
    public const uint ConnectHeaderVersion = 4;
    public const uint ConfigFlag = 1;
    public const uint ClientInfo = 0;
    public const ulong WrongDatacenter = 21;
    public const int AccountReplyMessageId = 0x4;
    public static readonly TimeSpan AccountReplyTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan TickSleep = TimeSpan.FromMilliseconds(10);
    public const byte EnglishLanguage = 0;

    private readonly string _executablePath;
    private readonly string _datPath;
    private readonly RememberedLoginStore _rememberedLogins;
    private LoginServers _servers = LoginServers.Retail;
    private readonly object _gate = new();
    private Task? _running;
    private volatile bool _logoutRequested;
    private TaskCompletionSource<string>? _secondFactorCode;
    private IReadOnlyList<CharacterSummary> _characters = [];
    private byte[]? _accountPreferencePack;
    private byte[]? _pendingPreferenceUpload;
    private (CharacterSummary Character, Func<ushort, uint> GameTypeOfMap, GameAdapter Adapter)? _pendingPlay;
    private GameAdapter? _playAdapter;
    private string? _playCharacterName;
    private Storefront _storefront;
    private readonly byte[] _debugSessionId = DebugSessionId.Create(DebugSessionId.MachineSeedOfThisMachine(), DateTime.UtcNow);
    private volatile GameSession? _game;
    private uint? _connectedDatacenter;

    public LoginFlow(string executablePath, string datPath, RememberedLoginStore rememberedLogins)
    {
        _executablePath = executablePath;
        _datPath = datPath;
        _rememberedLogins = rememberedLogins;
    }

    public bool CanLogInWithSteam()
    {
        try
        {
            return SteamAvailability.CanLogIn(SteamApiDeployment.FromExecutable(_executablePath).AppId);
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    public LoginStage Stage { get; private set; }

    public string Status { get; private set; } = "";

    public string? Error { get; private set; }

    public string? SecondFactorType { get; private set; }

    public byte[]? AccountPreferencePack
    {
        get { lock (_gate) return _accountPreferencePack; }
    }

    public IReadOnlyList<CharacterSummary> Characters
    {
        get { lock (_gate) return _characters; }
    }

    public bool IsBusy => Stage is LoginStage.RequestingSteamTicket or LoginStage.PortalLogin or LoginStage.WaitingForSecondFactor or LoginStage.ConnectingAuthServer or LoginStage.JoiningGame;

    public event Action<string>? Progress;

    public event Action<int>? MessageReceived;

    public event Action<string>? GameTraffic;

    public void Start(LoginRequest request)
    {
        if (_running is { IsCompleted: false }) throw new InvalidOperationException("a login is already running");
        _logoutRequested = false;
        Error = null;
        lock (_gate) _characters = [];
        _storefront = request.Method == LoginMethod.Steam ? Storefront.Steam : Storefront.None;
        _running = Task.Run(() => Run(request));
    }

    public void SubmitSecondFactor(string code) => _secondFactorCode?.TrySetResult(code);

    public void Logout() => _logoutRequested = true;

    public void UploadAccountPreferences(byte[] preferencePack)
    {
        lock (_gate)
        {
            _accountPreferencePack = preferencePack;
            _pendingPreferenceUpload = preferencePack;
        }
    }

    public void Play(CharacterSummary character, Func<ushort, uint> gameTypeOfMap, GameAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(gameTypeOfMap);
        ArgumentNullException.ThrowIfNull(adapter);
        lock (_gate) _pendingPlay = (character, gameTypeOfMap, adapter);
    }

    public uint? GameMapId => _game?.LoadMapId;

    public System.Numerics.Vector3? GameMapSpawnPositionInGameUnits => _game?.LoadMapSpawnPositionInGameUnits;

    public uint? ControlledAgentId => _game?.ControlledAgentId;

    public void ReportMapLoaded() => _game?.ReportMapLoaded();

    public void ReportFramesLastSecond(int frames) => _game?.ReportFramesLastSecond(frames);

    public void QueueGameMessage(int messageId, object[] values) => _game?.QueueMessage(messageId, values);

    public void ReportOwnPosition(System.Numerics.Vector3 positionInGameUnits, Game.OwnMovementInput movementInGameUnits, float speedInGameUnits) => _game?.ReportOwnPosition(positionInGameUnits, movementInGameUnits, speedInGameUnits);

    public bool TryTakeGameMessage(out DecodedMessage message)
    {
        message = null!;
        return _game?.TryTakeMessage(out message) ?? false;
    }

    public bool WaitForExit(TimeSpan timeout) => _running?.Wait(timeout) ?? true;

    public void Dispose()
    {
        Logout();
        _secondFactorCode?.TrySetCanceled();
        try { WaitForExit(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
    }

    private void Run(LoginRequest request)
    {
        try
        {
            _servers = request.Servers.Parse();
            var schemas = ExecutableProtocolSchemas.FromExecutable(_executablePath);
            if (_servers.Portal is null && _servers.AuthServer is null && !IsLiveBuild((uint)schemas.Build)) return;
            var group = HandshakeGroup.FromExecutable(_executablePath);
            if (_servers.AuthServerPublicValue is { } authServerPublicValue) group = group.WithServerPublicValue(authServerPublicValue);
            var credential = Credential(request);

            Enter(LoginStage.PortalLogin, "logging in to the portal");
            using var portal = new PortalLogin(PortalTrustAnchors.FromExecutable(_executablePath).Including(_servers.PortalCertificate), _servers.Portal?.Port ?? PortalPort, null);
            portal.AskSecondFactorCode = AskSecondFactorCode;
            var token = portal.Login(_servers.Portal?.Host ?? PortalLogin.PortalHostName(ProgramId), credential, Guid.NewGuid());
            Remember(request, token);

            var identity = new LoginIdentity(Environment.UserName, Dns.GetHostName(), (uint)schemas.Build, InstallIdentity.Current().ToByteArray(),
                ConfigFlag, 0, [], [], "", "", SystemRecord.FromThisMachine().ToBytes(), ClientInfo).WithPortalToken(token);
            foreach (uint datacenter in AuthDatacenters.TryOrder(_connectedDatacenter, Random.Shared))
            {
                ulong? result = RunAuthConnection(schemas, group, identity with { Datacenter = datacenter });
                if (result != WrongDatacenter)
                {
                    if (result is not null) _connectedDatacenter = datacenter;
                    break;
                }
                token = portal.RequestGameToken();
                identity = identity.WithPortalToken(token);
            }
            portal.Logout();
            if (Stage != LoginStage.Failed) Enter(LoginStage.LoggedOut, "logged out");
        }
        catch (Exception exception)
        {
            Fail(Describe(exception));
        }
    }

    private bool IsLiveBuild(uint executableBuild)
    {
        Enter(LoginStage.PortalLogin, "checking the live build");
        uint liveBuild = LiveBuild.FetchAsync(ProgramId, CancellationToken.None).GetAwaiter().GetResult().Build;
        if (executableBuild < liveBuild)
        {
            Fail($"{Path.GetFileName(_executablePath)} build {executableBuild} is behind the live build {liveBuild}; update the game");
            return false;
        }
        if (executableBuild > liveBuild) Progress?.Invoke($"{Path.GetFileName(_executablePath)} build {executableBuild} is ahead of the live build {liveBuild}");
        return true;
    }

    private PortalCredential Credential(LoginRequest request)
    {
        switch (request.Method)
        {
            case LoginMethod.Steam:
                Enter(LoginStage.RequestingSteamTicket, "requesting a Steam ticket");
                var deployment = SteamApiDeployment.FromExecutable(_executablePath);
                byte[] library;
                using (var archive = FileArchive.Open(_datPath, verifyChecksums: false)) library = deployment.ReadLibrary(archive);
                var steam = SteamAuthTicket.Request(SteamAuthTicket.DeployLibrary(library, deployment.FolderName, deployment.LibraryName), deployment.AppId);
                return PortalCredential.ForSteamTicket(steam.SteamId.ToString(), steam.Ticket);
            case LoginMethod.RememberedAccount:
                var remembered = _rememberedLogins.Read() ?? throw new InvalidOperationException("no remembered login");
                return PortalCredential.ForPasswordToken(remembered.Email, RememberedPasswordToken.Unprotect(remembered.ProtectedPassword), remembered.PasswordVersion);
            default:
                return PortalCredential.ForPassword(request.Email, request.Password);
        }
    }

    private void Remember(LoginRequest request, PortalGameToken token)
    {
        if (request.Method == LoginMethod.Steam) return;
        if (!request.RemembersLogin)
        {
            _rememberedLogins.Forget();
            return;
        }
        if (token.PasswordToken is { } passwordToken && token.PasswordTokenType is { } passwordTokenType)
            _rememberedLogins.Save(token.LoginName, passwordToken, passwordTokenType);
    }

    private string AskSecondFactorCode(string authType)
    {
        _secondFactorCode = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        SecondFactorType = authType;
        Enter(LoginStage.WaitingForSecondFactor, $"waiting for the {authType} code");
        string code = _secondFactorCode.Task.GetAwaiter().GetResult();
        SecondFactorType = null;
        Enter(LoginStage.PortalLogin, "checking the code");
        return code;
    }

    private ulong? RunAuthConnection(ExecutableProtocolSchemas schemas, HandshakeGroup group, LoginIdentity identity)
    {
        var server = _servers.AuthServer ?? new ServerEndpoint(AuthServerAddress.HostName(identity.Datacenter, ProgramId), SocketTransport.AuthServerPort);
        Enter(LoginStage.ConnectingAuthServer, $"connecting to {server}");
        using var transport = SocketTransport.ConnectAsync(server.Host, server.Port, CancellationToken.None).GetAwaiter().GetResult();
        transport.Send(ConnectHeader(schemas.Build));

        var connection = new ProtocolConnection(schemas.Login, group, RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength), transport.Send);
        var clock = new SystemEventTickTimeSource();
        var session = new LoginSession(connection, identity, () => clock.TryRead(out var now) ? now.Milliseconds : 0);
        ulong? accountResult = null;
        session.MessageReceived += message =>
        {
            MessageReceived?.Invoke(message.MessageId);
            if (message.MessageId != AccountReplyMessageId || message.Fields.Count <= 2 || message.Fields[1].Value is not ulong serial || message.Fields[2].Value is not ulong code) return;
            GameTraffic?.Invoke($"login transaction {serial} result {code}");
            if (serial == session.LoginSerial) accountResult = code;
        };
        var loop = new EventTickLoop(clock, limitsBacklog: true);
        loop.Attach(connection);
        loop.Ticked += _ =>
        {
            while (transport.TryTakeReceived(out var bytes)) session.Receive(bytes);
            session.OnTick();
            StartPendingPlay(session);
            _game?.ReceivePending();
        };

        var waited = Stopwatch.StartNew();
        while (!transport.Closed && !_logoutRequested)
        {
            loop.RunOnce();
            if (accountResult is { } failure && failure != 0) break;
            if (accountResult == 0 && Stage == LoginStage.ConnectingAuthServer)
            {
                lock (_gate)
                {
                    _characters = [.. session.Characters.Characters];
                    _accountPreferencePack = session.AccountPreferencePack;
                }
                Enter(LoginStage.CharacterSelect, $"{_characters.Count} characters");
            }
            if (session.IsForcedOut)
            {
                Fail("the login server ended the session");
                break;
            }
            SendPendingPreferenceUpload(session);
            if (session.PlayFailure is { } playFailure)
            {
                Fail($"the login server refused Play (result {playFailure})");
                break;
            }
            if (session.Referral is { } referral && _game is null) JoinGame(schemas, group, identity, session, referral, loop);
            if (_game is { Closed: true } && Stage is LoginStage.InGame or LoginStage.JoiningGame)
            {
                Fail("the game server closed the connection");
                break;
            }
            if (_game is { LoadMapId: { } loadMapId } && Stage == LoginStage.JoiningGame) Enter(LoginStage.InGame, $"loading map {loadMapId}");
            if (_game is { BeganPlaying: true } && Status.StartsWith("loading")) Enter(LoginStage.InGame, "playing");
            if (accountResult is null && waited.Elapsed > AccountReplyTimeout)
            {
                Fail("the login server did not answer");
                break;
            }
            Thread.Sleep(TickSleep);
        }

        if (accountResult is { } result && result != 0 && result != WrongDatacenter) Fail($"the login server refused the account (result {result})");
        if (transport.Closed && !_logoutRequested && Stage == LoginStage.CharacterSelect) Fail("the login server closed the connection");
        if (!transport.Closed && session.Started)
        {
            session.Logout();
            loop.RunOnce();
        }
        _game?.Dispose();
        _game = null;
        return accountResult;
    }

    private void SendPendingPreferenceUpload(LoginSession session)
    {
        byte[]? upload;
        lock (_gate)
        {
            upload = _pendingPreferenceUpload;
            _pendingPreferenceUpload = null;
        }
        if (upload is not null && session.Started) session.UploadAccountPreferences(upload);
    }

    private void StartPendingPlay(LoginSession session)
    {
        (CharacterSummary Character, Func<ushort, uint> GameTypeOfMap, GameAdapter Adapter)? play;
        lock (_gate)
        {
            play = _pendingPlay;
            _pendingPlay = null;
        }
        if (play is not { } request) return;
        _playAdapter = request.Adapter;
        _playCharacterName = request.Character.Name;
        Enter(LoginStage.JoiningGame, $"playing {request.Character.Name}");
        session.Play(request.Character, request.GameTypeOfMap);
    }

    private void JoinGame(ExecutableProtocolSchemas schemas, HandshakeGroup group, LoginIdentity identity, LoginSession session, GameServerReferral referral, EventTickLoop loop)
    {
        Enter(LoginStage.JoiningGame, $"connecting to the game server for map {referral.MapId}");
        var transport = SocketTransport.ConnectAsync(referral.Address.ToString(), referral.Port, CancellationToken.None).GetAwaiter().GetResult();
        transport.Send(new GameJoinRecord(identity.Build, referral.Ticket, referral.MapId, referral.InstanceTicket, session.JoinToken.ToArray(), identity.PortalUserId).Encode());
        var connection = new ProtocolConnection(schemas.Game, group, RandomNumberGenerator.GetBytes(HandshakeKeyExchange.SeedLength), transport.Send);
        loop.Attach(connection);
        var device = new DeviceReport(SystemRecord.Parse(identity.SystemRecord), _playAdapter!, _storefront, _debugSessionId);
        _game = new GameSession(transport, connection, new GameIdentity(_playCharacterName!, identity.AccountLoginName, identity.Provider, EnglishLanguage, EnglishLanguage, device));
        _game.Traffic += line => GameTraffic?.Invoke(line);
    }

    private static byte[] ConnectHeader(int build)
    {
        var header = new byte[16];
        BitConverter.TryWriteBytes(header, ConnectHeaderKind);
        BitConverter.TryWriteBytes(header.AsSpan(4), build);
        BitConverter.TryWriteBytes(header.AsSpan(8), ProgramId);
        BitConverter.TryWriteBytes(header.AsSpan(12), ConnectHeaderVersion);
        return header;
    }

    private void Enter(LoginStage stage, string status)
    {
        Stage = stage;
        Status = status;
        Progress?.Invoke(status);
    }

    private void Fail(string error)
    {
        Error = error;
        Enter(LoginStage.Failed, error);
    }

    private static string Describe(Exception exception) => exception switch
    {
        PortalRequestFailed { Reply.StatusText: var text } when text.Contains("ErrBadPasswd") => "wrong email or password",
        PortalRequestFailed failed => $"the portal refused the login ({failed.Reply.StatusText})",
        SteamApiUnavailableException steam => $"Steam is unavailable: {steam.Message}",
        System.Net.Http.HttpRequestException http => $"could not check the live build: {http.Message}",
        TaskCanceledException => "the login was cancelled",
        IOException or System.Net.Sockets.SocketException => $"network error: {exception.Message}",
        _ => exception.Message,
    };
}
