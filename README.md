# Castle Fight

Lane battle game for up to 8 players (usually 2v2 or 3v3). Godot 4.7 (.NET) + C#, desktop only, deterministic lockstep multiplayer.

## Layout

| Path | What |
|---|---|
| `src/CastleFight.Sim` | The whole game simulation in plain C#. No Godot references. Fixed-point math only. Maps, tile grid, lanes, units. |
| `src/CastleFight.Net` | Lockstep multiplayer: protocol, relay server, client, sessions and the shared game loop. No Godot references. |
| `tests/CastleFight.Sim.Tests` | Determinism, maps, placement, lanes/crowd flow, gameplay and grid tests. |
| `tests/CastleFight.Net.Tests` | Protocol, lobby, lockstep sync under latency/jitter, disconnects, desync detection, UDP loopback. |
| `tools/CastleFight.Bench` | Headless stress benchmark (ms per tick at N units). |
| `tools/CastleFight.Server` | Standalone relay server (the same relay a hosting player runs inside the game). |
| `docs/NEXT_STEPS.md` | Roadmap and handover notes: what is done, what is next. |
| `game/` | Godot project: menus, lobby, rendering (MultiMesh), camera, input, HUD. Reads the simulation, never changes it except through commands. |
| `game/scripts/ui` | Menus and lobby. The wooden theme is painted in code (`UiArt`, `UiTheme`) plus two shaders in `game/shaders`; fonts (OFL) are in `game/assets/fonts`. |

## Rules for code in `CastleFight.Sim`

Every client runs the simulation independently, so it must give bit-identical results everywhere:

- No `float`/`double`: use `Fix` / `FixVec2`.
- No `System.Random`, clocks or `delta` time: use `DeterministicRandom` and ticks.
- Iterate in slot order; break ties with unit `Id`. Never depend on `Dictionary`/`HashSet` order.
- No Godot APIs. Player actions enter only as `Command`s.

`Simulation.ComputeStateHash()` must match across clients; a mismatch is a desync bug. `Simulation.DumpState()`
writes the same fields as readable text. Any change to the simulation or the wire format should bump
`ProtocolInfo.Version`.

## Playing

Maps: **Single Lane**, **Two Lanes**, **Three Lanes**. Each team shares one build zone. Units walk to the lane
closest to where they stand, so building at the top or bottom of the zone picks the lane.

Controls: `1`-`4` choose a building, a green ghost shows where it goes (amber: not enough gold, red: blocked),
left click places it, right click or `Esc` cancels. `WASD` pans, the mouse wheel zooms, `K` spawns a stress wave.
`Esc` twice leaves the match. Offline only: `Space` pauses, `R` restarts.

Main menu: **Host Game**, **Join Game**, **Options** (name, window mode, frame cap, vsync), **Exit**. Settings,
favourite servers and the last host setup are saved in `settings.cfg` in the Godot user folder.

### Multiplayer

- **Host:** *Host Game*, pick the map and how many bots fight with and against you, then *Open Lobby*. Your game
  runs the relay on UDP port **7777** (changeable there). Players outside your network need that UDP port
  forwarded to your PC (and allowed through the Windows firewall). *Play Offline* starts the same setup against
  bots on your PC only.
- **Join:** *Join Game*, pick a favourite or recent server (double-click joins) or type an address and port.
  *Save as Favorite* keeps it in the list.
- **Lobby:** both armies with their open places, the map, a ready check and chat. Everyone can switch army and
  tick *Ready*; the host picks the map, adds, moves and removes bots and starts the battle.
- **Dedicated server:** `dotnet run -c Release --project tools/CastleFight.Server -- --port 7777`. Everyone joins
  it; the first to join is the lobby host. It opens a new lobby when a game ends.
- Everyone must run the same build. If a desync is detected, the game stops and each player gets
  `desync_<tick>_<slot>.txt` in the Godot user folder (`%APPDATA%\Godot\app_userdata\Castle Fight\`) to diff.

## Commands

```
dotnet test CastleFight.slnx
dotnet test CastleFight.slnx --filter "Category!=Network"     # skip the real-UDP test
dotnet run -c Release --project tools/CastleFight.Bench -- --units 2000,4000 --map three   # single|two|three|LxW
dotnet run -c Release --project tools/CastleFight.Server -- --port 7777
```

Godot: open `game/project.godot` in Godot 4.7 .NET and press F5.

Headless checks (`godot` = the Godot 4.7.2 mono console executable):

```
godot --headless --path game --fixed-fps 60 --quit-after 1200 -- --smoke --map 2
godot --headless --path game -- --host --autostart 2 --bots 2 --map 2 --autoplay --quit-at-tick 600 --print-hashes
godot --headless --path game -- --join 127.0.0.1 --ready --autoplay --quit-at-tick 600 --print-hashes
```

The last two play a networked game between two processes; their `HASH` lines must be identical. All launch
options are listed in `game/scripts/LaunchOptions.cs` (for example `--map 1 --players-per-team 4` starts single
player straight away).

Screenshots of the menus (needs a window, so no `--headless`): `--screen host|join|options` opens a menu,
`--screenshot out.png [--screenshot-frames N]` saves the window after N frames and quits, `--say "text"` sends a
chat line once in the lobby. For example, a lobby with chat:

```
godot --path game -- --host --bots 3 --say "Welcome!" --screenshot lobby.png --screenshot-frames 600
godot --headless --path game -- --join 127.0.0.1 --name Ragnar --ready --say "gl hf"   # within ~8 s
```

## Windows build

Needs the Godot 4.7.2 **mono** export templates (`%APPDATA%\Godot\export_templates\4.7.2.stable.mono\`).
From the repo root, with `godot` being the Godot 4.7.2 mono console executable:

```
godot --headless --path game --import
godot --headless --path game --export-release "Windows Desktop" ../build/windows/CastleFight.exe
```

The first command is only needed after adding or renaming scripts. The output in `build/windows/` (git-ignored) is:

- `CastleFight.exe` (~110 MB, the engine plus the packed game data)
- `data_CastleFight_windows_x86_64/` with the .NET runtime and the game's DLLs (`CastleFight.dll`,
  `CastleFight.Sim.dll`, `CastleFight.Net.dll`, `LiteNetLib.dll`, ...)

To share the game, zip and send the **whole `build/windows` folder**: the exe does not run without the `data_` folder
next to it. Quick check that the build runs (prints a HUD line with the unit count and state hash):

```
build/windows/CastleFight.exe --headless --fixed-fps 60 --quit-after 700 -- --smoke --map 2
```

The first time a player hosts, Windows asks whether to allow `CastleFight.exe` through the firewall. Allow it,
or nobody can join.
