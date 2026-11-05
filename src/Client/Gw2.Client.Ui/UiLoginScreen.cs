using System.Collections.Generic;
using System;
using System.Numerics;
using System.Text;
using Crystal;
using Crystal.Graphics;
using Crystal.ImGui;
using Gw2.Client.Net.Login;
using Gw2.Client.Net.Transport;

namespace Gw2.Client.Ui;

public sealed class UiLoginScreen : IDisposable
{
    private const int TextCapacity = 256;
    private const int AccountMethod = 0;
    private const int SteamMethod = 1;
    private const int CustomServerMethod = 2;
    private const float LoginWindowWidth = 300f;
    private const float CharacterSelectWindowWidth = 420f;
    private const float CharacterListHeight = 200f;
    private static readonly Vector2 Centered = new(0.5f, 0.5f);
    private static readonly Vector4 ErrorColor = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 StatusColor = new(0.7f, 0.8f, 1f, 1f);
    private const float LoadScreenStatusMargin = 48f;
    private static readonly Vector2 BottomCentered = new(0.5f, 1f);

    private readonly ImGuiRenderer _renderer;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly LoginFlow _login;
    private readonly RememberedLoginStore _rememberedLogins;
    private readonly byte[] _email = new byte[TextCapacity];
    private readonly byte[] _password = new byte[TextCapacity];
    private readonly byte[] _secondFactorCode = new byte[TextCapacity];
    private readonly byte[] _portalServer = new byte[TextCapacity];
    private readonly byte[] _authServer = new byte[TextCapacity];
    private readonly byte[] _portalCertificatePath = new byte[TextCapacity];
    private readonly byte[] _authServerPublicValue = new byte[TextCapacity];
    private readonly Action<LoginServerSettings, bool> _saveCustomServers;
    private readonly string _executableDescription;
    private int _method = AccountMethod;
    private bool _remembersLogin;
    private string? _rememberedEmail;
    private int _selectedCharacter;
    private bool _focusesFirstField = true;
    private readonly bool _canLogInWithSteam;
    private Texture2D? _loadScreenImage;
    private ImTextureRef _loadScreenImageRef;

    public UiLoginScreen(Game game, LoginFlow login, RememberedLoginStore rememberedLogins, LoginServerSettings customServers, bool customServerSelected, Action<LoginServerSettings, bool> saveCustomServers, string executableDescription)
    {
        _executableDescription = executableDescription;
        var servers = customServers;
        _saveCustomServers = saveCustomServers;
        Encoding.UTF8.GetBytes(servers.Portal, _portalServer);
        Encoding.UTF8.GetBytes(servers.AuthServer, _authServer);
        Encoding.UTF8.GetBytes(servers.PortalCertificatePath, _portalCertificatePath);
        Encoding.UTF8.GetBytes(servers.AuthServerPublicValue, _authServerPublicValue);
        if (customServerSelected) _method = CustomServerMethod;
        _graphicsDevice = game.GraphicsDevice;
        _renderer = new ImGuiRenderer(game);
        _login = login;
        _rememberedLogins = rememberedLogins;
        _canLogInWithSteam = login.CanLogInWithSteam();
        LoadRememberedLogin();
    }

    public CharacterSummary? PlayedCharacter { get; private set; }

    public void ShowLoadScreen(Texture2D image)
    {
        if (_loadScreenImage is not null) return;
        _loadScreenImage = image;
        _loadScreenImageRef = _renderer.BindTexture(image);
    }

