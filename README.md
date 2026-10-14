# Castle Fight

Lane battle game for 4-6 players. Godot 4.7 (.NET) + C#, desktop only, deterministic lockstep multiplayer.

## Layout

| Path | What |
|---|---|
| `src/CastleFight.Sim` | The whole game simulation in plain C#. No Godot references. Fixed-point math only. |
| `tests/CastleFight.Sim.Tests` | Determinism, gameplay and grid tests. |
| `tools/CastleFight.Bench` | Headless stress benchmark (ms per tick at N units). |
| `game/` | Godot project: rendering (MultiMesh), camera, input, HUD. Reads the simulation, never changes it except through commands. |

## Rules for code in `CastleFight.Sim`

Every client runs the simulation independently, so it must give bit-identical results everywhere:

- No `float`/`double`: use `Fix` / `FixVec2`.
- No `System.Random`, clocks or `delta` time: use `DeterministicRandom` and ticks.
- Iterate in slot order; break ties with unit `Id`. Never depend on `Dictionary`/`HashSet` order.
- No Godot APIs. Player actions enter only as `Command`s.

`Simulation.ComputeStateHash()` must match across clients; a mismatch is a desync bug.

## Commands

```
dotnet test CastleFight.slnx
dotnet run -c Release --project tools/CastleFight.Bench -- --units 500,1000,2000,4000
```

Godot: open `game/project.godot` in Godot 4.7 .NET and press F5.
Headless smoke test: `godot --headless --path game --fixed-fps 60 --quit-after 1200 -- --smoke`
