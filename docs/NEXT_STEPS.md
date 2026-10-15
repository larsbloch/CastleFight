# Next steps

Handover for a new conversation. Start it with:

> Read `README.md` and `docs/NEXT_STEPS.md`, then continue with the next step.

## Where we are

Steps 1-4 are done and on `main`:

1. `src/CastleFight.Sim`: deterministic simulation in plain C# (fixed-point math, structure-of-arrays unit pool,
   per-team spatial grids, ring-search targeting, soft separation + sidestep steering, commands, state hash).
2. `tests/` and `tools/CastleFight.Bench`.
3. `game/`: Godot 4.7.2 .NET project. MultiMesh rendering with interpolation, camera, HUD, `ScriptedBot`
   players, `--smoke` headless test, Windows release export preset (`build/windows/`).
4. **Maps, grid placement and lockstep multiplayer** (details below): selectable 1/2/3-lane maps, tile-grid
   building placement with a placement ghost, units choosing the nearest lane, `src/CastleFight.Net`
   (relay, client, lobby, desync detection), main menu + lobby UI, standalone server.

Then a **menu and lobby UI pass** (details below): wooden/iron theme, Host / Join / Options screens, map icons,
favourite servers and lobby chat. Step 5 (sim thread) was deliberately skipped for now; see "Later steps".

Benchmark (dev PC, 16 threads, single-threaded sim, `--units 4000` = 8,000 units, 300 ticks):

| Map | 8,000 units, avg ms/tick |
|---|---|
| Old 160x32 lane (`--map 160x32`) | 9.4 (was 9.6 before step 4) |
| Single Lane 224x48 | 5.0 |
| Two Lanes 224x68 | 4.4 |
| Three Lanes 224x108 | 3.6 |

The new maps are roomier, so the 8,000-unit target fits the 8 ms practical budget without threading for now.

## Decisions already made

| Topic | Decision |
|---|---|
| Engine | Godot 4.7.2 .NET, C#, `net10.0` |
| Platform | Desktop (Windows first). No web: Godot 4 can't export C# to web. |
| Multiplayer model | Deterministic lockstep over a relay: only commands are sent; every client runs the sim. |
| Players | 1-8 (`GameSetup.MaxPlayers`), max 4 per team. Usually 2v2/3v3; 4v4 is for rare big games. |
| Target scale | ~8,000 units alive (1,000 per player). |
| Art style | Warcraft 3-like: low poly (300-800 tris per normal unit) with hand-painted textures and team-colour masks. Not modern high-poly. |
| Tick rate | 20 ticks/s, input delay 3 ticks (150 ms) |
| Network | LiteNetLib 2.1 (UDP, reliable-ordered), default port **7777**, connect by IP, no LAN discovery yet |
| Build area | Shared by the whole team (not per player) |
| Grid | 1-unit tiles; current buildings 2x2, castle 4x4 |
| Lane choice | Never fixed at spawn: a unit always heads for the lane closest to where it stands now |
| Sim rules | No float/double, no `System.Random`, no clocks, no Godot APIs in `CastleFight.Sim` (see README) |
| Workflow | Every task on its own branch with a pull request to `main` (several agents work in parallel). Repo: github.com/larsbloch/CastleFight |
| Menu look | Weathered wood, iron and gold; Cinzel / Cinzel Decorative / Alegreya fonts (OFL). Textures are painted in code (`UiArt`), no image files. Menus scale with the window (1600x900 base); matches render at native size. |

## Step 4 (done): maps, grid, lockstep networking

### Maps (`MapDefinition`, `MapCatalog`)

Every map is 224 long. Team 0's base is on the left (x < 56), team 1's on the right (x >= 168), lanes in
between. Castles are 4x4 at x 4-8 (mirrored for team 1). The build zone is x 12-44 (32 deep) and spans every
lane's band, so where you build decides which lane your units take. There are 12 tiles of open "mouth" between
the zone and the lanes.

| Map (`MapId`) | Width | Lanes (Y) | Walls (Y, 12 thick) | Build zone (Y) | Zone tiles (was 672) |
|---|---|---|---|---|---|
| Single Lane (0) | 48 | 0-48 | none | 4-44 | 1,280 |
| Two Lanes (1) | 68 | 0-28, 40-68 | 28-40 | 6-62 | 1,792 |
| Three Lanes (2) | 108 | 0-28, 40-68, 80-108 | 28-40, 68-80 | 8-100 | 2,944 |

