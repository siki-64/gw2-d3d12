# gw2-d3d12

A new Guild Wars 2 client on the Crystal engine, with the research on the retail client it depends on, keyed to the
original client binary (`Gw2-64.exe`) and archive (`Gw2.dat`). The engine (`engine/`) and the research
(`gw2-research/`) are submodules with their own repositories.

> [!WARNING]
> This is an early research prototype, not a playable game client. It logs in, walks a character around and draws
> the world and parts of the HUD, but most of the game (combat feedback, effects, chat, inventory, the full UI) is
> missing or incomplete. It is not meant to replace the official client, is not supported by ArenaNet, and using it
> on the live servers may break the game's terms of service, so do so at your own risk. It needs an installed copy
> of Guild Wars 2 and ships no game files.

## The split

- `src/` is the new client and the public libraries it ships with: `Gw2.dat` decoding (`src/Dat/`), file formats and
  run-time extraction (`src/Formats/`) and the wire protocol (`src/Protocol/`). It builds without `gw2-research`.
- `gw2-research/` is the research on the retail client: notes by subject, the exe's object layouts and routines,
  live readers, captures and the tests that compare against them.
- The retail client never runs as part of the new client. Reading its memory is only for logging and debugging
  protocol and game state.

| Folder | Role | Notes |
| --- | --- | --- |
| `src/Dat/` | `Gw2.Dat.Identity` owns the ids an asset or name is found by, `Gw2.Dat.Archive` reads `Gw2.dat` and decodes its files, `Gw2.Dat.Content` turns them into content objects. | [`src/Dat/Gw2.Dat.Content/README.md`](src/Dat/Gw2.Dat.Content/README.md) |
| `src/Formats/`, `src/Protocol/` | `Gw2.Formats` reads file formats and extracts build-specific data from the exe at run time; `Gw2.Protocol` is the wire format, sessions, portal and capture parsing. | |
| `gw2-research/` | The retail client's side, its own repository: the `Gw2.Exe.*` research libraries (`src/`: `Handlers`, `Objects` with the exe's object layouts, `Routines`, `Live`), their tests and fixtures (`tests/`) and the notes on the retail client by subject (`docs/`). | [`gw2-research/src/Gw2.Exe.Objects/README.md`](gw2-research/src/Gw2.Exe.Objects/README.md) |
| `src/Client/` | The new client on the Crystal engine (the `engine/` submodule), talking to the **official servers** over its own connections: `Gw2.Client` is the executable. | [`ROADMAP.md`](ROADMAP.md) |
| `tests/` | The offline test projects by area (`Client/`) and the shared fixtures by build (`fixtures/<build>/`). | |
| `tools/` | Reverse-engineering tooling: IDA scripts (`ida/`), validators and extractors (`re/`), the evidence wrapper, `Gw2.DatSurvey`, `Gw2.CaptureDecoder`, `Gw2.LiveCapture`, `Gw2.LiveLogin`, `Gw2.PhysicsReplay`, `Gw2.TransportFixtures` and the read-only `Gw2.LiveMemory`. | [`tools/README.md`](tools/README.md) |
| `docs/` | Notes on the new client (`client/`); the retail client's notes are in `gw2-research/docs/`. | [`docs/README.md`](docs/README.md) |
| `gw2-research/research/` | The frozen images by build (`<build>/`: executable, `.dat` and IDA database); gitignored. | |

## Boundaries

- Constants stay hardcoded wherever the original binary hardcodes them; build-specific tables and addresses are
  read from the exe at run time, never shipped.
- `src/Client/` recreates the client and does not own reverse-engineering research. Game-agnostic engine and platform
  code lives in Crystal, not here.
- Everything targets net10 with C# 14 and nullable on, set once in the root `Directory.Build.props`.
- IDA Pro and x64dbg MCPs are the shared reverse-engineering interface. IDA scripts live in `tools/ida/`.

## Names

Assembly name, namespace and folder agree. The folder under `src/` is the first namespace segment after `Gw2`:
`Gw2.Dat.Identity`, `Gw2.Dat.Archive`, `Gw2.Dat.Content`; `Gw2.Formats`, `Gw2.Protocol`;
`Gw2.Client`, `Gw2.Client.Net`, `Gw2.Client.State`, `Gw2.Client.World`, `Gw2.Client.Scene`, `Gw2.Client.Model`,
`Gw2.Client.Frames`, `Gw2.Client.Graphics`, `Gw2.Client.Ui`, ...

## Build

The engine is a submodule; clone with `--recursive`, or run `git submodule update --init` in an existing clone, and again after switching to a branch that moves the engine pointer.

The client, libraries, tools and tests build with:

```powershell
dotnet build gw2.slnx -c Release
```

The client and its tests alone, without the `gw2-research` submodule:

```powershell
dotnet build client.slnf -c Release
```

A native client (Native AOT) publishes with the command below. It needs .NET SDK 10.0.403 or later, because older SDKs pick an AOT compiler that NuGet flags as vulnerable, and the Visual Studio C++ build tools for the linker:

```powershell
dotnet publish src/Client/Gw2.Client -c Release -o publish
```

## Run

Start the client with:

```powershell
dotnet run --project src/Client/Gw2.Client
```