    public void Draw(GameTime gameTime)
    {
        _graphicsDevice.SetRenderTarget(null);
        _renderer.BeginFrame(gameTime);
        var viewport = ImGui.GetMainViewport();
        if (_login.Stage is LoginStage.JoiningGame or LoginStage.InGame)
        {
            DrawLoadScreen(viewport);
            _renderer.EndFrame();
            return;
        }
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * 0.5f, ImGuiCond.Always, Centered);
        ImGui.SetNextWindowSize(new Vector2(_login.Stage is LoginStage.CharacterSelect ? CharacterSelectWindowWidth : LoginWindowWidth, 0f), ImGuiCond.Always);
        bool staysOpen = true;
        if (ImGui.Begin("Guild Wars 2", ref staysOpen, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
        {
            switch (_login.Stage)
            {
                case LoginStage.CharacterSelect: DrawCharacterSelect(); break;
                case LoginStage.WaitingForSecondFactor: DrawSecondFactor(); break;
                case LoginStage.RequestingSteamTicket or LoginStage.PortalLogin or LoginStage.ConnectingAuthServer: DrawProgress(); break;
                default: DrawLoginForm(); break;
            }
        }
        ImGui.End();
        _renderer.EndFrame();
    }

    public void Dispose()
    {
        Array.Clear(_password);
        if (_loadScreenImage is not null)
        {
            _renderer.UnbindTexture(_loadScreenImageRef);
            _loadScreenImage.Dispose();
        }
        _renderer.Dispose();
    }

    private void DrawLoginForm()
    {
        ImGui.RadioButton("ArenaNet", ref _method, AccountMethod);
        ImGui.SameLine();
        ImGui.BeginDisabled(!_canLogInWithSteam);
        ImGui.RadioButton("Steam", ref _method, SteamMethod);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.RadioButton("Custom", ref _method, CustomServerMethod);
        if (!_canLogInWithSteam && _method == SteamMethod) _method = AccountMethod;
        if (!_canLogInWithSteam) ImGui.TextWrapped("Steam login needs Steam running and logged in to an account that owns Guild Wars 2.");
        ImGui.Separator();

        bool submitted = false;
        if (_method == CustomServerMethod) DrawServerFields();
        if (_method != SteamMethod)
        {
            if (_rememberedEmail is { } rememberedEmail)
            {
                ImGui.Text($"Remembered: {rememberedEmail}");
                if (ImGui.Button("Use another account"))
                {
                    _rememberedEmail = null;
                    _focusesFirstField = true;
                }
            }
            else
            {
                if (_focusesFirstField)
                {
                    ImGui.SetKeyboardFocusHere();
                    _focusesFirstField = false;
                }
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputTextWithHint("##email", "Email", _email);
                ImGui.SetNextItemWidth(-1f);
                submitted |= ImGui.InputTextWithHint("##password", "Password", _password, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue);
            }
            ImGui.Checkbox("Remember login", ref _remembersLogin);
        }
        else
        {
            ImGui.TextWrapped("Logs in with the Steam account that is running Steam on this computer.");
        }

        if (_login.Error is { } error) ImGui.TextColored(ErrorColor, error);
        ImGui.Spacing();
        if (ImGui.Button("Log in", new Vector2(-1f, 0f)) || submitted) StartLogin();
        ImGui.TextDisabled(_executableDescription);
    }

    private void DrawServerFields()
    {
        ImGui.TextWrapped("Empty fields use the retail servers.");
        ServerField("##portal", "Portal ip[:port]", _portalServer);
        ServerField("##portal-certificate", "Portal certificate file", _portalCertificatePath);
        ServerField("##auth-server", "Auth server ip[:port]", _authServer);
        ServerField("##auth-server-public-value", "Auth server public value (hex)", _authServerPublicValue);
        ImGui.Separator();
    }

    private static void ServerField(string label, string hint, byte[] text)
    {
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint(label, hint, text);
    }

    private void DrawSecondFactor()
    {
        ImGui.Text($"Enter the {_login.SecondFactorType} code");
        ImGui.SetKeyboardFocusHere();
        ImGui.SetNextItemWidth(-1f);
        bool submitted = ImGui.InputTextWithHint("##code", "Code", _secondFactorCode, ImGuiInputTextFlags.EnterReturnsTrue);
        if (ImGui.Button("Continue", new Vector2(-1f, 0f)) || submitted)
        {
            _login.SubmitSecondFactor(TextOf(_secondFactorCode));
            Array.Clear(_secondFactorCode);
        }
    }

    private void DrawLoadScreen(ImGuiViewportPtr viewport)
    {
        if (_loadScreenImage is not null)
        {
            var screen = viewport.Size;
            var image = new Vector2(_loadScreenImage.Width, _loadScreenImage.Height);
            var scale = MathF.Max(screen.X / image.X, screen.Y / image.Y);
            var visible = screen / (image * scale);
            var uvMin = (Vector2.One - visible) * 0.5f;
            ImGui.GetBackgroundDrawList().AddImage(_loadScreenImageRef, viewport.Pos, viewport.Pos + screen, uvMin, uvMin + visible);
        }
        ImGui.SetNextWindowPos(viewport.WorkPos + new Vector2(viewport.WorkSize.X * 0.5f, viewport.WorkSize.Y - LoadScreenStatusMargin), ImGuiCond.Always, BottomCentered);
        var loadingStaysOpen = true;
        if (ImGui.Begin("Loading", ref loadingStaysOpen, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs))
            DrawProgress();
        ImGui.End();
    }

    public IReadOnlyList<string> LoadProgress { get; set; } = [];

    private void DrawProgress()
    {
        ImGui.TextColored(StatusColor, _login.Status);
        foreach (var line in LoadProgress) ImGui.TextUnformatted(line);
    }

    private void DrawCharacterSelect()
    {
        var characters = _login.Characters;
        ImGui.Text("Select a character");
        if (ImGui.BeginListBox("##characters", new Vector2(-1f, CharacterListHeight)))
        {
            for (int index = 0; index < characters.Count; index++)
                if (ImGui.Selectable(characters[index].Name, index == _selectedCharacter)) _selectedCharacter = index;
            ImGui.EndListBox();
        }
        bool hasCharacter = _selectedCharacter < characters.Count;
        ImGui.BeginDisabled(!hasCharacter);
        if (ImGui.Button("Play", new Vector2(-1f, 0f)) && hasCharacter) PlayedCharacter = characters[_selectedCharacter];
        ImGui.EndDisabled();
        if (ImGui.Button("Log out", new Vector2(-1f, 0f))) _login.Logout();
    }

    private void StartLogin()
    {
        var customServers = new LoginServerSettings(TextOf(_portalServer), TextOf(_authServer), TextOf(_portalCertificatePath), TextOf(_authServerPublicValue));
        _saveCustomServers(customServers, _method == CustomServerMethod);
        var servers = _method == CustomServerMethod ? customServers : LoginServerSettings.Retail;
        LoginRequest request = _method switch
        {
            SteamMethod => new LoginRequest(LoginMethod.Steam, "", "", RemembersLogin: false, servers),
            _ when _rememberedEmail is not null => new LoginRequest(LoginMethod.RememberedAccount, "", "", _remembersLogin, servers),
            _ => new LoginRequest(LoginMethod.Account, TextOf(_email), TextOf(_password), _remembersLogin, servers),
        };
        Array.Clear(_password);
        _selectedCharacter = 0;
        _login.Start(request);
    }

    private void LoadRememberedLogin()
    {
        try
        {
            if (_rememberedLogins.Read() is not { } remembered) return;
            _rememberedEmail = remembered.Email;
            _remembersLogin = true;
            Encoding.UTF8.GetBytes(remembered.Email, _email);
        }
        catch (Exception)
        {
            _rememberedEmail = null;
        }
    }

    private static string TextOf(byte[] text)
    {
        int length = Array.IndexOf(text, (byte)0);
        return Encoding.UTF8.GetString(text, 0, length < 0 ? text.Length : length);
    }
}
