using System;
using System.Collections.Generic;
using Gw2.Client.Net.Portal;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client.Net.Login;

public sealed record LoginIdentity(
    string UserName,
    string HostName,
    uint Build,
    byte[] InstallId,
    uint ConfigFlag,
    uint Datacenter,
    byte[] PortalUserId,
    byte[] GameToken,
    string LoginOption,
    string Provider,
    byte[] SystemRecord,
    uint ClientInfo)
{
    public LoginIdentity WithPortalToken(PortalGameToken token) => this with
    {
        PortalUserId = token.UserId.ToByteArray(),
        GameToken = token.Token.ToByteArray(),
        Provider = token.Provider,
        AccountLoginName = token.LoginName,
    };

    public string AccountLoginName { get; init; } = "";
}

public static class LoginMessageIds
{
    public const int ClockSample = 0x1;
    public const int ClientNames = 0x2;
    public const int ClientBuild = 0x3;
    public const int SystemRecord = 0x4;
    public const int Config = 0x6;
    public const int AccountLogin = 0xA;
    public const int Logout = 0xB;
    public const int ClientInfo = 0x22;
    public const int WorldSelect = 0xE;
    public const int PlayCharacter = 0x16;
    public const int MapTravel = 0x1D;
    public const int AccountPreferencesUpload = 0xC;

    public const int ServerHello = 0x2;
    public const int AccountReady = 0x8;
    public const int TransactionComplete = 0x4;
    public const int GameServerReferral = 0x14;
    public const int Character = 0x11;
    public const int AccountPreferences = 0xA;
    public const int ForceLogout = 0x1A;
}

public sealed class LoginSession
{
    private const int AccountPreferencePackFieldIndex = 2;
    private readonly ProtocolConnection _connection;
    private readonly LoginIdentity _identity;
    private readonly Func<uint> _milliseconds;
    private bool _clientInfoPosted;
    private bool _systemRecordSent;
    private const uint PlayWorldFlag = 1;
    private const int JoinTokenHalfLength = 16;
    private const int HomeWorldFieldIndex = 7;
    private const int MissionTravelValueLength = 16;
    private uint? _nextSerial;
    private uint? _playCharacterSerial;
    private uint? _mapTravelSerial;
    private ushort _travelMapId;
    private uint _travelGameType;
    private readonly byte[] _joinToken = new byte[JoinTokenHalfLength * 2];