The client opens the login screen and plays a character over its own connections. ArenaNet and Steam logins first
check that the exe's build is the live build, as the exe's patcher does, and stop with a message when the game needs an
update. `--standalone`, `--environment`,
`--benchmark` and `--flight` skip the login ([Debug](#debug)).

It finds `Gw2.dat` and `Gw2-64.exe` in its own folder first, so a client placed in the Guild Wars 2 folder uses that
install. Otherwise it looks in the Steam libraries and `%ProgramFiles%\Guild Wars 2`
([install paths](gw2-research/docs/engine/install-paths.md)). `%APPDATA%\Guild Wars 2\Local.dat`, when present, enables the
full-resolution body atlas. The body and the map draw with their AMAT shaders.

## Graphics settings

The Esc menu's Graphics Options window edits the client's own `%LocalAppData%\Gw2ClientRemake\GraphicsSettings.xml`
([graphics options](docs/client/ui/graphics-options.md)). On first start the values are seeded from the game's
`%AppData%\Guild Wars 2\GFXSettings.Gw2-64.exe.xml`, which the client only reads. Command-line switches override the
file. `--benchmark`, `--flight` and `--screenshot` runs ignore it so they stay reproducible.

## Debug

In game, Shift+F9 opens the debug view window (collider capsules) and Shift+F10 the perf graphs and collision
windows ([debug UI](docs/client/engine/debug-ui.md)).

Command-line switches go after `--`. Switches that take values are shown with their format.

Servers:

- `--auth-server <ip[:port]>` connects the login to that auth server instead of `Auth{n}.101.ArenaNetworks.com`
  (default port 6112), like the exe's `-authsrv`.
- `--portal <ip[:port]>` logs in through that portal instead of `cligate.101.ncplatform.net` (default port 6600),
  like the exe's `-portal`.
- A private server also needs the client to trust its keys, which the exe pins. `--portal-certificate <file>`
  trusts that portal certificate (DER or PEM) next to the exe's trust anchors, and `--auth-server-public-value
  <hex>` replaces the 64-byte auth handshake public value read from the exe. Without them only the retail portal
  and auth servers are accepted.
- The login screen has the same four fields under its **Custom** server option. They are saved to
  `%LOCALAPPDATA%\Gw2ClientRemake\CustomServers.xml` on each login; switches override the saved fields.

Scene and camera:

- `--standalone` skips the login and shows the standalone character; `--environment <map file id>` and `--map <map id>`
  load a map without logging in.
- `--race <RaceSex>` picks the standalone body (default `HumanFemale`); `--attach <model:socket[:grip]>` and
  `--attach-skin <skin[:stowed]>` add gear to it, and can be repeated.
- `--position <x,y,z>`, `--camera-eye <x,y,z>`, `--camera-front <x,y,z>` and `--field-of-view <radians>` pin the
  camera, for example to a MumbleLink position from the live client.
- `--time-of-day <hh:mm[:ss]>` sets the clock (default 12:00) and `--time-rate <factor>` its speed; `--still-scene`
  freezes animation and time.
- `--input-bindings <file>` loads a key binding loadout.

Rendering:

- `--debug-view <view>` draws one render pass: `shadow-cascades`, `normals`, `lighting`, `overdraw`, `clay`,
  `texel-density`, `albedo`, `wireframe` or `ambient-occlusion`.
- `--hand-shaders` draws the body with `GrCharacter.hlsl` instead of its AMAT shaders; `--decompiled-shaders <folder>`
  loads replacement shaders from a folder.
- `--vanilla-water-gate` turns off the hidden-water cull and keeps only the exe's reflection gate;
  `--low-quality-water` runs the wave FFT every 0.2 s and lerps between refreshes, like the exe's low water quality.
- Graphics settings from the command line override the settings file: `--shadows <none|low|medium|high|ultra>`,
  `--no-screen-space-shadows`, `--no-anisotropic-filtering`, `--shader-quality <level>`, `--render-sampling
  <subsample|native|supersample>`, `--reflections <none|terrain-and-sky|all>`, `--lod-distance <low|medium|high|ultra>`,
  `--textures <high|medium|low>`, `--gamma <value>`, `--no-bloom`, `--no-color-grading`, `--no-ambient-occlusion`,
  `--saturation-and-tint`, `--depth-blur`, `--light-adaptation`, `--light-rays`, `--distortion`, and the on/off
  switches `--gpu-driven`, `--hi-z`, `--parallel-recording`, `--volumetric-fog` and `--physical-bloom`.
- `--resolution <width>x<height>` sets the back buffer (default 1600x900, 1920x1080 for benchmarks and flights) and
  `--frame-limit <limit>` the frame cap.

Measuring:

- `--screenshot <png> --screenshot-at <seconds>` saves one frame.
- `--perf-graphs` opens the perf graphs window at start; `--slow-frames` logs frames that run long.
- `--benchmark <seconds>` records frame times, with `--benchmark-warmup <seconds>` (default 4), `--benchmark-name
  <name>` and `--benchmark-report <file>`; `--flight <x,y,z;x,y,z;...>` flies the camera along waypoints at
  `--flight-speed <units per second>` ([benchmarks](docs/client/benchmarks.md)).
- `--benchmark`, `--flight` and `--screenshot` ignore the settings file so runs stay reproducible.

```powershell
dotnet run --project src/Client/Gw2.Client -- --environment 3776288 --debug-view wireframe --screenshot shot.png --screenshot-at 6
```

## Contributing

Branching, commit, PR, pushing and history rules are in [`CONTRIBUTING.md`](CONTRIBUTING.md).