All numbers are parameters of `MapCatalog.CreateLaneMap`. Walls are thicker than any aggro range, and nothing
targets through a wall (line-of-sight check). The map id is in `GameSetup.Map`, and the map fingerprint is in
the state hash.

### Movement

- **Own base:** a per-team flow field (`BaseFlowField`: Dijkstra over tiles, rebuilt only when a structure is
  placed or dies) points every tile at the nearest lane entrance by walking distance around buildings. Units
  follow it, so if buildings or a crowd push a unit sideways it re-routes to whichever lane is now closest.
- **In a lane:** straight on, kept off the walls. Spreaders drift to a fraction of the current lane's width.
- **Past the lanes:** towards the enemy castle's band.
- Targets are bands, not points, so crowds never converge on a single spot.
- Walls: soft separation (the existing sidestep logic) plus a hard circle-vs-rectangle push-out.
- Structures collide as their square footprint, so a 1-tile gap between buildings is walkable.
- Spawning picks a free tile next to the building, front side first.

### Placement

`Command.PlaceBuilding(player, type, tileX, tileY)` (the footprint's lowest tile). The sim keeps a tile
occupancy array (derived state, checked by `ValidateOccupancy` in tests), and `CheckPlacement` says why a spot
is invalid (outside zone / occupied / not enough gold). Godot draws a tile grid over the build zones and a
green/amber/red ghost.

### Networking (`src/CastleFight.Net`)

- `RelayServer` never runs the sim. It runs the lobby, then sends one `TickBundle` per tick once every
  connected player's input has arrived. It runs in-process when a player hosts (`RelayHost`, on its own
  thread) or standalone (`tools/CastleFight.Server`).
- `LockstepClient` (protocol), `NetworkSession` (steps the sim when a bundle is there; the host runs the bots),
  `LocalSession` (single player), `MatchDriver` (the shared fixed-rate loop with catch-up and "waiting").
- `ITransport`: LiteNetLib for real games, `InMemoryNetwork` (virtual clock, latency, jitter, reordering) for tests.

| Direction | Message | Notes |
|---|---|---|
| C → S | `Hello(protocolVersion, buildId, name)` | Rejected on a protocol mismatch, or if the build differs from the first player's (MVID of `CastleFight.Sim`). |
| S → C | `Welcome(slot)` / `Rejected(reason, text)` | Reasons: version, build, lobby full, game in progress. |
| S → all | `LobbyState(map, hostSlot, slots)` | The first player in hosts; hosting passes on if they leave the lobby. |
| C → S | `SetTeam`, `SetReady`; host only: `AddBot`, `RemoveBot`, `SetSlotTeam`, `SetMap`, `StartGame` | Max 4 per team. Start needs every other human ready. |
| S → all | `GameStart(seed, map, inputDelay, hostSlot, players)` | Player id = index in `players`. Bundles for the first `inputDelay` ticks follow at once. |
| C → S | `TickInput(tick, commands)` | Every tick, even empty. Commands for anyone but yourself (or the bots, for the host) are dropped. |
| S → all | `TickBundle(tick, commands)` | Ordered by player id, then issue order. 12 bytes per command, all integers. |
| C → S | `StateHash(tick, hash)` | Every 20 ticks. |
| S → all | `Desync(tick, dumpTick, hashes)` | The relay halts. Every client steps to exactly `dumpTick` and writes `user://desync_<tick>_<slot>.txt` (`Simulation.DumpState`, one unit per line, diffable). |
| S → all | `PlayerLeft(playerId)` | The relay stops waiting for them; their buildings keep spawning. |
| C → S | `SendChat(text)` | Lobby only (protocol 2). The relay makes it one line of at most 120 characters and drops blank ones. |
| S → all | `Chat(slot, team, name, text)` | Slot 255 is a relay notice: "X joined.", "X left.", "X is now the host." Clients keep the last 200 lines. |

### Acceptance

- [x] 8 in-memory clients with 20-150 ms jitter and ±10% clock skew play 2,000 ticks with identical hashes
      (`LockstepTests`), plus 3 humans + 5 bots on three lanes with packet reordering.
