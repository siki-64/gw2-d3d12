using Gw2.Client.Settings;
using Gw2.Client.Debug;
﻿using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Numerics;
using Crystal;
using Crystal.Graphics;
using Crystal.Input;
using Gw2.Client.Content;
using Gw2.Client.Model;
using Gw2.Client.Scene;
using Gw2.Client.Hud;
using Gw2.Client.State;
using Gw2.Client.World;
using Gw2.Dat.Content;
using Gw2.Formats;
using Gw2.Dat.Archive;
using Gw2.Client.Graphics;
using Gw2.Client.Ui;
using Gw2.Client.Net.Game;
using Gw2.Client.Net.Login;
using Gw2.Client.Net.Keys;
using Gw2.Protocol.MsgPack;

namespace Gw2.Client;

public sealed partial class Gw2Client : Crystal.Game
{
    private static readonly Color BackgroundColor = new(40, 44, 52);

    private Gw2ClientOptions _options;
    private readonly BenchmarkRecorder _benchmark;
    private readonly MemoryUsageSampler _memoryUsage = new();
    private Vector3 _characterPosition;
    private readonly long _startupTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    private long _startupStepTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
    private bool _firstFrameDrawn;
    private bool _sceneFilled;
    private readonly Gw2OrbitCamera _camera = new();
    private Gw2ContentReader? _reader;
    private Gw2ContentService? _content;
    private TextKeyDelivery? _textKeyDelivery;
    private readonly List<DecodedMessage> _serverMessages = [];
    private Gw2ArchiveMaterials? _archiveMaterials;
    private CharacterScene? _character;
    private Gw2HighResolutionFiles? _highResolutionFiles;

    private Gw2MapEnvironment? _environment;
    private readonly Gw2EnvironmentWind _environmentWind = new();
    private uint? _worldMapId;
    private IReadOnlyList<Gw2.Dat.Content.Gw2Waypoint> _mapWaypoints = [];
    private int _waypointTravelNumber;
    private bool _characterReady;
    private float _secondsSinceCharacterReady;
    private float _secondsSinceSceneSettled;
    private readonly InputFrame _input = new();
    private Gw2MapPointLightAnimation[] _pointLightAnimations = [];
    private readonly List<Gw2PointLightFrame> _pointLightFrames = [];
    private Gw2MapSpotLightAnimation[] _spotLightAnimations = [];
    private readonly List<Gw2SpotLightFrame> _spotLightFrames = [];
    private float _dayWeight = 1f;
    private readonly Gw2TimeOfDayClock _timeOfDayClock = new();
    private Gw2DayNightCycle? _dayNightCycle;
    private long? _timeOfDayFrameMilliseconds;
    private float _mapLightSeconds;
    private GrFrameProfiler? _frameTimer;
    private UiPerfOverlay? _perfOverlay;
    private LoginFlow? _login;
    private readonly WorldState _world = new();
    private readonly ContentNames _names;
    private readonly HudStateModel _hudStateModel;
    private HudState? _hudState;
    private Gw2ContentFunctionRunner? _contentFunctionRunner;
    private int _weaponSwapNumber;
    private bool _placedOnOwnAgent;
    private float? _defaultRunSpeed;
    private Vector3? _ownFacing;
    private Gw2CollisionWater? _mapWater;
    private Vector3 _ownCameraDesiredCentre;
    private bool _followsOwnCameraCentre;

    private void MoveOwnCameraCentre(Vector3 moved)
    {
        _ownCameraDesiredCentre += moved;
        if (!_followsOwnCameraCentre) _camera.Target += moved;
    }
    private float _ownCameraDesiredDistance = Gw2WorldCameraMotion.DefaultDistance;
    private float _frameElapsedSeconds;
    private int _framesThisSecond;
    private long _frameSecondStartTimestamp;
    private bool _playRequested;
    private bool _mapLoadReported;
    private Task<WorldMapRead>? _worldMapRead;
    private bool _streamsWorldBehindLoadScreen;
    private long _worldLoadStartTimestamp;
    private readonly List<string> _worldLoadSteps = [];
    private UiLoginScreen? _loginScreen;
    private TimeSpan _framePeriod;
    private long _framePresentedTimestamp;
    private long _frameWorkStartTimestamp;
    private bool _frameWorkStarted;

    private const string GameWindowTitle = "Guild Wars 2";
    private const int LoginWindowWidth = 480;
    private const int LoginWindowHeight = 320;
    private bool _windowGrownForCharacterSelect;

    public Gw2Client(Gw2ClientOptions options) : base(new GameOptions
    {
        Title = options.LoginExecutablePath is null ? $"Gw2 client - {options.Content.RaceSexName}" : GameWindowTitle,
        Width = options.LoginExecutablePath is null ? options.Benchmark.BackBufferWidth : LoginWindowWidth,
        Height = options.LoginExecutablePath is null ? options.Benchmark.BackBufferHeight : LoginWindowHeight,
        IsFixedTimeStep = false,
        Graphics = new GraphicsDeviceOptions { VSync = options.Graphics.VerticalSync },
    })
    {
        _options = options;
        _benchmark = new BenchmarkRecorder(options.Benchmark);
        _names = new ContentNames(() => _reader, _world);
        _hudStateModel = new HudStateModel(_world, () => _reader is null ? null : OwnSkillBarOf(), () => _reader is null ? null : HotkeysOf(), _names);
        _characterPosition = options.Scene.Position;
        _framePeriod = Gw2FrameLimit.PeriodOf(options.Benchmark.FrameLimit);
        IsMouseVisible = true;
    }

    protected override void Initialize()
    {
        if (Display.GetAll().FirstOrDefault(display => !display.IsPrimary) is { WorkArea.Width: > 0 } secondary)
            Window.Position = CenteredIn(secondary.WorkArea, Options.Width, Options.Height);
    }

    private void GrowWindowForCharacterSelect()
    {
        if (_windowGrownForCharacterSelect || _login?.Stage is not (LoginStage.CharacterSelect or LoginStage.JoiningGame or LoginStage.InGame)) return;
        _windowGrownForCharacterSelect = true;
        var (width, height) = (_options.Benchmark.BackBufferWidth, _options.Benchmark.BackBufferHeight);
        var workArea = Window.Display.WorkArea;
        Window.Resize(width, height);
        Window.Position = CenteredIn(workArea, width, height);
    }

    private static Point CenteredIn(Rectangle workArea, int width, int height) =>
        new(workArea.X + Math.Max(0, (workArea.Width - width) / 2), workArea.Y + Math.Max(0, (workArea.Height - height) / 2));

    protected override void LoadContent()
    {
        MarkStartup("engine and window");
        _reader = Gw2ContentReader.Open(_options.Content.DatPath, verifyChecksums: false);
        MarkStartup("archive index");
        _ = _reader.ContentStore;
        MarkStartup(_reader.LoadedContentStoreFromPackCache ? "content store from pack cache" : "content store from archive");
        _content = new Gw2ContentService(_reader);
        ApplyEnergyDefaults(new Gw2CharacterConfigs(_reader.ContentIndex, CharacterConfigTable.Current));
        var keys = _reader.Keys;
        _textKeyDelivery = new TextKeyDelivery((textId, key) => keys.Store(Gw2KeyStore.TextKind, textId, key), () => keys.Clear(Gw2KeyStore.TextKind));
        _archiveMaterials = new Gw2ArchiveMaterials(_content);
        MarkStartup("content services");
        _highResolutionFiles = ReadHighResolutionFiles(_options.Content.LocalDatPath);
        MarkStartup("high-resolution files");
        if (_options.LoginExecutablePath is null) CreateCharacter(_options.Content.RaceSexName);
        _frameTimer = GrFrameSections.CreateProfiler(GraphicsDevice);
        _perfOverlay = new UiPerfOverlay(this) { IsOpen = _options.Debug.PerfGraphs, DebugView = _options.Debug.DebugView };
        LoadGraphics();
        MarkStartup("renderer setup");
        if (_options.LoginExecutablePath is { } executablePath)
        {
            var rememberedLogins = RememberedLoginStore.ForCurrentUser();
            var customServers = CustomServerSettings.WithCommandLine(CustomServerSettings.Load(), _options.LoginServers);
            _login = new LoginFlow(executablePath, _options.Content.DatPath, rememberedLogins);
            _login.Progress += status => Console.WriteLine($"LOGIN {status}");
            _login.GameTraffic += line => Console.WriteLine($"GAME {line}");
            _loginScreen = new UiLoginScreen(this, _login, rememberedLogins, customServers.Servers, customServers.IsSelected,
                (servers, isSelected) => CustomServerSettings.Save(new CustomServerState(servers, isSelected)), DescribeExecutable(executablePath));
            return;
        }
        LoadWorld(_options.Content.MapId);
    }

    private static string DescribeExecutable(string executablePath)
    {
        try
        {
            return $"{Path.GetFileName(executablePath)} build {Gw2.Formats.ExecutableBuild.FromExecutable(executablePath)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return $"{Path.GetFileName(executablePath)} build unknown ({exception.Message})";
        }
    }

    private sealed record WorldMapRead(uint? WorldMapId, Gw2MapDefinitions Maps, uint EnvironmentMapFileId, Gw2MapBundle? Map, Gw2.Client.Scene.Havok.Gw2HavokMapWorld? HavokWorld);

    private WorldMapRead ReadWorldMap(uint? worldMapId)
    {
        var maps = new Gw2MapDefinitions(_reader!.ContentIndex, MapContentLayout.Current);
        var mapFileId = _options.Content.EnvironmentMapFileId ?? (worldMapId is { } mapId ? MapFileIdOfMap(maps, mapId) : null);
        if (mapFileId is not { } environmentMapFileId || _reader.ReadMap(environmentMapFileId) is not { Environment: not null } map)
            return new WorldMapRead(worldMapId, maps, 0, null, null);
        return new WorldMapRead(worldMapId, maps, environmentMapFileId, map, Gw2.Client.Scene.Havok.Gw2HavokMapWorld.Build(map.Collision, map.Terrain, map.Info.Bounds));
    }

    private void LoadWorld(uint? worldMapId) => EnterWorldMap(ReadWorldMap(worldMapId));

    private void EnterWorldMap(WorldMapRead read)
    {
        var worldMapId = read.WorldMapId;
        _worldMapId = worldMapId;
        _cameraViewDistance = read.Map?.Terrain?.ViewDistance ?? Gw2.Dat.Content.Gw2MapTerrain.DefaultViewSpan;
        _mapWaypoints = WaypointsOf(worldMapId);
        var maps = read.Maps;
        if (read is { Map: { Environment: { } environment } map, HavokWorld: not null, EnvironmentMapFileId: var environmentMapFileId })
        {
            MarkStartup("map read and collision");
            _timeOfDayClock.Set(Gw2TimeOfDayClock.ClockOf(_options.Scene.TimeOfDay), _options.Scene.TimeOfDayRate, _options.Debug.StillScene);
            var cycleMapId = worldMapId ?? maps.OfMapFile(_reader.Ids.BaseIdOf(environmentMapFileId)).Select(definition => (uint?)definition.MapId).FirstOrDefault() ?? 0;
            (_dayNightCycle, bool freezesClock) = maps.PickDayNightCycle(cycleMapId);
            _timeOfDayClock.EnterMap(freezesClock);
            UpdateDayWeight();
            _pointLightAnimations = map.Lights?.PointLights.Where(group => group.Lights.Count == 2).Select(group => new Gw2MapPointLightAnimation(group)).ToArray() ?? [];
            _spotLightAnimations = map.Lights?.SpotLights.Where(group => group.Lights.Count == 2).Select(group => new Gw2MapSpotLightAnimation(group)).ToArray() ?? [];
            _environment = environment;
            _havokWorld = read.HavokWorld;
            _mapWater = map.Collision is { } mapCollision ? Gw2CollisionWater.From(mapCollision) : null;
            _ownStep = null;
            WriteGroundUnderCharacter();
            LoadMapGraphics(map, environment, environmentMapFileId);
            MarkStartup("rest of map graphics");
        }
        FrameStartPosition();
    }

