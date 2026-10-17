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
| Menu look | Weathered wood, iron and gold; Cinzel / Cinzel Decorative / Alegreya fonts (OFL). Textures are painted in code (`UiArt`), no image files. Menus and the in-game HUD scale with the window (1600x900 base); the 3D view renders at native resolution. |

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

`Command.PlaceBuilding(player, type, tileX, tileY)` (the footprint's lowest tile; since then players build with their builder, see "Builder"). The sim keeps a tile
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

## Terraced maps (done)

The three maps now follow the original Castle Fight layout. Heights are **only for looks**: to the sim, a cliff
is walls plus no-build ramps.

- Every map is now 240 long (`MapCatalog.CreateTerracedMap`). Team 0's side (team 1's is mirrored):

  | X | What | Level |
  |---|---|---|
  | 0-36 | Castle terrace: castle at 4-8, build zone 11-33 | 2 |
  | 36-38 | Upper cliff | |
  | 38-66 | Middle terrace: build zone 39-64 | 1 |
  | 66-68 | Lower cliff | |
  | 68-172 | Lanes (forest walls between them, as before) | 0 |

- Each cliff has one gap per lane, centred on the lane: 16 wide on Single Lane, 12 on the others. A ramp slopes
  through each gap and sticks out 4 tiles on the lower side. Nothing can be built on the slope plus a 2-tile
  landing at both ends (`Ramp.KeepClear`, `PlacementResult.OnRamp`, drawn red). Zones keep clear of the cliffs and
  the map edges, so a path around any row of buildings always remains.
- `MapDefinition`: `BuildZones(team)` (list), `Cliffs`, `Ramps`, `Terraces`, `LaneWalls` (forest) and
  `CliffWalls`. `Walls` is both kinds. Walls can be anywhere now, and each column has a bucket of the walls near
  it, so the per-unit wall checks stay cheap. `CreateLaneMap` still makes the old flat maps (tests, bench).
- Movement: the own-base flow field treats walls as blocked, so units head for the nearest ramp on their own.
  A second per-team field (`BaseFlowField.ToEnemyCastle`) covers the last 16 tiles of the lanes plus the enemy
  base and leads attackers up the enemy's ramps to the castle. In that field enemy buildings only cost extra
  (about a 12-tile detour), so a base walled in by buildings still leads attackers into them.
- Cliffs block line of sight like any wall, so nothing targets across a cliff.
- Godot: `TerrainHeights` (2 world units per level) lifts the units and the placement ghost onto the ground and
  makes mouse picking hit the terraces. `MapScenery` draws terrace blocks, stone cliff walls, ramp wedges and dirt
  roads from the castle to the ramps. `MapIcon` shows terraces, cliffs and ramps.
- Bench: about the same as before. A back-to-back run on a busy machine gave 8,000 units at 11.0 / 8.4 / 7.9
  ms/tick (single/two/three), against 9.9 / 9.6 / 8.3 on `main`. The absolute numbers in the table above came from
  a quieter run.
- Ideas: high-ground rules (miss chance uphill, ranged units shooting over cliffs), real cliff/ramp art, trees
  on the forest walls.

## Map textures (done)

- `TerrainArt` paints seamless textures in code at the first match (like `UiArt`, no image files): green grass
  with blade strokes, dirt with pebbles, cliff stone in courses of blocks with mortar and moss, forest canopy
  (tree crowns from above), and a soft variation noise.
- `shaders/ground.gdshader` draws every terrace block and ramp: grass on top, packed earth on the sides, and dirt
  wherever a map-sized path mask (`TerrainArt.PathMask`, 4 px per tile) says so. The mask comes from
  `MapScenery.Paths`: each lane with a grass verge on both sides, plus 6-wide roads from the castle over the ramps
  into the lanes. Noise frays the path edges and a large-scale variation breaks up the tiling. The old road planes
  are gone.
- Cliff walls (stone) and forest walls use `StandardMaterial3D` world triplanar mapping, so blocks of any size never
  stretch the texture. Tile sizes are `TerrainArt.*Tile` (world units per repeat).
- Ramp wedges now have flat normals (they were smoothed across edges, which shaded them oddly).
- To use hand-painted art later: load the image in the matching `TerrainArt` property and adjust its tile size.

## Menu and lobby UI pass (done)

