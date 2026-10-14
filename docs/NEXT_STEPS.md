# Next steps

Handover for a new conversation. Start it with:

> Read `README.md` and `docs/NEXT_STEPS.md`, then start on step 4 (networking) on a `networking` branch.

## Where we are

Steps 1-3 are done and on `main`:

1. `src/CastleFight.Sim`: deterministic simulation in plain C# (fixed-point math, structure-of-arrays unit pool,
   per-team spatial grids, ring-search targeting, soft separation + sidestep steering, commands, state hash).
2. `tests/` (22 tests: determinism, crowd flow, hunter targeting, placement, winning) and `tools/CastleFight.Bench`.
3. `game/`: Godot 4.7.2 .NET project. MultiMesh rendering with interpolation, camera, HUD, mouse placement,
   `ScriptedBot` players, `--smoke` headless test, Windows release export preset (`build/windows/`).

Measured on the dev PC (16 threads, single-threaded sim): 4,000 units ≈ 4.5 ms/tick, 8,000 (lane packed full)
≈ 11.6 ms, 20,000 on a 320x64 map ≈ 10.3 ms. Budget is 50 ms per tick (20 ticks/s); practical target < 8 ms
because the sim currently runs on the render thread.

## Decisions already made

| Topic | Decision |
|---|---|
| Engine | Godot 4.7.2 .NET, C#, `net10.0` |
| Platform | Desktop (Windows first). No web: Godot 4 can't export C# to web. |
| Multiplayer model | Deterministic lockstep: only commands are sent; every client runs the sim. |
| Players | 1-8 (`GameSetup.MaxPlayers`). Usually 2v2/3v3; 4v4 is for rare big games. |
| Tick rate | 20 ticks/s, input delay 3 ticks (150 ms) to start with |
| Sim rules | No float/double, no `System.Random`, no clocks, no Godot APIs in `CastleFight.Sim` (see README) |
| Workflow | Work on feature branches; only commit/push when asked. Repo: github.com/larsbloch/CastleFight |

## Step 4: lockstep networking (next)

Goal: a release build that 2-8 people can play together over LAN or the internet, with automatic desync
detection.

### Design

- **Library:** LiteNetLib (UDP, reliable-ordered channel, pure C#).
- **Topology:** relay. The server never runs the simulation. It only collects commands and broadcasts one
  bundle per tick. Hosting a game = the host's client also starts the relay in-process. The same relay
  code runs as a standalone console server (`tools/CastleFight.Server`) for cloud hosting later.
- **New project `src/CastleFight.Net`** (no Godot references, unit-testable):
  - Message types + binary serialization (`Fix` sent as raw `long`).
  - `RelayServer`: lobby slots (max 8), team assignment, start game, tick bundling, hash comparison.
  - `LockstepClient`: sends local commands, buffers received bundles, tells the game which ticks are ready.
  - An `ITransport` abstraction with a LiteNetLib implementation and an in-memory one for tests
    (configurable latency, jitter, and packet ordering).

### Protocol (first version)

| Direction | Message | Notes |
|---|---|---|
| C → S | `Hello(protocolVersion, buildHash, playerName)` | Reject mismatched versions; a desync is guaranteed otherwise. |
| S → C | `Welcome(slot)` / `Rejected(reason)` | |
| S → all | `LobbyState(slots: name, team, isBot, ready)` | |
| C → S | `SetTeam`, `SetReady`; host only: `AddBot`, `RemoveBot`, `StartGame` | |
| S → all | `GameStart(seed, playerTeams, botSlots, inputDelay)` | Everyone builds the same `GameSetup`. |
| C → S | `TickInput(tick, commands[])` | Sent for **every** tick, empty if no commands. |
| S → all | `TickBundle(tick, commands[])` | Sent once all live players' input for `tick` has arrived; commands in canonical order (player id, then issue order). |
| C → S | `StateHash(tick, hash)` | Every 20 ticks. |
| S → all | `Desync(tick, slots)` | Server compares hashes; on mismatch each client dumps its state. |
| S → all | `PlayerLeft(slot)` | From the next bundle on, that slot simply sends no commands. |

### Client loop (replaces the single-player loop in `game/scripts/Main.cs`)

- Local input → `TickInput` for `currentTick + inputDelay`, as now.
- Step the sim only for ticks whose `TickBundle` has arrived. If several are buffered, run up to N per frame
  to catch up. If none is buffered for a while, show "Waiting for players…".
- Bots: the **host's** client runs `ScriptedBot` for bot slots and sends their commands as those slots.

### Desync tooling

- Add `Simulation.DumpState(TextWriter)`: a readable dump of every field the hash covers.
- On `Desync`, every client writes `user://desync_<tick>_<slot>.txt`, so two files can be diffed.
- Optional: record `GameStart` and every `TickBundle` to a replay file. Replaying it must reproduce the
  hashes. This gives replays for free, and a desync becomes reproducible offline.

### UI (minimal)

- Main menu: Single player / Host / Join (IP + port) / Quit.
- Lobby: player list with team picker, Add bot (host), Ready, Start (host).
- In game: ping and input delay in the HUD, plus the waiting overlay.

### Tests / acceptance criteria

- [ ] Unit test: 8 `LockstepClient`s over the in-memory transport with random latency/jitter play
      2,000 ticks with bots; every client has identical hashes at every check.
- [ ] Unit test: a client that disconnects mid-game does not stall or desync the others.
- [ ] Unit test: mismatched protocol versions are rejected.
- [ ] Manual: host + 2 more game windows on one PC, play a full match, no desync.
- [ ] Manual: 8 windows (or 2-3 windows plus bots filling 8 slots), K-spawn stress, check step times.
- [ ] Release export still works; README updated with how to host/join and which UDP port to forward.

### Open questions to decide during step 4

- Default UDP port (suggest 7777) and whether to add LAN discovery (LiteNetLib supports broadcast).
- Adaptive input delay from measured ping (nice to have; fixed 3 ticks is fine at first).
- Reconnect mid-game: out of scope for the first version (would need a state snapshot or replay catch-up).

## Later steps (in rough order)

5. **Sim on its own thread.** The main thread renders by interpolating between the last two finished ticks,
   so a step can use most of the 50 ms budget without causing frame hitches. Do this when step times go
   above about 8 ms.
6. **Gameplay design (needs the owner's input).** Races and their buildings/units/tiers, the income
   model (Castle Fight style income/treasure), armor/attack types, projectiles, splash, auras/heals/slows,
   more `UnitBehavior`s. With 8 players there are more buildings, so watch total unit counts.
7. **Visuals.** Real unit models with GPU-baked animations (vertex animation textures) in the MultiMesh,
   HP bars, building placement ghost, build menu, minimap, end-of-game score screen.
8. **Distribution.** Icon, version numbering, Steam (Steamworks relay/matchmaking removes port forwarding).

## Environment notes

- .NET SDK 10.0.401 (the broken VS workload manifest is fixed; no workaround needed any more).
- Godot export templates for 4.7.2 mono (Windows x86_64 only) are installed in
  `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\`.
- Godot itself may not be installed yet: `winget install GodotEngine.GodotEngine.Mono`. For automated
  checks, the portable editor zip from GitHub releases works headless:
  `--headless --path game --import`, then the smoke test from the README, or
  `--export-release "Windows Desktop" ../build/windows/CastleFight.exe`.
- Godot plays from the editor in Debug. `CastleFight.Sim` forces `<Optimize>true</Optimize>` because of that.