- [x] A client dropping out mid-game does not stall or desync the others.
- [x] Mismatched protocol versions and builds are rejected.
- [x] Real UDP: two headless Godot instances (host + join), and two through the standalone server, had identical
      hashes through tick 600 (commands in the README). Also covered by `LiteNetLoopbackTests`.
- [x] Release export works and contains `CastleFight.Net.dll` and `LiteNetLib.dll`. The exported exe passes the
      headless smoke test, and the owner tried the Windows build and confirmed it works well (README:
      "Windows build").
- [ ] Manual: host + 2 windows, a full match played by hand (the owner).
- [ ] Manual: 8 windows (or 2-3 windows plus bots), K-spawn stress up to 8,000 units, watch step times.

### Follow-ups (not done)

- The owner has more lane/movement rules to add.
- Adaptive input delay from measured ping (fixed 3 ticks = 150 ms stalls when the round trip is longer).
- Reconnect mid-game (needs a state snapshot or replay catch-up).
- Replays: record `GameStart` + every `TickBundle`; replaying must reproduce the hashes.
- LAN discovery (LiteNetLib supports broadcast). Steam's relay later removes port forwarding.
- If the host's game closes, the in-process relay goes with it; the standalone server avoids that.

## Menu and lobby UI pass (done)

- **Look:** `UiTheme` builds one Godot `Theme` (fonts, colours, styles for every control type, variations such as
  `HeaderLabel`, `BigButton`, `SmallButton`, `InsetPanel`, `CardButton`, `ListRow`). `UiArt` paints the
  textures at startup (planked wood with an iron frame, corner brackets and rivets, wooden buttons, dark insets,
  check boxes). `shaders/menu_backdrop.gdshader` is the animated background (dusk sky, smoke, castle
  silhouettes, embers); `shaders/gold_text.gdshader` makes the "CASTLE FIGHT" title worn gold.
- Themes only pass down through Controls, so `App` keeps every menu under one themed full-screen `Control`
  (with the backdrop). The in-game HUD is not themed yet.
- **Screens:** `MainMenu` (Host Game / Join Game / Options / Exit), `HostScreen` (map cards, bots per army,
  port; *Open Lobby* or *Play Offline*), `JoinScreen` (favourite + recent servers, address form),
  `OptionsScreen` (name, window mode, frame cap, vsync), `LobbyScreen` (map, both armies with open places,
  ready check, `ChatPanel`).
- `MapIcon` draws any `MapDefinition` as an icon (lanes, forest walls, bases, build zones, castles), so new
  maps get an icon for free. `MapSelector` turns them into cards.
- Single player is now *Host Game → Play Offline* (your allies + enemy bots from the host setup).
- The protocol went to version 2 for chat (see the table above).
- Ideas: a ping per player in the lobby, kicking players, server status/ping for favourites (LiteNetLib
  unconnected messages), in-game chat, theming the HUD, sounds for clicks.

## Later steps (in rough order)

5. **Sim on its own thread.** The main thread renders by interpolating between the last two finished ticks,
   so a step can use most of the 50 ms budget without causing frame hitches. Do this when step times go
   above about 8 ms. On the step 4 maps 8,000 units take 3.6-5.0 ms, so this can wait until gameplay adds cost.
6. **Gameplay design (needs the owner's input).** Races and their buildings/units/tiers, the income
   model (Castle Fight style income/treasure), armor/attack types, projectiles, splash, auras/heals/slows,
   more `UnitBehavior`s. With 8 players there are more buildings, so watch total unit counts.
7. **Visuals.** See the visuals plan below. Also HP bars, build menu, minimap, end-of-game score screen
   (the placement ghost and tile grid were done in step 4).
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
  `--export-release "Windows Desktop" ../build/windows/CastleFight.exe`. In step 4 the portable
  `Godot_v4.7.2-stable_mono_win64.zip` was unpacked to `%TEMP%\godot472\` (use the `_console.exe` for output).
- After adding a script, run `--headless --path game --import` once so Godot writes its `.uid` file; commit it.
- Godot plays from the editor in Debug. `CastleFight.Sim` forces `<Optimize>true</Optimize>` because of that.