- **Look:** `UiTheme` builds one Godot `Theme` (fonts, colours, styles for every control type, variations such as
  `HeaderLabel`, `BigButton`, `SmallButton`, `InsetPanel`, `CardButton`, `ListRow`). `UiArt` paints the
  textures at startup (planked wood with an iron frame, corner brackets and rivets, wooden buttons, dark insets,
  check boxes). `shaders/menu_backdrop.gdshader` is the animated background (dusk sky, smoke, castle
  silhouettes, embers); `shaders/gold_text.gdshader` makes the "CASTLE FIGHT" title worn gold.
- Themes only pass down through Controls, so `App` keeps every menu under one themed full-screen `Control`
  (with the backdrop). The in-game HUD sets the same theme (`UiTheme.Shared`) on itself.
- **Screens:** `MainMenu` (Host Game / Join Game / Options / Exit), `HostScreen` (map cards, port,
  *Open Lobby*; bots are added in the lobby), `JoinScreen` (favourite + recent servers, address form),
  `OptionsScreen` (name, window mode, frame cap, vsync), `LobbyScreen` (map, both armies with open places,
  ready check, `ChatPanel`).
- `MapIcon` draws any `MapDefinition` as an icon (lanes, forest walls, bases, build zones, castles), so new
  maps get an icon for free. `MapSelector` turns them into cards.
- Single player is *Host Game → Open Lobby*, add bots and start. The offline `LocalSession` is only used from the
  command line (`--map N`, `--smoke`).
- The protocol went to version 2 for chat (see the table above).
- Ideas: a ping per player in the lobby, kicking players, server status/ping for favourites (LiteNetLib
  unconnected messages), in-game chat, sounds for clicks.

## In-game HUD (done)

Warcraft 3 style, in `ui/GameHud.cs`. The stone is painted in code too (`UiArt.Hud.cs`): a seamless stone-block
texture repeated over every shape with world-aligned UVs, plus bronze-rimmed wells, metal-rimmed slots and navy/gold
top-bar buttons (theme variations `HudButton`, `HudWell`, `HudSlot`, `HudResource`, `HudValueLabel`, `HudPlaqueLabel`,
`HudDebugLabel`).

- **Top bar:** *Menu (F10)* and *Debug (F11)*; the space where WC3 has Quests/Allies/Log is left empty. A medallion in
  the middle (decoration only). Gold, wood and food on the right (`ResourceIcon` draws the coin, tree and drumstick);
  wood and food always show 0 until they get rules. No "upkeep" box.
- **Menu:** Return to Game, Restart (single player only), Leave Match. It pauses a single player game; online the
  game goes on. `Esc` closes it.
- **Debug:** shows/hides the old text readout (FPS, units, tick, sim step, hash, ping, build keys, controls). The
  choice is saved in `settings.cfg` (`[hud] debug`). Notices such as "X left the game" are always shown, above the
  minimap.
- **Bottom console:** a castle wall with battlements. Left tower: `Minimap` (a `MapIcon` letterboxed to the map's
  aspect, blue/red dots per unit and squares per building, refreshed 10 times a second; dense crowds are drawn as one
  dot per cell; click/drag moves the camera). Middle: an arched portrait frame and the unit stats well (both empty).
  Right tower (taller than the left one): inventory (2x3) and the **command HUD** (4 wide, 3 high; that is what we
  call it, not "command card").
- Only the stone takes the mouse (`GameHud._HasPoint`); clicks and the wheel anywhere else go to the game, and the
  placement ghost hides while the mouse is over the HUD.
- The window now uses `canvas_items` scaling in matches as well (it used to be off), so the HUD is laid out at
  1600x900 or wider and scales up crisply. 3D still renders at the window's resolution.
- Next: HP bars, selecting other things than the builder (buildings, units).

## Builder (done)

Every player has one builder; buildings are no longer placed instantly by the player.

- **Sim** (`Builder.cs`, `Simulation.Builders.cs`): `Player.Builder` holds position, destination, order
  (`Idle`, `Move`, `GoToSite`, `Construct`), the site, the progress, the path and the queue; all of it is in the state
  hash. Builders are not units: they are not in `UnitPool`, cannot be attacked and do not collide with units. They
  start in front of their castle, 2 tiles apart, and walk at 8 tiles/s (`Simulation.BuilderSpeed`).