    private void MarkStartup(string step)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var line = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"STARTUP {step}: {System.Diagnostics.Stopwatch.GetElapsedTime(_startupStepTimestamp, now).TotalMilliseconds:F0} ms (total {System.Diagnostics.Stopwatch.GetElapsedTime(_startupTimestamp, now).TotalMilliseconds:F0} ms)");
        _startupStepTimestamp = now;
        Console.WriteLine(line);
    }

    partial void LoadGraphics();

    partial void LoadMapGraphics(Gw2MapBundle map, Gw2MapEnvironment environment, uint environmentMapFileId);

    partial void UnloadMapGraphics();

    private void RunContentFunctions(uint ownAgentId, bool dodgePressed, bool dodgeReleased, out bool dodgeTakenByContentFunction)
    {
        dodgeTakenByContentFunction = false;
        if (_login is null || _reader is null) return;
        _contentFunctionRunner ??= new Gw2ContentFunctionRunner(new Gw2ContentFunctions(_reader.ContentIndex, ContentFunctionContentLayout.Current, SkillPaletteContentLayout.Current.SkillContentType));
        _contentFunctionRunner.SyncBindings(_world.ContentFunctions.ActiveBindingUids);
        var world = new OwnContentFunctionHost(this, ownAgentId);
        var now = Environment.TickCount64;
        _world.ContentFunctions.ExpireMovementModifiers(now);
        SendContentFunctionReports(_contentFunctionRunner.Tick(now, world));
        ApplyContentFunctionEffects(ownAgentId, now);
        if (dodgeReleased) SendContentFunctionReports(_contentFunctionRunner.ReleaseInput(Gw2.Client.Net.Game.OwnContentFunctionState.DodgeInputId));
        if (!dodgePressed || _contentFunctionRunner.BindingOfInput(Gw2.Client.Net.Game.OwnContentFunctionState.DodgeInputId) is null) return;
        SendContentFunctionReports(_contentFunctionRunner.PressInput(Gw2.Client.Net.Game.OwnContentFunctionState.DodgeInputId, 0, now, world));
        dodgeTakenByContentFunction = true;
    }

    private void SendContentFunctionReports(IReadOnlyList<ContentFunctionReport> reports)
    {
        foreach (var report in reports)
        {
            var (messageId, values) = report switch
            {
                ContentFunctionInstanceStarted started => Gw2.Client.Net.Game.OwnContentFunctionState.InstanceStartMessage(started.InstanceId, started.TargetAgentId, started.Source, started.FunctionUid, started.InputId),
                ContentFunctionConditionEvaluated evaluated => Gw2.Client.Net.Game.OwnContentFunctionState.ConditionResultMessage(evaluated.InstanceId, evaluated.RootFunctionUid, evaluated.FunctionUid, evaluated.NodeIndex, evaluated.Result),
                ContentFunctionInputReleased released => Gw2.Client.Net.Game.OwnContentFunctionState.InputPressMessage(released.FunctionUid),
                ContentFunctionSkillActivated activated => Gw2.Client.Net.Game.OwnContentFunctionState.SkillActivationMessage(activated.FunctionUid, activated.NodeIndex, activated.SkillDataId),
                _ => throw new InvalidOperationException(report.ToString()),
            };
            _login!.QueueGameMessage(messageId, values);
        }
    }

    private void ApplyServerMessages()
    {
        _serverMessages.Clear();
        while (_login is not null && _login.TryTakeGameMessage(out var gameMessage))
        {
            var lifeStateBefore = LifeStateAgentOf(gameMessage) is { } lifeStateAgent ? _world.LifeState.StateOf(lifeStateAgent) : null;
            _world.Handle(gameMessage, ClientMilliseconds(), _defaultRunSpeed ??= CharacterMovementLayout.Current.DefaultRunSpeedInGameUnits);
            LogLifeStateMessage(gameMessage, lifeStateBefore);
            LogSkillbarAssignment(gameMessage);
            FollowServerPositionOfOwnCharacter(gameMessage);
            TakeServerForcedMovementOfOwnCharacter(gameMessage);
            _serverMessages.Add(gameMessage);
        }
        if (_textKeyDelivery is null) return;
        foreach (var message in _serverMessages) _textKeyDelivery.Handle(message);
    }

    protected override void Update(GameTime gameTime)
    {
        ApplyServerMessages();
        GrowWindowForCharacterSelect();
        if (_loginScreen is not null && !LeaveLoginScreenOnPlay()) return;
        if (!_frameWorkStarted)
        {
            _frameWorkStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            _frameWorkStarted = true;
            _benchmark.MarkFrameWorkStart(_archiveMaterials?.MainThreadReads ?? 0);
        }
        AdvanceTimeOfDay(gameTime);
        SampleInput();
        var stepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        UpdateContexts((float)gameTime.ElapsedGameTime.TotalSeconds, AnimationSeconds(gameTime));
        _benchmark.StartWhenSettled(_characterReady, _secondsSinceSceneSettled);
        _benchmark.AdvanceFlight((float)gameTime.ElapsedGameTime.TotalSeconds, _camera.LookFrom,
            () => _benchmark.WriteFlightSample(_camera.Eye, GraphicsDevice.GetVideoMemoryInfo().LocalUsage, FlightStreamingDescription(), _content?.CachedCount ?? 0));
        AdvanceMapLights(AnimationSeconds(gameTime));
        CompleteContent();
        if (_worldMapId is { } linkedMapId) _mumbleLink.Write(_characterPosition, _walkAhead ?? Vector2.UnitX, _camera.Eye, _camera.Target, linkedMapId);
    }

    private static double ElapsedStepMilliseconds(ref long started)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var milliseconds = (now - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        started = now;
        return milliseconds;
    }

    private void AdvanceTimeOfDay(GameTime gameTime)
    {
        var frameMilliseconds = (long)gameTime.TotalGameTime.TotalMilliseconds;
        if (_timeOfDayFrameMilliseconds is { } previous) _timeOfDayClock.Advance((int)(frameMilliseconds - previous));
        _timeOfDayFrameMilliseconds = frameMilliseconds;
        UpdateDayWeight();
    }

    private uint? MapFileIdOfMap(Gw2MapDefinitions maps, uint mapId) =>
        maps.Find(mapId) is { MapFileBaseId: not 0 } definition && _reader!.Ids.TryGetFileId(definition.MapFileBaseId, out var fileId) ? fileId : null;

    private void UpdateDayWeight()
    {
        if (_dayNightCycle is not { } cycle) return;
        _dayWeight = Gw2TimeOfDayClock.DayWeight(cycle, _timeOfDayClock.TimeOfDay);
    }

    private void AdvanceMapLights(float elapsedSeconds)
    {
        _mapLightSeconds += elapsedSeconds;
        var halfExtent = Gw2MapLightRange.HalfExtent(_options.Graphics.ShaderQuality);
        _pointLightFrames.Clear();
        foreach (var animation in _pointLightAnimations)
            if (Gw2MapLightRange.Contains(_camera.Eye, animation.Position, halfExtent))
                _pointLightFrames.Add(animation.Advance(_dayWeight, _mapLightSeconds, elapsedSeconds));
        _spotLightFrames.Clear();
        foreach (var animation in _spotLightAnimations)
            if (Gw2MapLightRange.Contains(_camera.Eye, animation.Position, halfExtent))
                _spotLightFrames.Add(animation.Advance(_dayWeight, _mapLightSeconds, elapsedSeconds));
    }

    private Texture2D? LoadScreenImageOf(ushort mapId)
    {
        var maps = new Gw2MapDefinitions(_reader!.ContentIndex, MapContentLayout.Current);
        if (maps.Find(mapId)?.LoadScreenImageFileId is not { } baseFileId) return null;
        var fileId = _highResolutionFiles?.HighResolutionOf(baseFileId) ?? baseFileId;
        return _reader.ReadTexture(fileId) is { } asset ? GrTextureUpload.FromAsset(GraphicsDevice, asset) : null;
    }

    private bool LeaveLoginScreenOnPlay()
    {
        if (_loginScreen!.PlayedCharacter is not { } character) return false;
        if (!_playRequested)
        {
            CreateCharacter(Gw2CharacterRaces.CompositeRaceNameOf(character.Race ?? -1, character.Sex ?? -1) ?? _options.Content.RaceSexName);
            var metadata = Gw2PackDecoders.Decode<Gw2MapMetadata>(_reader!.ReadDocument(Gw2MapMetadata.FileId))
                ?? throw new InvalidDataException("the map metadata pack does not decode");
            var adapter = GraphicsDevice.Adapter;
            _login!.Play(character, mapId => MapTravelGameTypes.ForMapType(metadata.MapTypeOf(mapId)),
                new GameAdapter(adapter.Description, adapter.VendorId, adapter.DeviceId, GraphicsDevice.GetVideoMemoryInfo().LocalBudget));
            _playRequested = true;
            if (LoadScreenImageOf(character.LocationMapId) is { } loadScreenImage) _loginScreen.ShowLoadScreen(loadScreenImage);
        }
        if (_login!.GameMapId is not { } gameMapId) return false;
        if (_worldMapRead is null)
        {
            if (_login.GameMapSpawnPositionInGameUnits is { } spawnPosition) _characterPosition = spawnPosition;
            _worldLoadStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            _worldMapRead = Task.Run(() => ReadWorldMap(gameMapId));
        }
        if (!_streamsWorldBehindLoadScreen)
        {
            if (!_worldMapRead.IsCompleted)
            {
                _loginScreen.LoadProgress = [$"Reading map {gameMapId}  {WorldLoadSeconds():F1} s"];
                return false;
            }
            _worldLoadSteps.Add($"Map and collision read  {WorldLoadSeconds():F1} s");
            _streamsWorldBehindLoadScreen = true;
            EnterWorldMap(_worldMapRead.Result);
            _worldLoadSteps.Add($"Sky, water and shadows  {WorldLoadSeconds():F1} s");
            return true;
        }
        _loginScreen.LoadProgress = [.. _worldLoadSteps, .. StreamingProgress(), $"Agents  {_world.Agents.All.Count}", $"Elapsed  {WorldLoadSeconds():F1} s"];
        if (!IsSceneSettled || !_characterReady) return true;
        if (!_mapLoadReported)
        {
            Console.WriteLine($"LOGIN world streamed in {WorldLoadSeconds():F1} s");
            _login.ReportMapLoaded();
            _mapLoadReported = true;
        }
        if (_login.ControlledAgentId is not { } ownId || !_world.Agents.TryGet(ownId, out _)) return true;
        Console.WriteLine($"LOGIN own agent placed in {WorldLoadSeconds():F1} s");
        _loginScreen.Dispose();
        _loginScreen = null;
        _streamsWorldBehindLoadScreen = false;
        return true;
    }

    private double WorldLoadSeconds() => System.Diagnostics.Stopwatch.GetElapsedTime(_worldLoadStartTimestamp).TotalSeconds;

    private IEnumerable<string> StreamingProgress()
    {
        var lines = new List<string>();
        DescribeStreaming(lines);
        if (!_characterReady) lines.Add("Character  loading");
        if (_mapLoadReported) lines.Add("Position  waiting for server");
        return lines;
    }

    partial void DescribeStreaming(List<string> lines);

    partial void StreamWorldAroundEye();

    protected override void Draw(GameTime gameTime)
    {
        if (_loginScreen is not null)
        {
            if (_streamsWorldBehindLoadScreen) StreamWorldAroundEye();
            GraphicsDevice.Clear(BackgroundColor);
            _loginScreen.Draw(gameTime);
            return;
        }
        _frameTimer?.BeginFrame(_frameWorkStartTimestamp);
        _memoryUsage.Sample(GraphicsDevice, _content?.CachedCount ?? 0);
        if (_frameTimer is { HasCompletedGpuFrame: true } completed) _benchmark.RecordGpuFrame(completed);
        if (_frameTimer is not null) _perfOverlay?.RecordGpu(_frameTimer);
        UpdateCollisionWindow();
        GraphicsDevice.Clear(BackgroundColor);
        DrawScene(gameTime);
        _perfOverlay?.Draw(gameTime);
        if (!_firstFrameDrawn)
        {
            _firstFrameDrawn = true;
            MarkStartup("first frame");
        }
        if (_characterReady && _options.Debug.ScreenshotPath is { } screenshotPath && _secondsSinceCharacterReady >= _options.Debug.ScreenshotAtSeconds) SaveScreenshotAndExit(screenshotPath);
    }

    partial void DrawScene(GameTime gameTime);

    private const float GroundProbeLift = 50f;
    private const float GroundProbeDepth = 5000f;

    private Gw2.Client.Scene.Havok.Gw2SurfaceHit? GroundUnderCharacter()
    {
        var position = _characterPosition;
        return _havokWorld?.CastGameRay(position - new Vector3(0f, 0f, GroundProbeLift), position + new Vector3(0f, 0f, GroundProbeDepth));
    }

    private void WriteGroundUnderCharacter()
    {
        var position = _characterPosition;
        Console.WriteLine(GroundUnderCharacter() is { } ground
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"COLLISION bodies {_havokWorld!.BodyCount} | ground {ground.Position.Z:F2} ({ground.Position.Z - position.Z:F2} below) | {(ground.IsTerrain ? "terrain" : "model")} {ground.BodyId} | normal {ground.Normal.X:F2},{ground.Normal.Y:F2},{ground.Normal.Z:F2}")
            : $"COLLISION bodies {_havokWorld?.BodyCount ?? 0} | no ground");
    }

    private void UpdateCollisionWindow()
    {
        if (_perfOverlay is not { Collision.IsOpen: true } overlay) return;
        overlay.Collision.BodyCount = _havokWorld?.BodyCount ?? 0;
        overlay.Collision.Position = _characterPosition;
        overlay.Collision.Ground = GroundUnderCharacter();
    }

    private static Gw2HighResolutionFiles? ReadHighResolutionFiles(string? localDatPath)
    {
        if (localDatPath is null || !File.Exists(localDatPath)) return null;
        using var local = FileArchive.Open(localDatPath, verifyChecksums: false);
        return Gw2HighResolutionFiles.Read(local);
    }

    protected override void UnloadContent()
    {
        _mumbleLink.Dispose();
        _loginScreen?.Dispose();
        _login?.Dispose();
        UnloadGraphics();
        _perfOverlay?.Dispose();
        _content?.Dispose();
        _reader?.Dispose();
    }

    partial void UnloadGraphics();

    private void SampleInput()
    {
        _input.Update();
        if (_perfOverlay is { } menuOverlay) menuOverlay.IsMenuAvailable = _loginScreen is null;
        _perfOverlay?.Update(_input);
        if (_perfOverlay?.ExitRequested == true) Exit();
        if (_perfOverlay is { } overlay && overlay.DebugView != _debugView) SetDebugView(overlay.DebugView);
        UpdateKeybindWindow();
        UpdateGraphicsSettingsWindow();
        UpdateGeneralOptionsWindow();
    }

    private void TakeServerForcedMovementOfOwnCharacter(DecodedMessage message)
    {
        if (Gw2.Client.Net.Game.AgentPositionBlends.BlendingAgentOf(message) is { } blendingAgent && blendingAgent == _login?.ControlledAgentId && _drawnPositionBeforeTeleport is { } before) BeginOwnPositionBlend(before);
        if (Gw2.Client.Net.Game.ServerForcedMovement.AgentOf(message) is not { } agentId || _login?.ControlledAgentId != agentId) return;
        if (Gw2.Client.Net.Game.ServerForcedMovement.MoveToOf(message) is { } moveTo) _pendingServerMoveTo = moveTo;
        if (Gw2.Client.Net.Game.ServerForcedMovement.JumpOf(message) is { } jump) _pendingServerJump = jump;
        if (Gw2.Client.Net.Game.ServerForcedMovement.TeleportOf(message) is { } teleport && _placedOnOwnAgent) TeleportOwnCharacter(teleport.PositionInGameUnits);
        if (Gw2.Client.Net.Game.ServerForcedMovement.DashOf(message) is { } dash) _pendingServerDash = dash;
    }

    private Vector3? _drawnPositionBeforeTeleport;
    private Vector3 _positionBlendOffset;
    private long? _positionBlendStartMilliseconds;

    private void BeginOwnPositionBlend(Vector3 drawnBefore)
    {
        _positionBlendOffset = drawnBefore - _characterPosition;
        _positionBlendStartMilliseconds = ClientMilliseconds();
        _drawnPositionBeforeTeleport = null;
    }

    private Vector3 OwnPositionBlendOffset()
    {
        if (_positionBlendStartMilliseconds is not { } start) return Vector3.Zero;
        var remaining = 1.0 - (ClientMilliseconds() - start) / Gw2.Client.Net.Game.AgentPositionBlends.BlendDuration.TotalMilliseconds;
        if (remaining <= 0.0)
        {
            _positionBlendStartMilliseconds = null;
            return Vector3.Zero;
        }
        return _positionBlendOffset * (float)remaining;
    }

    private void TeleportOwnCharacter(Vector3 position)
    {
        _ownStep?.StopMoveTo();
        _pendingServerMoveTo = null;
        _serverMoveToEndClock = null;
        _drawnPositionBeforeTeleport = DrawnCharacterPosition;
        var moved = position - _characterPosition;
        _characterPosition = position;
        MoveOwnCameraCentre(moved);
    }

    private void ApplyServerForcedMovement(Gw2.Client.Scene.Havok.Gw2CharacterStep character, uint ownAgentId)
    {
        if (_pendingServerJump is { } jump)
        {
            character.Jump(jump.WireVerticalVelocity, jump.KeepsLargeVelocity);
            _pendingServerJump = null;
        }
        if (_pendingServerDash is { } dash)
        {
            _pendingServerDash = null;
            character.StopMoveTo();
            _serverMoveToEndClock = null;
            _serverDash = dash;
            _serverDashEndClock = _ownStepClock + dash.DurationMilliseconds / 1000f;
            if (dash.FlatFacing != Vector2.Zero) _ownFacing = new Vector3(Vector2.Normalize(dash.FlatFacing), 0f);
        }
        if (_pendingServerMoveTo is not { } moveTo) return;
        _serverDash = null;
        _pendingServerMoveTo = null;
        character.MoveTo(Gw2.Client.Scene.Havok.Gw2CharacterStep.CollisionOf(moveTo.DestinationInWireUnits), moveTo.KeepsSteeringFlag);
        _serverMoveToSpeed = moveTo.SpeedInGameUnits > 0f ? moveTo.SpeedInGameUnits : OwnRunSpeed(ownAgentId);
        _serverMoveToEndClock = moveTo.DurationMilliseconds > 0 ? _ownStepClock + moveTo.DurationMilliseconds / 1000f : null;
    }

    private Gw2.Client.Net.Game.ServerMoveTo? _pendingServerMoveTo;
    private Gw2.Client.Net.Game.ServerJump? _pendingServerJump;
    private Gw2.Client.Net.Game.ServerDash? _pendingServerDash;
    private Gw2.Client.Net.Game.ServerDash? _serverDash;
    private float _serverDashEndClock;
    private float _serverMoveToSpeed;
    private float? _serverMoveToEndClock;

    private bool IsMovedByServer => _ownStep?.MovesToDestination == true || _serverDash is not null;

    private void FollowServerPositionOfOwnCharacter(DecodedMessage message)
    {
        if (message.MessageId != GameAgents.AgentPosition || !_placedOnOwnAgent || _login?.ControlledAgentId is not { } ownId) return;
        if (message.Fields[1].Value is not ulong agentId || agentId != ownId || !_world.Agents.TryGet(ownId, out var own)) return;
        var moved = own.PositionInGameUnits - _characterPosition;
        _characterPosition = own.PositionInGameUnits;
        MoveOwnCameraCentre(moved);
    }

    private void FollowGameAgents()
    {
        if (_login is null || _agentCapsules is null || _loginScreen is not null) return;
        var ownAgentId = _login.ControlledAgentId;
        if (!_placedOnOwnAgent && ownAgentId is { } id && _world.Agents.TryGet(id, out var own))
        {
            _placedOnOwnAgent = true;
            _characterPosition = own.PositionInGameUnits;
            FrameStartPosition();
            WriteGroundUnderCharacter();
        }
        if (_placedOnOwnAgent && ownAgentId is { } controlledId)
        {
            UseOwnAgentAppearance(controlledId);
            SelectTargetOnTab(controlledId);
            var clickedSelection = SelectTargetOnClick(controlledId);
            if (IsActive && _input.IsKeyPressed(Keys.OemTilde)) _weaponSwapNumber++;
            var movement = WalkOwnCharacter(controlledId) with { SkillUse = UseWeaponSkillOnNumberKeys(controlledId), WeaponSwapNumber = _weaponSwapNumber, HeldGroundReticleInGameUnits = HeldGroundReticle(), ClickedSelection = clickedSelection, WaypointTravel = TravelToNearestWaypointWhileDefeated(controlledId) };
            _login.ReportOwnPosition(_characterPosition, movement, OwnSpeed(controlledId, movement));
            TrackOwnCast(controlledId, movement.SkillUse);
            _hudState = _hudStateModel.StateOf(controlledId, ClientMilliseconds()) with { CastBar = OwnCastBarState() };
            if (_perfOverlay is { } overlay) overlay.Hud.State = DebugHudStateOf(controlledId);
        }
        _sceneCharacters.Clear();
        _agentCapsules.Clear();
        foreach (var agent in _world.Agents.All)
        {
            var isControlled = agent.AgentId == ownAgentId;
            var position = isControlled ? _characterPosition : agent.PositionInGameUnits;
            var facing = isControlled && _ownFacing is { } ownFacing ? ownFacing : agent.Facing;
            var visibleAttitude = VisibleAttitudeOf(agent, isControlled);
            _sceneCharacters.Add(new Gw2SceneCharacter(agent.AgentId, position, agent.HeightInGameUnits, facing, agent.RadiusInGameUnits, visibleAttitude, isControlled, _world.Agents.PlayerNameOf(agent) ?? (agent.CodedName is null ? _names.SpeciesNameOf(agent) : null), agent.CodedName));
            if (_perfOverlay?.ShowsColliderCapsules == true) _agentCapsules.Add(position, agent.HeightInGameUnits, agent.RadiusInGameUnits, Gw2NameColors.Of(visibleAttitude));
        }
        _sceneNamedObjects.Clear();
        foreach (var keyframed in _world.Agents.Keyframed)
        {
            if (_names.ItemNameOf(keyframed) is { } itemName)
                _sceneNamedObjects.Add(new Gw2SceneNamedObject(keyframed.AgentId, keyframed.PositionInGameUnits, itemName, Gw2AgentNameLabels.ItemNameplateRangeInGameUnits));
            else if (_names.GadgetNameOf(keyframed) is { } gadgetName)
                _sceneNamedObjects.Add(new Gw2SceneNamedObject(keyframed.AgentId, keyframed.PositionInGameUnits, gadgetName, Gw2AgentNameLabels.NameplateRangeInGameUnits));
        }
    }

    private IReadOnlyList<Gw2.Dat.Content.Gw2Waypoint> WaypointsOf(uint? worldMapId)
    {
        if (worldMapId is not { } mapId || _reader is null || _options.LoginExecutablePath is not { } executablePath) return [];
        var layouts = new Gw2.Dat.Content.Gw2MapLayouts(_reader.ContentIndex, Gw2.Formats.MapLayoutContentLayout.FromExecutable(executablePath));
        return layouts.FindWaypoints(mapId, Gw2.Formats.PointOfInterestContentLayout.FromExecutable(executablePath));
    }

    private Gw2.Dat.Content.Gw2Waypoint? NearestWaypoint()
    {
        if (_mapWaypoints.Count == 0) return null;
        return _mapWaypoints.MinBy(waypoint => Vector3.DistanceSquared(new Vector3(waypoint.X, waypoint.Y, waypoint.Z), _characterPosition));
    }

    private Gw2.Client.Net.Game.OwnWaypointTravel? TravelToNearestWaypointWhileDefeated(uint ownAgentId)
    {
        if (_world.LifeState.StateOf(ownAgentId) != Gw2.Client.Net.Game.LifeState.Defeated || NearestWaypoint() is not { } waypoint) return null;
        if (IsActive && _input.IsKeyPressed(Keys.R))
        {
            _waypointTravelNumber++;
            Console.WriteLine($"WAYPOINT travel to {waypoint.PointOfInterestDataId}");
        }
        return _waypointTravelNumber == 0 ? null : new Gw2.Client.Net.Game.OwnWaypointTravel(_waypointTravelNumber, waypoint.PointOfInterestDataId);
    }

    private string? OwnLifeStateLabelOf(uint ownAgentId)
    {
        var state = _world.LifeState.StateOf(ownAgentId);
        if (state == Gw2.Client.Net.Game.LifeState.Defeated && NearestWaypoint() is { } waypoint) return $"Defeated  (R: waypoint {waypoint.PointOfInterestDataId})";
        return state?.ToString();
    }

    private Gw2.Client.Net.Game.OwnMovementInput WalkOwnCharacter(uint ownAgentId)
    {
        if (!IsActive || _havokWorld is null || !_world.LifeState.AllowsMovement(ownAgentId, isInWater: false)) return default;
        var keyboard = _input.Keyboard;
        var hotkeys = HotkeysOf();
        var turnKeysStrafe = _input.IsMouseButtonDown(MouseButton.Right);
        var holdsForward = hotkeys.IsDown(_input, MoveForwardControl);
        var holdsBackward = hotkeys.IsDown(_input, MoveBackwardControl);
        var holdsTurnLeft = hotkeys.IsDown(_input, TurnLeftControl);
        var holdsTurnRight = hotkeys.IsDown(_input, TurnRightControl);
        var holdsStrafeLeft = hotkeys.IsDown(_input, StrafeLeftControl) || (turnKeysStrafe && holdsTurnLeft);
        var holdsStrafeRight = hotkeys.IsDown(_input, StrafeRightControl) || (turnKeysStrafe && holdsTurnRight);
        var heldMovementKeys = (holdsForward ? 1 : 0) | (holdsBackward ? 2 : 0) | (holdsStrafeLeft ? 4 : 0) | (holdsStrafeRight ? 8 : 0);
        var forward = (holdsForward ? 1f : 0f) - (holdsBackward ? 1f : 0f);
        var right = (holdsStrafeRight ? 1f : 0f) - (holdsStrafeLeft ? 1f : 0f);
        if (_actionCamera && forward == 0f && right == 0f && _input.GamePad.ThumbSticks.Left is var leftStick && leftStick != Vector2.Zero) (forward, right) = (leftStick.Y, leftStick.X);
        var turnLeft = turnKeysStrafe ? 0f : (holdsTurnLeft ? 1f : 0f) - (holdsTurnRight ? 1f : 0f);
        if (hotkeys.IsPressed(_input, JumpOrSwimUpControl) && !IsOwnCharacterAirborne && _dodge is null) _ownStepJumpPending = true;
        var swimVertical = SwimVerticalOf(_ownStep?.Context.Current, hotkeys.IsDown(_input, JumpOrSwimUpControl) || hotkeys.IsDown(_input, SwimUpControl), hotkeys.IsDown(_input, SwimDownControl));
        if (IsMovedByServer) (forward, right, turnLeft, swimVertical) = (0f, 0f, 0f, 0f);
        var isWalking = forward != 0f || right != 0f || swimVertical != 0f;
        var view = _camera.Target - _camera.Eye;
        if (turnLeft != 0f && _walkAhead is { } lastFacing)
        {
            var turned = MathF.Atan2(lastFacing.Y, lastFacing.X) + turnLeft * _frameElapsedSeconds * KeyboardTurnRadiansPerSecond;
            _walkAhead = new Vector2(MathF.Cos(turned), MathF.Sin(turned));
            _camera.Yaw = turned + MathF.PI;
        }
        else if (!IsOrbitingCameraOnly() || _walkAhead is null) _walkAhead = GroundFacingOf(view) ?? _walkAhead ?? Vector2.UnitX;
        var ahead = _walkAhead.Value;
        var side = new Vector2(-ahead.Y, ahead.X);
        var holdsDodge = hotkeys.IsDown(_input, DodgeControl);
        var dodgeReleased = _heldDodge && !holdsDodge;
        var dodgePressed = hotkeys.IsPressed(_input, DodgeControl) && !IsOwnCharacterAirborne && _dodge is null && _world.Endurance.CanDodge(ownAgentId);
        _heldDodge = holdsDodge;
        RunContentFunctions(ownAgentId, dodgePressed, dodgeReleased, out var dodgeTakenByContentFunction);
        if (!dodgeTakenByContentFunction && dodgePressed)
        {
            var characterFacing = _ownFacing is { } facing ? new Vector2(facing.X, facing.Y) : ahead;
            var dodgeDirection = isWalking ? Vector2.Normalize(ahead * forward - side * right) : -characterFacing;
            _dodge = new Gw2.Client.Net.Game.OwnDodge(_lastDodgeNumber + 1, DodgeDirectionIndexOf(forward, right), new Vector3(dodgeDirection, 0f));
            _lastDodgeNumber = _dodge.Value.Number;
            _dodgeRemainingSeconds = Gw2.Client.Net.Game.OwnCharacterMovement.DodgeMilliseconds / 1000f;
        }
        var movement = new Gw2.Client.Net.Game.OwnMovementInput(0, Vector3.Zero, _actionCamera ? new Vector3(ahead, 0f) : Vector3.Normalize(view));
        var step = Vector2.Zero;
        if (isWalking)
        {
            var direction = forward != 0f || right != 0f ? MovingOf(ahead, forward, right) : Vector2.Zero;
            var viewDirection = Vector3.Normalize(view);
            var direction3 = _ownStep is { } swimming && SteersInThreeDimensions(swimming.Context.Current, viewDirection) ? SwimDirectionOf(direction, ahead, viewDirection) : new Vector3(direction, 0f);
            direction3 = WithSwimVertical(direction3, swimVertical);
            if (_dodge is null) _ownFacing = direction3.Z != 0f ? direction3 : new Vector3(ahead, 0f);
            movement = movement with { HeldMovementKeys = heldMovementKeys, Direction = direction3 };
            step = direction * _frameElapsedSeconds;
        }
        if (_dodge is { } dodge)
        {
            movement = movement with { Dodge = dodge };
            var dodgeSeconds = MathF.Min(_frameElapsedSeconds, _dodgeRemainingSeconds);
            step = new Vector2(dodge.Direction.X, dodge.Direction.Y) * Gw2.Client.Net.Game.OwnCharacterMovement.DodgeSpeedInGameUnits * dodgeSeconds;
            _dodgeRemainingSeconds -= _frameElapsedSeconds;
            if (_dodgeRemainingSeconds <= 0f) _dodge = null;
        }
        var start = DrawnCharacterPosition;
        if (OwnCharacterStep(ownAgentId) is not { } character) return default;
        ApplyServerForcedMovement(character, ownAgentId);
        var walkSpeed = character.MovesToDestination ? _serverMoveToSpeed
            : _serverDash is { } dashing ? dashing.SpeedInGameUnits
            : movement.Dodge is not null ? Gw2.Client.Net.Game.OwnCharacterMovement.DodgeSpeedInGameUnits
            : isWalking ? OwnRunSpeed(ownAgentId) : 0f;
        var capFacing = movement.Dodge is not null ? (Vector3?)null : movement.Facing;
        StepOwnCharacter(character, step, walkSpeed, capFacing, movement.Direction);
        MoveOwnCameraCentre(DrawnCharacterPosition - start);
        if (!isWalking && !IsOwnCharacterAirborne && _dodge is null) return default(Gw2.Client.Net.Game.OwnMovementInput) with { Physics = OwnPhysicsStateOf(_ownStep) };
        return movement with { IsAirborne = IsOwnCharacterAirborne, Physics = OwnPhysicsStateOf(_ownStep) };
    }

    private static Gw2.Client.Net.Game.OwnPhysicsState? OwnPhysicsStateOf(Gw2.Client.Scene.Havok.Gw2CharacterStep? step) =>
        step is null ? null : new Gw2.Client.Net.Game.OwnPhysicsState(step.StateByte, step.MotionFlags);

    private const uint MoveForwardControl = 0;
    private const uint MoveBackwardControl = 1;
    private const uint TurnLeftControl = 4;
    private const uint TurnRightControl = 5;
    private const uint StrafeLeftControl = 2;
    private const uint StrafeRightControl = 3;
    private const uint DodgeControl = 6;
    private const uint JumpOrSwimUpControl = 9;
    private const uint SwimUpControl = 10;
    private const uint SwimDownControl = 11;
    private const uint ToggleActionCameraControl = 18;
    private const uint DisableActionCameraControl = 19;
    private const float KeyboardTurnRadiansPerSecond = 2.0943952f;

    private static Vector2? GroundFacingOf(Vector3 view)
    {
        if (RetailNormalized(view.X, view.Y, view.Z, 0.000001) is not { } facing || MathF.Abs(facing.Z) > 0.95f) return null;
        return RetailNormalized(facing.X, facing.Y, 0f, 0.000001) is { } ground ? new Vector2(ground.X, ground.Y) : null;
    }

    private static Vector2 MovingOf(Vector2 facing, float forward, float right)
    {
        var x = forward * facing.X;
        var y = forward * facing.Y;
        if (forward == 0f)
        {
            x = facing.Y * right;
            y = -facing.X * right;
        }
        else if (right != 0f)
        {
            x += facing.Y * right;
            y += -facing.X * right;
        }
        return RetailNormalized(x, y, 0f, 0.01) is { } moving ? new Vector2(moving.X, moving.Y) : facing;
    }

    private static Vector3? RetailNormalized(float x, float y, float z, double minimumLength)
    {
        double dx = x, dy = y, dz = z;
        var lengthSquared = dx * dx + dy * dy + dz * dz;
        if (minimumLength * minimumLength > lengthSquared) return null;
        var length = Math.Sqrt(lengthSquared);
        return new Vector3((float)(dx / length), (float)(dy / length), (float)(dz / length));
    }

    private Gw2.Client.Scene.Havok.Gw2CharacterStep? OwnCharacterStep(uint ownAgentId)
    {
        if (_havokWorld is null || !_world.Agents.TryGet(ownAgentId, out var own) || own.RadiusInGameUnits <= 0f) return null;
        var radius = own.RadiusInGameUnits / GameAgents.GameUnitsPerCollisionUnit;
        var height = own.HeightInGameUnits / GameAgents.GameUnitsPerCollisionUnit;
        var collisionPosition = Gw2.Client.Scene.Havok.Gw2CharacterStep.CollisionOf(_characterPosition);
        if (_ownStep is null || _ownStepWorld != _havokWorld || _ownStep.Radius != radius || _ownStep.Height != height)
        {
            _ownStep = new Gw2.Client.Scene.Havok.Gw2CharacterStep(_havokWorld, radius, height, collisionPosition)
            {
                Water = _mapWater,
                WaterSurfaceOffset = Gw2.Client.Scene.Havok.Gw2CharacterStep.WaterSurfaceOffsetOf(own.SwimDepthInGameUnits),
            };
            _ownStepWorld = _havokWorld;
            _ownStepSeconds = 0f;
            _ownStepClock = 0f;
        }
        else if (Gw2.Client.Scene.Havok.Gw2CharacterStep.GameOf(_ownStep.Position) != _characterPosition)
        {
            _ownStep.Proxy.Position = collisionPosition;
        }
        return _ownStep;
    }

    private const float SurfaceDiveTangent = 0.57735026f;
    private const float SteepestSwimHeading = 0.95f;

    private static bool SteersInThreeDimensions(Gw2.Client.Scene.Havok.Gw2CharacterStateType state, Vector3 view)
    {
        if (MathF.Abs(view.Z) > SteepestSwimHeading) return false;
        if (state == Gw2.Client.Scene.Havok.Gw2CharacterStateType.Diving) return true;
        return state == Gw2.Client.Scene.Havok.Gw2CharacterStateType.Swimming && view.Z >= SurfaceDiveTangent * new Vector2(view.X, view.Y).Length();
    }

    public static float SwimVerticalOf(Gw2.Client.Scene.Havok.Gw2CharacterStateType? state, bool holdsSwimUp, bool holdsSwimDown)
    {
        if (state is not (Gw2.Client.Scene.Havok.Gw2CharacterStateType.Diving or Gw2.Client.Scene.Havok.Gw2CharacterStateType.Swimming)) return 0f;
        var vertical = (holdsSwimDown ? 1f : 0f) - (holdsSwimUp ? 1f : 0f);
        return state == Gw2.Client.Scene.Havok.Gw2CharacterStateType.Swimming ? MathF.Max(vertical, 0f) : vertical;
    }

    public static Vector3 WithSwimVertical(Vector3 direction, float vertical) =>
        vertical == 0f ? direction : Vector3.Normalize(direction with { Z = vertical });

    private static Vector3 SwimDirectionOf(Vector2 direction, Vector2 ahead, Vector3 view)
    {
        var alongAhead = Vector2.Dot(direction, ahead);
        var sideways = direction - ahead * alongAhead;
        var lifted = view * alongAhead + new Vector3(sideways, 0f);
        return lifted.LengthSquared() > 1e-6f ? Vector3.Normalize(lifted) : new Vector3(direction, 0f);
    }

    private void StepOwnCharacter(Gw2.Client.Scene.Havok.Gw2CharacterStep character, Vector2 step, float walkSpeed, Vector3? facing, Vector3 heldDirection)
    {
        var dash = _serverDash;
        var holdsMovement = dash is not null || (walkSpeed > 0f && (step != Vector2.Zero || heldDirection.Z != 0f));
        var dashFlat = dash is { } dashing ? new Vector2(dashing.CollisionDirection.X, dashing.CollisionDirection.Y) : Vector2.Zero;
        var direction = dashFlat != Vector2.Zero ? Vector2.Normalize(dashFlat)
            : step != Vector2.Zero ? Vector2.Normalize(step) : _ownStepForward;
        var moveInGame = holdsMovement && heldDirection.Z != 0f && heldDirection.LengthSquared() > 0.5f ? heldDirection : new Vector3(direction, 0f);
        var move = dash is { } dashed
            ? System.Runtime.Intrinsics.Vector128.Create(dashed.CollisionDirection.X, dashed.CollisionDirection.Y, dashed.CollisionDirection.Z, 0f)
            : System.Runtime.Intrinsics.Vector128.Create(moveInGame.X, moveInGame.Y, -moveInGame.Z, 0f);
        if (facing is null) character.Flags &= ~Gw2.Client.Scene.Havok.Gw2CharacterStep.SpeedCappedByFacingFlag;
        else character.Flags |= Gw2.Client.Scene.Havok.Gw2CharacterStep.SpeedCappedByFacingFlag;
        var flatFacing = facing is { } faced ? Gw2.Client.Scene.Havok.Gw2CharacterStep.FlatFacingOf(faced) : move;
        _ownStepSeconds += _frameElapsedSeconds;
        while (_ownStepSeconds >= Gw2.Client.Scene.Havok.Gw2CharacterStep.StepSeconds)
        {
            _ownStepSeconds -= Gw2.Client.Scene.Havok.Gw2CharacterStep.StepSeconds;
            _ownStepClock += Gw2.Client.Scene.Havok.Gw2CharacterStep.StepSeconds;
            if (_serverDash is not null && _ownStepClock >= _serverDashEndClock)
            {
                _serverDash = null;
                holdsMovement = false;
                walkSpeed = 0f;
            }
            if (_serverMoveToEndClock is { } endClock && _ownStepClock >= endClock)
            {
                character.StopMoveTo();
                _serverMoveToEndClock = null;
            }
            _positionBeforeOwnStep = Gw2.Client.Scene.Havok.Gw2CharacterStep.GameOf(character.Position);
            character.Step(_ownStepClock, holdsMovement ? move : default, flatFacing, walkSpeed / GameAgents.GameUnitsPerCollisionUnit, 0f, holdsMovement, _ownStepJumpPending);
            _ownStepJumpPending = false;
            if (holdsMovement) _ownStepForward = direction;
        }
        _characterPosition = Gw2.Client.Scene.Havok.Gw2CharacterStep.GameOf(character.Position);
        _positionAfterOwnStep = _characterPosition;
        _ownLocaleState = OwnContentFunctionHost.LocaleStateOf(character.Context.Current, -System.Runtime.Intrinsics.Vector128.GetElement(character.Velocity, 2), _ownLocaleState);
        _ownAirborne = character.Context.Current is Gw2.Client.Scene.Havok.Gw2CharacterStateType.InAir or Gw2.Client.Scene.Havok.Gw2CharacterStateType.Jumping;
    }

    private Vector3 _positionBeforeOwnStep;
    private Vector3 _positionAfterOwnStep;

    private Vector3 DrawnCharacterPosition => SteppedCharacterPosition + OwnPositionBlendOffset();

    private Vector3 SteppedCharacterPosition =>
        _ownStep is not null && _positionAfterOwnStep == _characterPosition
            ? Vector3.Lerp(_positionBeforeOwnStep, _positionAfterOwnStep, Math.Clamp(_ownStepSeconds / Gw2.Client.Scene.Havok.Gw2CharacterStep.StepSeconds, 0f, 1f))
            : _characterPosition;

    private Gw2.Client.Scene.Havok.Gw2HavokMapWorld? _havokWorld;
    private Gw2.Client.Scene.Havok.Gw2CharacterStep? _ownStep;
    private float _ownStepSeconds;
    private float _ownStepClock;
    private Vector2 _ownStepForward = Vector2.UnitX;
    private bool _ownStepJumpPending;
    private Gw2.Client.Scene.Havok.Gw2HavokMapWorld? _ownStepWorld;
    private Gw2.Client.Scene.Havok.Gw2CameraCollision? _cameraCollision;
    private Gw2.Client.Scene.Havok.Gw2HavokMapWorld? _cameraCollisionWorld;

    private Vector3 PullCameraInFrontOfCollision()
    {
        if (_havokWorld is null) return _ownCameraDesiredCentre;
        if (_cameraCollision is null || _cameraCollisionWorld != _havokWorld) (_cameraCollision, _cameraCollisionWorld) = (new Gw2.Client.Scene.Havok.Gw2CameraCollision(_havokWorld), _havokWorld);
        var viewport = GraphicsDevice.Viewport;
        var sphere = Gw2.Client.Scene.Havok.Gw2CameraCollision.EyeSphereOf(_camera.FieldOfView, viewport.AspectRatio, _camera.NearPlane);
        var anchor = _ownCameraDesiredCentre;
        var fromCentre = _camera.Eye - _camera.Target;
        var lookBack = fromCentre.LengthSquared() >= 1e-12f ? Vector3.Normalize(fromCentre) : Vector3.Zero;
        var desiredEye = anchor + lookBack * _ownCameraDesiredDistance;
        var collidedCentre = _login?.ControlledAgentId is { } ownId && _world.Agents.TryGet(ownId, out var own)
            ? _cameraCollision.CollideCentre(_characterPosition, own.RadiusInGameUnits, own.HeightInGameUnits, anchor)
            : anchor;
        var collisionSensitivity = OwnCameraPreferences.CollisionSensitivity;
        var (eye, centre) = _cameraCollision.PlaceEyeAndCentre(anchor, collidedCentre, desiredEye, sphere, _camera.Distance, collisionSensitivity);
        _camera.Distance = MathF.Min(_camera.Distance, Vector3.Distance(eye, centre));
        return centre;
    }

    private bool _ownAirborne;
    private byte _ownLocaleState;

    private sealed class OwnContentFunctionHost(Gw2Client client, uint ownAgentId) : IContentFunctionHost
    {
        private const byte GroundLocaleState = 3;
        private const byte RisingLocaleState = 4;
        private const byte FallingLocaleState = 5;
        private const byte UnderwaterLocaleState = 9;
        private const byte SurfaceLocaleState = 14;
        private const byte MountedLocaleState = 0;

        public static byte LocaleStateOf(Gw2.Client.Scene.Havok.Gw2CharacterStateType state, float upwardVelocity, byte previous) => state switch
        {
            Gw2.Client.Scene.Havok.Gw2CharacterStateType.OnGround => GroundLocaleState,
            Gw2.Client.Scene.Havok.Gw2CharacterStateType.InAir => upwardVelocity > 0 || previous == RisingLocaleState ? RisingLocaleState : FallingLocaleState,
            Gw2.Client.Scene.Havok.Gw2CharacterStateType.Jumping => FallingLocaleState,
            Gw2.Client.Scene.Havok.Gw2CharacterStateType.Diving => UnderwaterLocaleState,
            Gw2.Client.Scene.Havok.Gw2CharacterStateType.Swimming => SurfaceLocaleState,
            Gw2.Client.Scene.Havok.Gw2CharacterStateType.Mounted => MountedLocaleState,
            _ => GroundLocaleState,
        };

        public byte LocaleState => client._ownLocaleState;

        public bool HasBuff(uint buffDataId, bool onSelf) => onSelf && client._world.Buffs.SkillIdsOf(ownAgentId).Contains(buffDataId);

        public bool HasMovementModifier(uint movementModifierUid) => client._world.ContentFunctions.HasMovementModifier(ownAgentId, movementModifierUid);

        public void RemoveMovementModifier(uint movementModifierUid) => client._world.ContentFunctions.RemoveMovementModifier(ownAgentId, movementModifierUid);

        public void ApplyMovementModifier(uint movementModifierUid, uint? durationMilliseconds) =>
            client._world.ContentFunctions.ApplyMovementModifier(ownAgentId, movementModifierUid, Environment.TickCount64 + durationMilliseconds);

        public float Endurance => client._world.Endurance.PointsOf(ownAgentId) ?? 0;

        public float MaximumEndurance => client._world.Endurance.MaximumOf(ownAgentId) ?? 0;

        public float Energy(uint kind) => client._world.Energies.ValueOf(ownAgentId, kind, Environment.TickCount64);

        public uint AdvancedMovementInputFlags => client._contentFunctionEffects.AdvancedMovementInputFlags;

        public void Perform(ContentFunctionEffectNode effect)
        {
            if (!client.PerformEnergyEffect(ownAgentId, effect)) client._contentFunctionEffects.Perform(effect);
        }

        public void SetAdvancedMovementInput(uint input, bool active) => client._contentFunctionEffects.SetAdvancedMovementInput(input, active);

        public void StartEnergyDrain(uint kind, float drainPerSecond)
        {
            var meter = client._world.Energies.MeterFor(ownAgentId, kind, Environment.TickCount64);
            meter.SetChangePerSecond(meter.ChangePerSecond - drainPerSecond);
        }

        public void StopEnergyDrain(uint kind, float drainPerSecond, bool restoresDefaultRate)
        {
            var meter = client._world.Energies.MeterFor(ownAgentId, kind, Environment.TickCount64);
            if (restoresDefaultRate) meter.RestoreDefaultRate();
            else meter.SetChangePerSecond(meter.ChangePerSecond + drainPerSecond);
        }

        public void ApplyForce(float x, float y, float z) => client._contentFunctionEffects.ApplyForce(x, y, z);
    }

    private readonly OwnContentFunctionEffects _contentFunctionEffects = new();
    private readonly Dictionary<uint, (Vector3 Position, float EnergyPerDistance)> _energyChangeByDistance = [];

    private bool PerformEnergyEffect(uint ownAgentId, ContentFunctionEffectNode effect)
    {
        var kind = effect.Word(ContentFunctionArgument.EnergyKind);
        var now = Environment.TickCount64;
        var energies = _world.Energies;
        Gw2.Client.Net.Game.EnergyMeter Meter() => energies.MeterFor(ownAgentId, kind, now);
        float Source() => energies.ValueOf(ownAgentId, effect.Word(ContentFunctionArgument.SourceEnergyKind), now);
        switch (effect.Kind)
        {
            case ContentFunctionOpcode.EnergyAdjust:
                Meter().Adjust(effect.Single(ContentFunctionArgument.Amount));
                return true;
            case ContentFunctionOpcode.EnergyAdjustFromOtherKind:
                Meter().Adjust(Source() * effect.Single(ContentFunctionArgument.Scale));
                return true;
            case ContentFunctionOpcode.EnergyAdjustFromOtherKindAboveOffset:
                Meter().Adjust((Source() - effect.Single(ContentFunctionArgument.Offset)) * effect.Single(ContentFunctionArgument.Scale));
                return true;
            case ContentFunctionOpcode.EnergyRatePause:
                Meter().SetPaused(true);
                return true;
            case ContentFunctionOpcode.EnergyRateResume:
                Meter().SetPaused(false);
                return true;
            case ContentFunctionOpcode.EnergyRateSet:
                Meter().SetChangePerSecond(effect.Single(ContentFunctionArgument.Amount));
                return true;
            case ContentFunctionOpcode.EnergyRateSetDefault:
                Meter().SetDefaultChangePerSecond(effect.Single(ContentFunctionArgument.Amount));
                return true;
            case ContentFunctionOpcode.EnergyRateRestoreDefault:
                Meter().RestoreDefaultRate();
                return true;
            case ContentFunctionOpcode.StartEnergyChangeByDistance:
                _energyChangeByDistance[kind] = (_characterPosition, effect.Single(ContentFunctionArgument.Scale));
                return true;
            case ContentFunctionOpcode.StopEnergyChangeByDistance:
                _energyChangeByDistance.Remove(kind);
                return true;
            default:
                return false;
        }
    }

    private void ApplyEnergyDefaults(Gw2CharacterConfigs configs)
    {
        _world.Energies.DefaultMaximum = configs.Number(CharacterConfigTable.EnergyMaximumConfig) ?? 0;
        _world.Energies.DefaultChangePerSecond = configs.Number(CharacterConfigTable.EnergyDefaultChangePerSecondConfig) ?? 0;
    }

    private void ApplyEnergyChangeByDistance(uint ownAgentId, long now)
    {
        foreach (var (kind, (position, energyPerDistance)) in _energyChangeByDistance.ToArray())
        {
            _world.Energies.MeterFor(ownAgentId, kind, now).Adjust(Vector3.Distance(position, _characterPosition) * energyPerDistance);
            _energyChangeByDistance[kind] = (_characterPosition, energyPerDistance);
        }
    }

    private void ApplyContentFunctionEffects(uint ownAgentId, long now)
    {
        ApplyEnergyChangeByDistance(ownAgentId, now);
        if (!_contentFunctionEffects.JumpRequested) return;
        _contentFunctionEffects.JumpRequested = false;
        _ownStepJumpPending = true;
    }
    private bool IsOwnCharacterAirborne => _ownAirborne || _ownStepJumpPending;
    private Gw2.Client.Net.Game.OwnDodge? _dodge;
    private int _lastDodgeNumber;
    private bool _heldDodge;
    private Gw2.Client.Net.Game.OwnTargetSelection? _targetSelection;
    private const float TabTargetRangeInGameUnits = 1200f;

    private uint? _heldGroundSkillControl;
    private Hotkeys? _hotkeys;
    private bool _hotkeysEquipped;

    private Hotkeys HotkeysOf()
    {
        if (_hotkeys is null)
        {
            _hotkeys = new Hotkeys(new Gw2InputActions(_reader!.ContentIndex, InputBindingLayout.Current).Defaults());
            ImportInputBindingLoadout();
        }
        if (!_hotkeysEquipped && _login?.AccountPreferencePack is { } preferencePack)
        {
            _hotkeysEquipped = true;
            _hotkeys.EquipFromPreferencePack(preferencePack, InputBindingLayout.Current);
            ImportInputBindingLoadout();
        }
        return _hotkeys;
    }

    private void ImportInputBindingLoadout()
    {
        if (_options.InputBindingLoadoutPath is { } loadoutPath) _hotkeys!.ImportLoadout(loadoutPath, InputBindingLayout.Current);
    }
    private const byte FirstMechanicSkillSlot = 12;
    private OwnSkillBar? _ownSkillBar;

    private OwnSkillBar OwnSkillBarOf() => _ownSkillBar ??= new OwnSkillBar(_reader!.ContentIndex, _world);
    private Gw2.Client.Net.Game.OwnSkillUse? _skillUse;
    private Gw2SkillCastBar? _skillCastBar;
    private readonly Gw2CastProgress _ownCast = new();
    private int? _castSkillUseNumber;
    private uint? _castOrderId;
    private const double CastMillisecondsPerSecond = 1000.0;

    private Gw2SkillCastBar SkillCastBarOf() =>
        _skillCastBar ??= new Gw2SkillCastBar(_reader!.ContentIndex, SkillPaletteContentLayout.Current, SkillCastBarLayout.Current, _reader.ReadAnimSequences()!);

    private void TrackOwnCast(uint ownAgentId, Gw2.Client.Net.Game.OwnSkillUse? skillUse)
    {
        var nowSeconds = ClientMilliseconds() / CastMillisecondsPerSecond;
        if (skillUse is { } use && use.Number != _castSkillUseNumber)
        {
            _castSkillUseNumber = use.Number;
            if (SkillCastBarOf().TimingOf(use.SkillId) is { Widget: Gw2SkillProgressWidget.Warmup or Gw2SkillProgressWidget.Channel or Gw2SkillProgressWidget.Charge } timing)
            {
                _ownCast.Begin(use.Slot, use.SkillId, timing.AtActivationSpeed(_world.ActivationSpeeds.SpeedOf(ownAgentId)), nowSeconds);
                _castOrderId = _world.SkillOrders.OrderOf(ownAgentId)?.OrderId;
            }
        }
        _ownCast.Update(nowSeconds);
        if (_world.SkillOrders.OrderOf(ownAgentId) is not { } order || order.SkillId != _ownCast.SkillId || order.OrderId == _castOrderId) return;
        _ownCast.ReachChargeLevel(order.ChargeLevel, nowSeconds);
        if (!order.IsStopped) return;
        _castOrderId = order.OrderId;
        _ownCast.Stop(nowSeconds);
    }

    private Gw2.Client.Hud.HudCastBarState OwnCastBarState()
    {
        var nowSeconds = ClientMilliseconds() / CastMillisecondsPerSecond;
        return _ownCast.IsVisibleAt(nowSeconds) ? new(_ownCast.SegmentsAt(nowSeconds), _ownCast.OpacityAt(nowSeconds)) : new([], 0f);
    }

    private Gw2.Client.Net.Game.OwnSkillUse? UseWeaponSkillOnNumberKeys(uint ownAgentId)
    {
        if (!IsActive || IsKeybindWindowOpen) return _skillUse;
        var hotkeys = HotkeysOf();
        int pressed = hotkeys.PressedIndexOf(_input, Hotkeys.WeaponSkillControls);
        int pressedBarSlot = hotkeys.PressedIndexOf(_input, Hotkeys.BarSkillControls);
        int pressedMechanic = hotkeys.PressedIndexOf(_input, Hotkeys.MechanicControls);
        if (pressed < 0 && pressedBarSlot < 0 && pressedMechanic < 0) return _skillUse;
        var bar = OwnSkillBarOf();
        if (bar.SelectionOf(ownAgentId) is not var (state, selection)) return _skillUse;
        if (pressedMechanic >= 0)
        {
            bool swapsLegend = Gw2OwnSkills.SwapsLegend(state, pressedMechanic);
            uint? mechanicSkillId = bar.MechanicSkillOf(state, pressedMechanic);
            _skillUse = new Gw2.Client.Net.Game.OwnSkillUse(
                (_skillUse?.Number ?? 0) + 1,
                (byte)(FirstMechanicSkillSlot + pressedMechanic),
                mechanicSkillId ?? Gw2.Client.Net.Game.OwnSkillUse.EmptySlot,
                swapsLegend && mechanicSkillId is not null ? ownAgentId : 0,
                ReleasesAtOnce: swapsLegend);
            return _skillUse;
        }
        if (pressedBarSlot >= 0)
        {
            if (bar.BarSkillOf(ownAgentId, pressedBarSlot, state, selection) is not { } barSkillId) return _skillUse;
            _skillUse = new Gw2.Client.Net.Game.OwnSkillUse((_skillUse?.Number ?? 0) + 1, (byte)pressedBarSlot, barSkillId, 0, bar.Palettes.IsGroundTargeted(barSkillId) ? CursorGroundPoint() : null);
            _heldGroundSkillControl = _skillUse.Value.GroundPointInGameUnits is null ? null : Hotkeys.BarSkillControls[pressedBarSlot];
            return _skillUse;
        }
        var skills = bar.WeaponSkillsOf(ownAgentId, state, selection);
        if (skills[pressed] is not { } skillId) return _skillUse;
        Vector3? groundPoint = null;
        if (bar.Palettes.IsGroundTargeted(skillId))
        {
            if (CursorGroundPoint() is not { } cursorGround) return _skillUse;
            groundPoint = cursorGround;
        }
        _skillUse = new Gw2.Client.Net.Game.OwnSkillUse((_skillUse?.Number ?? 0) + 1, (byte)(OwnSkillBar.FirstWeaponBarSlot + pressed), skillId, groundPoint is null ? _targetSelection?.TargetAgentId ?? 0 : 0, groundPoint);
        _heldGroundSkillControl = groundPoint is null ? null : Hotkeys.WeaponSkillControls[pressed];
        return _skillUse;
    }

    private Vector3? HeldGroundReticle()
    {
        if (_heldGroundSkillControl is not { } control) return null;
        if (!IsActive || !HotkeysOf().IsDown(_input, control))
        {
            _heldGroundSkillControl = null;
            return null;
        }
        return CursorGroundPoint() ?? _skillUse?.GroundPointInGameUnits;
    }

    private Vector3? CursorGroundPoint() => CursorRay() is { } ray ? SurfaceBetween(ray.Near, ray.Far) : null;

    private (Matrix4x4 WorldToClip, Vector2 Cursor, Vector3 Near, Vector3 Far)? CursorRay()
    {
        var viewport = GraphicsDevice.Viewport;
        var worldToClip = _camera.View * _camera.Projection(viewport.AspectRatio);
        if (!Matrix4x4.Invert(worldToClip, out var clipToWorld)) return null;
        var mouse = _input.Mouse;
        var x = 2f * mouse.X / GraphicsDevice.BackBufferWidth - 1f;
        var y = 1f - 2f * mouse.Y / GraphicsDevice.BackBufferHeight;
        var first = Vector4.Transform(new Vector4(x, y, 0f, 1f), clipToWorld);
        var second = Vector4.Transform(new Vector4(x, y, 1f, 1f), clipToWorld);
        var a = new Vector3(first.X, first.Y, first.Z) / first.W;
        var b = new Vector3(second.X, second.Y, second.Z) / second.W;
        var (near, far) = Vector3.DistanceSquared(a, _camera.Eye) <= Vector3.DistanceSquared(b, _camera.Eye) ? (a, b) : (b, a);
        return (worldToClip, new Vector2(x, y), near, far);
    }

    private uint? HoveredAgent(uint ownAgentId)
    {
        if (CursorRay() is not { } ray) return null;
        var hitDistance = SurfaceBetween(ray.Near, ray.Far) is { } hit ? Vector3.Distance(ray.Near, hit) : float.PositiveInfinity;
        var candidates = _world.Agents.All
            .Where(agent => agent.AgentId != ownAgentId)
            .Select(agent => new Gw2.Client.Net.Game.PickCandidate(agent.AgentId, agent.PositionInGameUnits, agent.HeightInGameUnits, agent.RadiusInGameUnits));
        return Gw2.Client.Net.Game.OwnAgentPicking.HoveredAgent(candidates, ray.WorldToClip, ray.Cursor, new Gw2.Client.Net.Game.PickRay(ray.Near, ray.Far, ray.Near, hitDistance), _characterPosition, _targetSelection?.TargetAgentId);
    }

    private Gw2.Client.Net.Game.OwnTargetSelection? SelectTargetOnClick(uint ownAgentId)
    {
        if (!IsActive) return _clickedSelection;
        var pressed = _input.IsMouseButtonPressed(MouseButton.Left);
        var released = _input.IsMouseButtonReleased(MouseButton.Left);
        if (!pressed && !released) return _clickedSelection;
        var hovered = HoveredAgent(ownAgentId);
        var cursor = new Vector2(_input.Mouse.X, _input.Mouse.Y);
        var milliseconds = ClientMilliseconds();
        if (pressed) _clickSelection.Press(hovered, cursor, milliseconds);
        if (!released || !_clickSelection.TryRelease(hovered, cursor, milliseconds, GraphicsDevice.BackBufferWidth, out var selected)) return _clickedSelection;
        if (selected is null && _targetSelection is null) return _clickedSelection;
        _targetSelection = selected is { } agentId ? new Gw2.Client.Net.Game.OwnTargetSelection((_targetSelection?.Number ?? 0) + 1, agentId) : null;
        _clickedSelection = new Gw2.Client.Net.Game.OwnTargetSelection((_clickedSelection?.Number ?? 0) + 1, selected ?? 0);
        return _clickedSelection;
    }

    private static uint? LifeStateAgentOf(DecodedMessage message) =>
        message.MessageId is Gw2.Client.Net.Game.AgentLifeState.AgentData or Gw2.Client.Net.Game.AgentLifeState.AgentCommandData or Gw2.Client.Net.Game.AgentLifeState.HealthCreate && message.Fields.Count > 1
            ? (uint)Convert.ToUInt64(message.Fields[1].Value)
            : null;

    private void LogLifeStateMessage(DecodedMessage message, Gw2.Client.Net.Game.LifeState? before)
    {
        if (LifeStateAgentOf(message) is not { } agentId || agentId != _login?.ControlledAgentId) return;
        var fields = string.Join(" ", message.Fields.Skip(1).Select(field => field.Value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(field.Value, System.Globalization.CultureInfo.InvariantCulture)));
        Console.WriteLine($"LIFE 0x{message.MessageId:X} agent {agentId} [{fields}] {before?.ToString() ?? "untracked"} -> {_world.LifeState.StateOf(agentId)?.ToString() ?? "untracked"}");
    }

    private static readonly int[] SkillbarAssignmentMessages = [Gw2.Client.Net.Game.OwnSkillbar.SlotPalette, 0x2AE, 0x2AF];

    private static void LogSkillbarAssignment(DecodedMessage message)
    {
        if (Array.IndexOf(SkillbarAssignmentMessages, message.MessageId) < 0) return;
        var fields = string.Join(" ", message.Fields.Skip(1).Select(field => field.Value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(field.Value, System.Globalization.CultureInfo.InvariantCulture)));
        Console.WriteLine($"SKILLBAR 0x{message.MessageId:X} [{fields}]");
    }

    private long ClientMilliseconds() => (long)System.Diagnostics.Stopwatch.GetElapsedTime(_startupTimestamp).TotalMilliseconds;

    private Gw2.Client.Ui.UiDebugHudState DebugHudStateOf(uint ownAgentId) =>
        _hudStateModel.DebugStateOf(ownAgentId, OwnLifeStateLabelOf(ownAgentId), _targetSelection?.TargetAgentId, IsActive ? HoveredAgent(ownAgentId) : null, ClientMilliseconds());

    private readonly Gw2.Client.Net.Game.OwnClickSelection _clickSelection = new();
    private Gw2.Client.Net.Game.OwnTargetSelection? _clickedSelection;

    private Gw2.Client.Net.Game.OwnTargetSelection? SelectTargetOnTab(uint ownAgentId)
    {
        if (!IsActive || !_input.IsKeyPressed(Keys.Tab)) return _targetSelection;
        var byDistance = _world.Agents.All
            .Where(agent => agent.AgentId != ownAgentId && Vector3.Distance(agent.PositionInGameUnits, _characterPosition) <= TabTargetRangeInGameUnits)
            .OrderBy(agent => Vector3.Distance(agent.PositionInGameUnits, _characterPosition))
            .Select(agent => agent.AgentId)
            .ToList();
        if (byDistance.Count == 0) return _targetSelection;
        var current = _targetSelection is { } selected ? byDistance.IndexOf(selected.TargetAgentId) : -1;
        var next = byDistance[(current + 1) % byDistance.Count];
        _targetSelection = new Gw2.Client.Net.Game.OwnTargetSelection((_targetSelection?.Number ?? 0) + 1, next);
        return _targetSelection;
    }
    private float _dodgeRemainingSeconds;

    private static int DodgeDirectionIndexOf(float forward, float right) => (forward, right) switch
    {
        ( < 0f, < 0f) => 1,
        ( < 0f, > 0f) => 2,
        ( < 0f, _) => 0,
        ( > 0f, < 0f) => 4,
        ( > 0f, > 0f) => 5,
        ( > 0f, _) => 3,
        (_, < 0f) => 6,
        (_, > 0f) => 7,
        _ => 0,
    };

    private float OwnSpeed(uint ownAgentId, Gw2.Client.Net.Game.OwnMovementInput movement)
    {
        var runSpeed = OwnRunSpeed(ownAgentId);
        if (movement.Direction == Vector3.Zero || movement.Dodge is not null) return runSpeed;
        var moveDirection = System.Runtime.Intrinsics.Vector128.Create(movement.Direction.X, movement.Direction.Y, -movement.Direction.Z, 0f);
        var flatFacing = Gw2.Client.Scene.Havok.Gw2CharacterStep.FlatFacingOf(movement.Facing);
        return Gw2.Client.Scene.Havok.Gw2CharacterStep.CappedSpeedOf(flatFacing, moveDirection, runSpeed / GameAgents.GameUnitsPerCollisionUnit) * GameAgents.GameUnitsPerCollisionUnit;
    }

    private float OwnRunSpeed(uint ownAgentId) =>
        _world.Agents.TryGet(ownAgentId, out var own) && own.TravelSpeed > 0f ? own.TravelSpeed : _defaultRunSpeed ??= CharacterMovementLayout.Current.DefaultRunSpeedInGameUnits;

    private static Gw2VisibleAttitude VisibleAttitudeOf(GameAgent agent, bool isControlled) => isControlled
        ? Gw2VisibleAttitude.Controlled
        : agent.PlayerId is not null && agent.Attitude is GameAttitude.Friendly or GameAttitude.Neutral
            ? Gw2VisibleAttitude.Player
            : agent.Attitude switch
            {
                GameAttitude.Friendly => Gw2VisibleAttitude.Friendly,
                GameAttitude.Hostile => Gw2VisibleAttitude.Hostile,
                GameAttitude.Indifferent => Gw2VisibleAttitude.Indifferent,
                _ => Gw2VisibleAttitude.Neutral,
            };

    private float AnimationSeconds(GameTime gameTime) => _options.Debug.StillScene ? 0f : (float)gameTime.ElapsedGameTime.TotalSeconds;

    private void UpdateContexts(float elapsedSeconds, float animationSeconds)
    {
        var stepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _frameElapsedSeconds = elapsedSeconds;
        _world.Agents.Advance(elapsedSeconds);
        FollowGameAgents();
        var isUiMouse = _perfOverlay is { WantsMouse: true };
        var ownsCamera = _options.Benchmark is { RecordSeconds: null, Flight: null };
        if (ownsCamera && IsActive && !isUiMouse && _placedOnOwnAgent) UpdateActionCameraToggle();
        var holdsLookButton = _input.IsMouseButtonDown(MouseButton.Left) || _input.IsMouseButtonDown(MouseButton.Right);
        CaptureMouse(ownsCamera && IsActive && !isUiMouse && (holdsLookButton || (_actionCamera && _placedOnOwnAgent)));
        if (ownsCamera && IsActive && !isUiMouse && (IsDraggingCamera() || (_actionCamera && _placedOnOwnAgent)))
        {
            var lookMovement = _mouseCaptured ? _input.RelativeMovement : _input.MouseMovement;
            if (_placedOnOwnAgent) LookAroundOwnCharacter(new Vector2(lookMovement.X, lookMovement.Y));
            else _camera.Orbit(-lookMovement.X * 0.01f, lookMovement.Y * 0.01f);
        }
        var wheelDelta = _input.ScrollNotches * Mouse.WheelDelta;
        if (ownsCamera && wheelDelta != 0 && !isUiMouse)
        {
            _secondsSinceCameraLook = 0f;
            if (_placedOnOwnAgent) _ownCameraDesiredDistance = Gw2WorldCameraMotion.ZoomedDistance(_ownCameraDesiredDistance, _input.ScrollNotches, OwnCameraPreferences.ZoomSpeedOption, Gw2WorldCameraMotion.DefaultMinimumDistance, Gw2WorldCameraMotion.DefaultMaximumDistance);
            else _camera.Zoom(MathF.Pow(0.999f, wheelDelta));
        }

        _secondsSinceCameraLook += elapsedSeconds;
        _secondsSinceCameraReset += elapsedSeconds;
        if (ownsCamera && _placedOnOwnAgent && !_actionCamera) SwingCameraBehindOwnCharacter(elapsedSeconds);
        _followsOwnCameraCentre = ownsCamera && _placedOnOwnAgent;
        if (ownsCamera && _placedOnOwnAgent) _camera.Distance = Gw2WorldCameraMotion.EaseDistance(_camera.Distance, _ownCameraDesiredDistance, Gw2WorldCameraMotion.FollowDistanceRatePerSecond, elapsedSeconds);
        if (_options.Scene.FieldOfView is null && _placedOnOwnAgent) _camera.FieldOfView = Gw2WorldCameraMotion.FieldOfViewOf(OwnCameraPreferences.FieldOfViewDegrees);
        _camera.NearPlane = Gw2.Client.Scene.Gw2CameraProjection.NearPlaneOf(_cameraViewDistance, _camera.FieldOfView);
        var followedCentre = ownsCamera && _placedOnOwnAgent ? PullCameraInFrontOfCollision() : _ownCameraDesiredCentre;
        if (_followsOwnCameraCentre && _actionCamera) _camera.Target = followedCentre;
        else if (_followsOwnCameraCentre) _camera.Target = Gw2.Client.Scene.Havok.Gw2CameraCentreLineOfSight.FollowCentre(_havokWorld, _camera.Target, followedCentre, _camera.Eye, followedCentre + (_camera.Eye - _camera.Target), Gw2WorldCameraMotion.SnapDistanceOf(OwnCameraSmoothingMode, OwnCameraPreferences.SnapsCloseToTheCharacter), Gw2WorldCameraMotion.DefaultCentreRatePerSecond, elapsedSeconds);
        else _ownCameraDesiredCentre = _camera.Target;

        if (_environment?.Wind is { } mapWind)
            _character?.Wind = _environmentWind.Advance(Gw2WindSettings.Of(mapWind), Environment.TickCount64 / 1000f, MathF.Min(1f, elapsedSeconds), 0f);
        stepStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _character?.Motion = OwnCharacterMotion();
        _character?.Update(animationSeconds);
        PlacePlayerModels(animationSeconds);
        _secondsSinceSceneSettled = IsSceneSettled ? _secondsSinceSceneSettled + elapsedSeconds : 0f;
        if (!_sceneFilled && IsSceneSettled)
        {
            _sceneFilled = true;
            MarkStartup("scene filled");
        }
        if (!_characterReady) return;
        _secondsSinceCharacterReady += elapsedSeconds;
        SkinCharacter();
    }

    partial void SkinCharacter();

    private void CompleteContent()
    {
        if (_characterReady || _character is null || !_character.IsSettled) return;
        if (_character.Error is { } error) throw new InvalidOperationException("Character content failed to decode.", error);

        var uploadSummary = "";
        UploadCharacter(ref uploadSummary);
        FrameCharacter();
        _characterReady = true;
        MarkStartup("character ready");
        var idleName = _character.IdleClip is { } idle ? Gw2AnimationRoles.Name(idle.Token) : "no idle";
        if (_login is null) Window.Title = $"Gw2 client - {_options.Content.RaceSexName} - {_character.Parts.Count} parts{uploadSummary}, {idleName}";
    }

    partial void UploadCharacter(ref string uploadSummary);

    private Gw2CharacterMotion OwnCharacterMotion()
    {
        var facing = _ownFacing is { } faced ? new Vector2(faced.X, faced.Y) : Vector2.Zero;
        if (_ownStep is not { } step || _loginScreen is not null) return new Gw2CharacterMotion(0f, facing, Vector2.Zero);
        var output = step.LastOutput;
        var moveDirection = new Vector2(System.Runtime.Intrinsics.Vector128.GetElement(output.MoveDirection, 0), System.Runtime.Intrinsics.Vector128.GetElement(output.MoveDirection, 1));
        return new Gw2CharacterMotion(output.MoveSpeed * GameAgents.GameUnitsPerCollisionUnit, facing, moveDirection);
    }

    private void UseOwnAgentAppearance(uint ownAgentId)
    {
        if (_world.Appearances.AppearanceOf(ownAgentId) is not { } block || Gw2AppearanceIndices.Of(block) is not { CompositeRaceName: not null } appearance) return;
        var armor = ArmorOf(ownAgentId);
        if (_character?.Appearance == appearance && _character.Armor.Equals(armor)) return;
        _characterReady = false;
        _character = CharacterScene.Of(_content!, appearance, armor, _highResolutionFiles);
    }

    private Gw2CharacterArmor ArmorOf(uint agentId) =>
        new(_world.Equipment.VisibleArmorOf(agentId).Select(piece => Gw2ArmorPiece.Of(piece.SkinDataId, piece.DyeDataIds)).ToList());

    private void CreateCharacter(string compositeRaceName) =>
        _character = new CharacterScene(_content!, compositeRaceName) { HighResolutionFiles = _highResolutionFiles };

    private readonly Gw2MumbleLink _mumbleLink = new();
    private float _cameraViewDistance = Gw2.Dat.Content.Gw2MapTerrain.DefaultViewSpan;
    private Vector2? _walkAhead;

    private bool _actionCamera;
    private bool _mouseCaptured;
    private bool _mouseCaptureUnavailable;
    private Point _cursorBeforeCapture;

    private void CaptureMouse(bool wantsCapture)
    {
        if (_mouseCaptureUnavailable || wantsCapture == _mouseCaptured) return;
        try
        {
            if (wantsCapture) _cursorBeforeCapture = new Point(_input.Mouse.X, _input.Mouse.Y);
            Mouse.IsRelativeMode = wantsCapture;
            if (!wantsCapture) Mouse.SetPosition(_cursorBeforeCapture.X, _cursorBeforeCapture.Y);
            _mouseCaptured = wantsCapture;
        }
        catch (InvalidOperationException)
        {
            _mouseCaptureUnavailable = true;
            _mouseCaptured = false;
        }
    }

    private void UpdateActionCameraToggle()
    {
        var hotkeys = HotkeysOf();
        if (hotkeys.IsPressed(_input, ToggleActionCameraControl)) _actionCamera = !_actionCamera;
        if (hotkeys.IsPressed(_input, DisableActionCameraControl)) _actionCamera = false;
    }

    private bool IsDraggingCamera() =>
        (_input.IsMouseButtonDown(MouseButton.Left) && _input.WasDown(MouseButton.Left)) ||
        (_input.IsMouseButtonDown(MouseButton.Right) && _input.WasDown(MouseButton.Right));

    private bool IsOrbitingCameraOnly() =>
        _input.IsMouseButtonDown(MouseButton.Left) && !_input.IsMouseButtonDown(MouseButton.Right);

    private float _secondsSinceCameraLook;
    private float _secondsSinceCameraReset;

    private void SwingCameraBehindOwnCharacter(float elapsedSeconds)
    {
        if (_secondsSinceCameraLook < Gw2WorldCameraMotion.SwingBehindIdleSeconds || _secondsSinceCameraReset < Gw2WorldCameraMotion.SwingBehindIdleSeconds) return;
        if (_ownFacing is not { } facing || (facing.X == 0f && facing.Y == 0f)) return;
        var behindYaw = MathF.Atan2(facing.Y, facing.X) + MathF.PI;
        _camera.Yaw = Gw2WorldCameraMotion.TurnAngleToward(_camera.Yaw, behindYaw, Gw2WorldCameraMotion.FollowDistanceRatePerSecond, elapsedSeconds);
    }

    private void LookAroundOwnCharacter(Vector2 mouseMovement)
    {
        if (mouseMovement != Vector2.Zero) _secondsSinceCameraLook = 0f;
        var radiansPerPixel = Gw2WorldCameraMotion.MouseLookRadiansPerPixel * Gw2WorldCameraMotion.MouseLookScaleOf(OwnCameraPreferences.RotationSpeedPercent);
        _camera.Yaw -= mouseMovement.X * radiansPerPixel;
        _camera.Pitch = Math.Clamp(_camera.Pitch + mouseMovement.Y * radiansPerPixel, -Gw2WorldCameraMotion.DefaultPitchLimit, Gw2WorldCameraMotion.DefaultPitchLimit);
    }

    private void FrameStartPosition()
    {
        _secondsSinceCameraReset = 0f;
        _camera.Target = _characterPosition;
        if (_placedOnOwnAgent)
        {
            if (_login?.ControlledAgentId is { } ownId && _world.Agents.TryGet(ownId, out var own))
                _camera.Target = Gw2WorldCameraMotion.AgentAnchorOf(_characterPosition, own.HeightInGameUnits);
            _camera.Distance = OwnCameraPreferences.Distance;
            _ownCameraDesiredDistance = OwnCameraPreferences.Distance;
            _camera.Pitch = -Gw2WorldCameraMotion.DefaultPlacementPitch;
        }
        if (_options.Scene.FieldOfView is { } fieldOfView) _camera.FieldOfView = fieldOfView;
        if (_options.Scene.CameraEye is { } eye && _options.Scene.CameraFront is { } front) _camera.LookFrom(eye, front);
        _ownCameraDesiredCentre = _camera.Target;
    }

    private void FrameCharacter()
    {
        var positions = _character!.Parts
            .SelectMany(part => part.Meshes)
            .Where(mesh => mesh.LodLevel == 0)
            .SelectMany(mesh => mesh.Vertices)
            .Select(vertex => vertex.Position)
            .ToList();
        if (positions.Count == 0) return;

        var minimum = positions.Aggregate(Vector3.Min);
        var maximum = positions.Aggregate(Vector3.Max);
        _camera.Target = (minimum + maximum) / 2f + _characterPosition;
        _camera.Distance = (maximum - minimum).Length() * 1.3f;
        if (_options.Scene.FieldOfView is { } fieldOfView) _camera.FieldOfView = fieldOfView;
        if (_options.Scene.CameraEye is { } eye && _options.Scene.CameraFront is { } front) _camera.LookFrom(eye, front);
    }

    protected override void EndDraw()
    {
        var workEndTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_framePresentedTimestamp != 0) _benchmark.RecordDrawCalls(GraphicsDevice.Metrics.DrawCount);
        _frameTimer?.Mark(GrFrameSection.FrameEnd);
        if (_frameTimer is { HasCompletedCpuFrame: true } cpuFrame)
        {
            if (_framePresentedTimestamp != 0) _benchmark.RecordCpuSections(cpuFrame);
            _perfOverlay?.RecordCpuSections(cpuFrame);
        }
        var endDrawStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        base.EndDraw();
        WaitForFrameLimit();
        var presentedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        CountFramesForServer(presentedTimestamp);
        if (_framePresentedTimestamp != 0)
            _perfOverlay?.RecordPresent(
                System.Diagnostics.Stopwatch.GetElapsedTime(_framePresentedTimestamp, presentedTimestamp).TotalSeconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(_frameWorkStartTimestamp, workEndTimestamp).TotalSeconds);
        if (_framePresentedTimestamp != 0)
            _benchmark.RecordFrame(
                System.Diagnostics.Stopwatch.GetElapsedTime(_framePresentedTimestamp, presentedTimestamp).TotalSeconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(_frameWorkStartTimestamp, workEndTimestamp).TotalSeconds,
                _frameTimer, _archiveMaterials?.MainThreadReads ?? 0, SlowFrameStreamingDescription);
        _framePresentedTimestamp = presentedTimestamp;
        _frameWorkStarted = false;
        if (!_benchmark.IsComplete) return;
        _benchmark.WriteReport();
        Exit();
    }

    private void CountFramesForServer(long presentedTimestamp)
    {
        _framesThisSecond++;
        if (_frameSecondStartTimestamp == 0) _frameSecondStartTimestamp = presentedTimestamp;
        if (System.Diagnostics.Stopwatch.GetElapsedTime(_frameSecondStartTimestamp, presentedTimestamp) < TimeSpan.FromSeconds(1)) return;
        _login?.ReportFramesLastSecond(_framesThisSecond);
        _framesThisSecond = 0;
        _frameSecondStartTimestamp = presentedTimestamp;
    }

    private void WaitForFrameLimit()
    {
        if (_framePresentedTimestamp == 0) return;
        while (true)
        {
            var remaining = _framePeriod - System.Diagnostics.Stopwatch.GetElapsedTime(_framePresentedTimestamp);
            if (remaining <= TimeSpan.Zero) return;
            if (remaining > TimeSpan.FromMilliseconds(2)) System.Threading.Thread.Sleep(1);
            else System.Threading.Thread.SpinWait(64);
        }
    }

    private string FlightStreamingDescription()
    {
        var streaming = "";
        DescribeFlightStreaming(ref streaming);
        return streaming;
    }

    partial void DescribeFlightStreaming(ref string description);

    private string SlowFrameStreamingDescription()
    {
        var streaming = "";
        DescribeSlowFrameStreaming(ref streaming);
        return streaming;
    }

    partial void DescribeSlowFrameStreaming(ref string description);

    private bool IsSceneSettled
    {
        get
        {
            var settled = true;
            CheckStreamingSettled(ref settled);
            return settled;
        }
    }

    partial void CheckStreamingSettled(ref bool settled);

    private void SaveScreenshotAndExit(string path)
    {
        var timeOfDay = _timeOfDayClock.TimeOfDay;
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Screenshot at time of day {timeOfDay.Hour:D2}:{timeOfDay.Minute:D2}:{timeOfDay.Second:D2}, day weight {_dayWeight:F3}"));
        Screenshots.Save(GraphicsDevice, path);
        Exit();
    }
}
