# Next steps

Handover for a new conversation. Start it with:

> Read `README.md` and `docs/NEXT_STEPS.md`, then start on step 4 (networking).

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
| Target scale | ~8,000 units alive (1,000 per player). The 160x32 lane is packed full at 8,000, so it will likely grow for 8 players. |
| Art style | Warcraft 3-like: low poly (300-800 tris per normal unit) with hand-painted textures and team-colour masks. Not modern high-poly. |
| Tick rate | 20 ticks/s, input delay 3 ticks (150 ms) to start with |
| Sim rules | No float/double, no `System.Random`, no clocks, no Godot APIs in `CastleFight.Sim` (see README) |
| Workflow | Commit directly to `main` and push (single developer for now). Repo: github.com/larsbloch/CastleFight |

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
   above about 8 ms. That will be needed for the 8,000-unit target (benchmark: 8,000 units ≈ 11.6 ms).
6. **Gameplay design (needs the owner's input).** Races and their buildings/units/tiers, the income
   model (Castle Fight style income/treasure), armor/attack types, projectiles, splash, auras/heals/slows,
   more `UnitBehavior`s. With 8 players there are more buildings, so watch total unit counts.
7. **Visuals.** See the visuals plan below. Also HP bars, building placement ghost, build menu, minimap,
   end-of-game score screen.
8. **Distribution.** Icon, version numbering, Steam (Steamworks relay/matchmaking removes port forwarding).

## Visuals plan: animated models for 8,000 units

This step is independent of networking, so it can move earlier if wanted.

Normal Godot animation (a `Skeleton3D` + `AnimationPlayer` per unit) costs CPU per unit and does not scale to
thousands. Use **vertex animation textures (VAT)** instead:

- **Bake:** each animation (idle, walk, attack, death) is baked offline into a texture. Each row is one frame,
  each pixel one vertex position (plus normals).
- **Draw:** each unit type stays one MultiMesh, one draw call. Per-instance custom data holds animation id,
  start time, speed and variation. The vertex shader reads the texture, so the CPU does no per-unit
  animation work.
- **Sim stays unchanged:** the renderer derives the animation from sim state (`Vel` means walk, target in
  range means attack). The sim should expose "attacked on tick N" so the swing lines up with the damage.
- **Deaths:** the sim frees a unit on the tick it dies. The renderer keeps short-lived "corpse" instances to
  play the death animation.
- **Team colour:** a mask in the texture, tinted per instance in the shader (WC3 style).
- **Flying units (gryphons):** a render height offset, plus a sim rule (`IsFlying`: only ranged units and
  other flyers can hit them).

GPU budget (at 8,000 units, polygon count is the main cost, then shadows):

| Kind | Triangles |
|---|---|
| Normal units | 300-800 |
| Big units (knight on horse, gryphon) | 800-1,500 |
| Heroes / special | up to 2,000 |
| Buildings | 1,000-3,000 (few of them) |

- **Blob shadows:** use these (a dark soft decal under each unit, like WC3), not real shadow maps. Shadow maps
  draw every unit a second time.
- **LOD:** add LOD versions (150-300 tris) for zoomed-out views if measurements show the need.
  Small on-screen units waste GPU time on sub-pixel triangles.

### First visuals task

1. Download a CC0 rigged and animated character (KayKit "Adventurers"/"Skeletons" or Quaternius; check the
   license) in the 500-1,000 triangle range.
2. Write an editor/bake tool: `.glb` (mesh + skeleton + animations) in, VAT texture + mesh + animation table out.
3. Write a VAT shader, and add animation selection to `UnitRenderer` via MultiMesh custom data.
4. Add blob shadows.
5. Add GPU frame time to the HUD (`RenderingServer.ViewportSetMeasureRenderTime` /
   `ViewportGetMeasuredRenderTimeGpu`) next to the sim step time.
6. Stress test: K-spawn up to 8,000 animated units, and record CPU sim ms and GPU ms in this file.

### Where real models come from later

- **The owner trying it:** text/image-to-3D generators (Meshy, Tripo, Hunyuan3D, …) work for exploring a style.
  Then auto-rig and animate humanoids in Mixamo, and reduce polygons in Blender (AI meshes are usually
  10k-50k tris).
- **Final art:** clean up by hand, or commission a hand-painted low-poly artist.
- **Never ship ripped WC3/Blizzard assets.**
- The bake tool should accept any rigged and animated `.glb`, wherever it came from.

## Environment notes

- .NET SDK 10.0.401 (the broken VS workload manifest is fixed; no workaround needed any more).
- Godot export templates for 4.7.2 mono (Windows x86_64 only) are installed in
  `%APPDATA%\Godot\export_templates\4.7.2.stable.mono\`.
- Godot itself may not be installed yet: `winget install GodotEngine.GodotEngine.Mono`. For automated
  checks, the portable editor zip from GitHub releases works headless:
  `--headless --path game --import`, then the smoke test from the README, or
  `--export-release "Windows Desktop" ../build/windows/CastleFight.exe`.
- Godot plays from the editor in Debug. `CastleFight.Sim` forces `<Optimize>true</Optimize>` because of that.