- **Pathing:** A* over tiles (8 directions, no cutting corners) around walls, cliffs, buildings and construction sites,
  then straightened wherever a direct line keeps 3/8 tile clear of them and a whole tile clear of walls (`PlanPath`).
  Tiles touching a wall cost a bit more, so the wide star keeps off cliffs and uses the middle of ramps. A walking builder plans again
  whenever a building or site appears or goes (`_obstacleVersion`). A move into a wall ends as close as it can get; a
  builder that a building was finished on top of walks out of it.
- **Commands:** `MoveBuilder(player, position, queued)` (the position goes in `TileX`/`TileY` in steps of 1/16 tile)
  and `Build(player, type, tileX, tileY, queued)`; `queued` is `Command.QueuedFlag` in `Count`, so the wire format is
  unchanged (protocol 3). Without it an order clears the queue and replaces the current order (abandoning a site
  under construction refunds the gold); with it the order waits in `Builder.Queue` (at most
  `Simulation.MaxQueuedOrders`) until the others are done. A build order must pass `CheckPlacement` when given; a
  queued one may wait for the gold but must not overlap the builder's other planned sites (`CheckPlacement(...,
  queued: true)`). The builder walks to the nearest free tile next to the site (corners count) and checks again there.
  If the spot is taken, gold ran out or no tile next to it can be reached, it gives up (`BuildFailedTick`/
  `BuildFailure`, shown as a message) and goes on with its queue. Otherwise it pays and constructs: after
  `Simulation.BuildTicks` (5 s) the building appears. A site under construction and the tile its builder stands on
  count as occupied for everyone.
- `PlaceBuilding` (instant) is still there for tests and the bench. `ScriptedBot` now sends its builder when it is
  idle.
- **Godot:** `BuilderView` draws a spinning star per builder (team colour), a green ring under the selected local
  builder, a rising box on each construction site and faint boxes on the sites the local builder is heading to or has
  queued. `MoveMarker` shows where a right click sent it: green circles closing in for 2 s, then fading out. Builders
  are diamonds on the minimap.
- **Controls:** the builder starts selected. `F1` or LMB on the builder selects it, LMB anywhere else deselects. RMB
  moves it. While it is selected the command HUD shows the 4 buildings (slots 1-4, name, cost in red if you can't
  afford it; `CommandButton`), hotkeys 1-4. Picking one starts placing (the placement ghost); LMB orders the build,
  RMB/Esc cancels. Shift+LMB / Shift+RMB queue instead; after Shift+LMB the building stays picked. The portrait shows
  the star and the stats well what the builder is doing and how many orders are queued.
- Ideas: keep the builder from walking into the enemy base, show the queued path and moves, a second `F1` press
  centres the camera on the builder, a model instead of the star.

## Later steps (in rough order)

5. **Sim on its own thread.** The main thread renders by interpolating between the last two finished ticks,
   so a step can use most of the 50 ms budget without causing frame hitches. Do this when step times go
   above about 8 ms. On the step 4 maps 8,000 units take 3.6-5.0 ms, so this can wait until gameplay adds cost.
6. **Gameplay design (needs the owner's input).** Races and their buildings/units/tiers, the income
   model (Castle Fight style income/treasure), armor/attack types, projectiles, splash, auras/heals/slows,
   more `UnitBehavior`s. With 8 players there are more buildings, so watch total unit counts.
7. **Visuals.** See the visuals plan below. Also HP bars, end-of-game score screen
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
- `build/build-windows.ps1` (or double-click `build/build-windows.cmd`) does the whole release build and downloads
  a portable Godot into `build/tools` if none is installed. That is also the easiest Godot to use for automated
  checks: `build/tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe`.
- Godot itself may not be installed yet: `winget install GodotEngine.GodotEngine.Mono`. For automated
  checks, the portable editor zip from GitHub releases works headless:
  `--headless --path game --import`, then the smoke test from the README, or
  `--export-release "Windows Desktop" ../build/windows/CastleFight.exe`. In step 4 the portable
  `Godot_v4.7.2-stable_mono_win64.zip` was unpacked to `%TEMP%\godot472\` (use the `_console.exe` for output).
- After adding a script, run `--headless --path game --import` once so Godot writes its `.uid` file; commit it.
- Godot plays from the editor in Debug. `CastleFight.Sim` forces `<Optimize>true</Optimize>` because of that.
