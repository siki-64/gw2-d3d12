using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Gw2.Client.Net.Portal;

public sealed record PortalGameToken(Guid Token, Guid UserId, string AccountAlias, string UserName, string LoginName, string Provider, string GateHostname, byte[]? PasswordToken = null, string? PasswordTokenType = null);

public sealed record PortalCredential(string LoginName, string Provider, byte[] Password, string? PasswordTokenType = null)
{
    public static readonly IReadOnlyList<string> PasswordTokenTypes = ["srp", "srp2", "gw1"];

    public const string PortalProvider = "Portal";
    public const string SteamProvider = "Steam";

    public static PortalCredential ForPassword(string loginName, string password) =>
        new(loginName, PortalProvider, Encoding.UTF8.GetBytes(password));

    public static PortalCredential ForPasswordToken(string loginName, byte[] passwordToken, byte passwordTokenVersion) =>
        new(loginName, PortalProvider, passwordToken, PasswordTokenTypes[passwordTokenVersion]);

    public static PortalCredential ForSteamTicket(string loginName, ReadOnlySpan<byte> authSessionTicket) =>
        new(loginName, SteamProvider, Encoding.UTF8.GetBytes(Convert.ToHexStringLower(authSessionTicket)));
}

public sealed class PortalRequestFailed : Exception
{
    public PortalRequestFailed(string path, StsReply reply)
        : base($"{path} answered {reply.StatusCode} {reply.StatusText}: {reply.Body}") => Reply = reply;

    public StsReply Reply { get; }
}

public sealed class PortalLogin : IDisposable
{
    public const string Scopes = "_self full_economy";
    public const string Language = "en";
    public const string GameCode = "gw2";
    public const int AuthorizationTimeoutMilliseconds = 600000;
    private static readonly string[] CodeAuthTypes = { "SMS", "EmailCode", "Totp" };

    private readonly PortalTrustAnchors _trustAnchors;
    private readonly int _port;
    private readonly Action<string>? _log;
    private StsConnection? _gate;
    private PortalGameToken? _account;

    public PortalLogin(PortalTrustAnchors trustAnchors, int port, Action<string>? log)
    {
        _trustAnchors = trustAnchors;
        _port = port;
        _log = log;
    }

    public Func<string, string>? AskSecondFactorCode { get; set; }

    public static string PortalHostName(uint region) => $"cligate.{region}.ncplatform.net";

    public PortalGameToken Login(string portalHostname, PortalCredential credential, Guid clientId)
    {
        string gateHostname = ResolveGateHostname(portalHostname, credential);
        _gate = OpenConnection(gateHostname);

        StsReply login = Expect("/Auth/StartSsoLogin", _gate.Request("/Auth/StartSsoLogin", new XElement("Request",
            new XElement("CID", clientId.ToString().ToUpperInvariant()),
            new XElement("Provider", credential.Provider),
            new XElement("LoginName", credential.LoginName),
            new XElement("Scopes", Scopes),
            new XElement("Language", Language),
            PasswordElements(credential))));
        login = CompleteSecondFactor(login);

        StsReply accounts = Expect("/Account/ListMyGameAccounts", _gate.Request("/Account/ListMyGameAccounts",
            new XElement("Request", new XElement("GameCode", GameCode))));
        string alias = accounts.Body!.Elements("Row")
            .Where(row => row.Element("GameCode")?.Value == GameCode)
            .Select(row => row.Element("Alias")?.Value)
            .FirstOrDefault() ?? throw new InvalidOperationException("no gw2 game account on this login");

        _account = new PortalGameToken(Guid.Empty, Guid.Parse(login.Element("UserId")!), alias, login.Element("UserName")!,
            login.Element("LoginName")!, credential.Provider, gateHostname,
            login.Element("PasswordToken") is { Length: > 0 } passwordToken ? Convert.FromBase64String(passwordToken) : null,
            login.Element("PasswordTokenVersion"));
        return RequestGameToken();
    }

    public PortalGameToken RequestGameToken()
    {
        if (_account is null || _gate is null) throw new InvalidOperationException("the portal login has not completed");
        StsReply token = Expect("/Auth/RequestGameToken", _gate.Request("/Auth/RequestGameToken", new XElement("Request",
            new XElement("GameCode", GameCode),
            new XElement("AccountAlias", _account.AccountAlias))));
        return _account with { Token = Guid.Parse(token.Element("Token")!) };
    }

    private static object[] PasswordElements(PortalCredential credential) => credential.PasswordTokenType is { } tokenType
        ? [new XElement("PasswordToken", Convert.ToBase64String(credential.Password)), new XElement("PasswordType", tokenType)]
        : [new XElement("Password", Convert.ToBase64String(credential.Password))];

    public void Logout() => _gate?.Request("/Auth/LogoutMyClient", new XElement("Request"));

    public void Dispose() => _gate?.Dispose();

    private string ResolveGateHostname(string portalHostname, PortalCredential credential)
    {
        using StsConnection portal = OpenConnection(portalHostname);
        StsReply reply = Expect("/Auth/GetHostname", portal.Request("/Auth/GetHostname", new XElement("Request",
            new XElement("LoginName", credential.LoginName),
            new XElement("Provider", credential.Provider))));
        return reply.Element("Hostname")!.TrimEnd('.');
    }

    private StsConnection OpenConnection(string hostname)
    {
        var connection = new StsConnection(hostname, _port, _trustAnchors.Accepts) { Log = _log };
        connection.Open();
        return connection;
    }

    private StsReply CompleteSecondFactor(StsReply login)
    {
        string? authType = login.Element("AuthType");
        if (string.IsNullOrEmpty(authType) || authType == "None") return login;
        _log?.Invoke($"portal second factor {authType}");

        bool asksForCode = CodeAuthTypes.Contains(authType) || (authType == "Email" && login.Body!.Element("AuthPromptInput") is not null);
        if (asksForCode)
        {
            string code = AskSecondFactorCode?.Invoke(authType) ?? throw new InvalidOperationException($"{authType} needs a code");
            return Expect("/Auth/UpgradeSsoLogin", _gate!.Request("/Auth/UpgradeSsoLogin",
                new XElement("Request", new XElement("Otp", code))));
        }

        string requestId = login.Element("RequestId") ?? throw new InvalidOperationException($"{authType} without RequestId");
        _log?.Invoke("portal waiting for the authorization");
        return Expect("/Auth/WaitForSsoAuthz", _gate!.Request("/Auth/WaitForSsoAuthz",
            new XElement("Request", new XElement("RequestId", requestId)), AuthorizationTimeoutMilliseconds));
    }

    private static StsReply Expect(string path, StsReply reply)
    {
        if (reply.StatusCode != 200) throw new PortalRequestFailed(path, reply);
        return reply;
    }
}
