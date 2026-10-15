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
| `game/` | Godot project: menu, lobby, rendering (MultiMesh), camera, input, HUD. Reads the simulation, never changes it except through commands. |

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
`Esc` twice leaves the match. Single player only: `Space` pauses, `R` restarts.

### Multiplayer

- **Host:** main menu, *Host game*. Your game runs the relay on UDP port **7777** (changeable). Players outside
  your network need that UDP port forwarded to your PC (and allowed through the Windows firewall).
- **Join:** enter the host's address and port, then *Join*. In the lobby, pick a team and tick *Ready*. The host
  picks the map, adds bots and starts.
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