    public LoginSession(ProtocolConnection connection, LoginIdentity identity, Func<uint> milliseconds)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _milliseconds = milliseconds ?? throw new ArgumentNullException(nameof(milliseconds));
    }

    public CharacterList Characters { get; } = new CharacterList();

    public bool Started { get; private set; }

    public byte[]? AccountPreferencePack { get; private set; }

    public uint HomeWorld { get; private set; }

    public ReadOnlySpan<byte> JoinToken => _joinToken;

    public GameServerReferral? Referral { get; private set; }

    public ulong? PlayFailure { get; private set; }

    public void Play(CharacterSummary character, Func<ushort, uint> gameTypeOfMap)
    {
        ArgumentNullException.ThrowIfNull(character);
        _travelMapId = character.LocationMapId;
        _travelGameType = gameTypeOfMap(_travelMapId);
        Send(LoginMessageIds.WorldSelect, NextSerial(), HomeWorld, PlayWorldFlag);
        _playCharacterSerial = NextSerial();
        Send(LoginMessageIds.PlayCharacter, _playCharacterSerial.Value, character.Name);
    }

    public uint LoginSerial => _identity.Datacenter;

    private uint NextSerial()
    {
        var serial = _nextSerial ?? LoginSerial + 1;
        _nextSerial = serial + 1;
        return serial;
    }

    public event Action<DecodedMessage>? MessageReceived;

    public void Receive(ReadOnlySpan<byte> bytes)
    {
        var messages = _connection.Receive(bytes);
        if (!Started && _connection.Established)
        {
            Started = true;
            SendConnectSetup();
        }
        foreach (var message in messages)
        {
            Handle(message);
            MessageReceived?.Invoke(message);
        }
    }

    public void OnTick()
    {
        if (!_clientInfoPosted) return;
        _clientInfoPosted = false;
        Send(LoginMessageIds.ClientInfo, _identity.ClientInfo);
    }

    public bool IsForcedOut { get; private set; }

    public void Logout()
    {
        if (!IsForcedOut) Send(LoginMessageIds.Logout, 0u);
    }

    public void UploadAccountPreferences(byte[] preferencePack)
    {
        AccountPreferencePack = preferencePack;
        Send(LoginMessageIds.AccountPreferencesUpload, NextSerial(), preferencePack);
    }

    private void SendConnectSetup()
    {
        Send(LoginMessageIds.ClientNames, _identity.UserName, _identity.HostName);
        Send(LoginMessageIds.ClientBuild, _identity.Build, _identity.InstallId);
        Send(LoginMessageIds.Config, _identity.ConfigFlag);
    }

    private void Handle(DecodedMessage message)
    {
        switch (message.MessageId)
        {
            case LoginMessageIds.ServerHello:
                Send(LoginMessageIds.ClockSample, _milliseconds() | 1u);
                Send(LoginMessageIds.AccountLogin, _identity.Datacenter, _identity.PortalUserId, _identity.GameToken, _identity.LoginOption, _identity.Provider);
                break;
            case LoginMessageIds.AccountReady when Serial(message) == LoginSerial:
                Fill(message.Fields[2].Value, 0);
                Fill(message.Fields[3].Value, JoinTokenHalfLength);
                HomeWorld = (uint)(ulong)message.Fields[HomeWorldFieldIndex].Value!;
                if (_systemRecordSent) break;
                _systemRecordSent = true;
                Send(LoginMessageIds.SystemRecord, _identity.SystemRecord, _identity.InstallId);
                _clientInfoPosted = true;
                break;
            case LoginMessageIds.AccountReady when Serial(message) == _playCharacterSerial:
                Fill(message.Fields[3].Value, JoinTokenHalfLength);
                break;
            case LoginMessageIds.TransactionComplete when Serial(message) == _playCharacterSerial:
                _playCharacterSerial = null;
                if (Result(message) is var playResult && playResult != 0)
                {
                    PlayFailure = playResult;
                    break;
                }
                _mapTravelSerial = NextSerial();
                Send(LoginMessageIds.MapTravel, _mapTravelSerial.Value, _travelGameType, 0u, (uint)_travelMapId, 0u, 0u, new byte[MissionTravelValueLength]);
                break;
            case LoginMessageIds.TransactionComplete when Serial(message) == _mapTravelSerial && Result(message) != 0:
                PlayFailure = Result(message);
                break;
            case LoginMessageIds.GameServerReferral when Serial(message) == _mapTravelSerial:
                Referral = Login.GameServerReferral.FromMessage(message.Fields);
                break;
            case LoginMessageIds.AccountPreferences when message.Fields.Count > AccountPreferencePackFieldIndex:
                AccountPreferencePack = message.Fields[AccountPreferencePackFieldIndex].Value as byte[];
                break;
            case LoginMessageIds.Character:
                Characters.Add(ReadCharacter(message.Fields));
                break;
            case LoginMessageIds.ForceLogout when !IsForcedOut:
                Send(LoginMessageIds.Logout, 0u);
                IsForcedOut = true;
                break;
        }
    }

    private static uint? Serial(DecodedMessage message) =>
        message.Fields.Count > 1 && message.Fields[1].Value is ulong serial ? (uint)serial : null;

    private static ulong Result(DecodedMessage message) => message.Fields.Count > 2 && message.Fields[2].Value is ulong result ? result : ulong.MaxValue;

    private void Fill(object? guid, int offset)
    {
        if (guid is byte[] bytes && bytes.Length == JoinTokenHalfLength) bytes.CopyTo(_joinToken, offset);
    }

    private static CharacterSummary ReadCharacter(IReadOnlyList<DecodedField> fields) =>
        new CharacterSummary(
            (uint)(ulong)fields[1].Value!,
            new Guid((byte[])fields[2].Value!),
            BitConverter.ToUInt64((byte[])fields[4].Value!),
            (string)fields[6].Value!,
            (byte[])fields[7].Value!);

    private void Send(int messageId, params object[] values) => _connection.SendValues(messageId, values);
}
