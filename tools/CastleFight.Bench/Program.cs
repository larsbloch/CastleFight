using System.Diagnostics;
using CastleFight.Sim;

// Headless stress test: spawns big armies right next to each other and measures how long each
// simulation step takes. At 20 ticks per second a step has a 50 ms budget, but it must leave
// room for rendering, so aim for well under 10 ms on the slowest machine you want to support.
//
//   dotnet run -c Release --project tools/CastleFight.Bench -- --units 1000,2000,4000 --ticks 400 --map three
//   --map is single, two, three (the catalog maps) or LxW for a custom single-lane map.

int[] unitsPerTeam = [500, 1000, 2000, 4000];
int ticks = 400;
MapDefinition map = MapCatalog.Get(MapId.SingleLane);
ulong seed = 42;

for (int i = 0; i + 1 < args.Length; i += 2)
{
    switch (args[i])
    {
        case "--units": unitsPerTeam = args[i + 1].Split(',').Select(int.Parse).ToArray(); break;
        case "--ticks": ticks = int.Parse(args[i + 1]); break;
        case "--seed": seed = ulong.Parse(args[i + 1]); break;
        case "--map": map = ParseMap(args[i + 1]); break;
        default: throw new ArgumentException($"Unknown option {args[i]}");
    }
}

#if DEBUG
Console.WriteLine("WARNING: Debug build. Use -c Release for meaningful numbers.");
#endif
Console.WriteLine($"Map {map.Name} ({map.LengthTiles}x{map.WidthTiles}, {map.Lanes.Count} lanes), {ticks} ticks per run, .NET {Environment.Version}, {Environment.ProcessorCount} logical cores");
Console.WriteLine("Each run: units/team split evenly over all unit types, both armies spawned facing each other.");
Console.WriteLine();

Run(500, 200); // warm up the JIT

Console.WriteLine($"{"units/team",10} {"start",7} {"end",7} {"avg ms",8} {"p95 ms",8} {"max ms",8} {"% of 50ms",10}");
foreach (int count in unitsPerTeam)
{
    var result = Run(count, ticks);
    Console.WriteLine($"{count,10} {result.StartAlive,7} {result.EndAlive,7} {result.Average,8:0.00} {result.P95,8:0.00} {result.Max,8:0.00} {result.Average / 50 * 100,9:0.0}%");
}

Console.WriteLine();
Console.Write("Determinism check (same seed run twice, hash compared every 10 ticks): ");
var first = Run(unitsPerTeam[0], ticks, collectHashes: true).Hashes;
var second = Run(unitsPerTeam[0], ticks, collectHashes: true).Hashes;
Console.WriteLine(first.SequenceEqual(second) ? $"OK, final hash {first[^1]:X16}" : "MISMATCH");

RunResult Run(int perTeam, int tickCount, bool collectHashes = false)
{
    var sim = new Simulation(GameSetup.ForTeams(seed, 1), map);
    var mobileTypes = sim.Catalog.MobileTypeIds;
    var spawn = new List<Command>();
    for (int player = 0; player < 2; player++)
    {
        for (int t = 0; t < mobileTypes.Count; t++)
        {
            int share = perTeam / mobileTypes.Count + (t < perTeam % mobileTypes.Count ? 1 : 0);
            spawn.Add(Command.DebugSpawn(player, (ushort)mobileTypes[t], share));
        }
    }
    sim.Step(spawn.ToArray());
    int startAlive = sim.Units.AliveCount;

    var times = new double[tickCount];
    var hashes = new List<ulong>();
    for (int tick = 0; tick < tickCount; tick++)
    {
        long start = Stopwatch.GetTimestamp();
        sim.Step([]);
        times[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (collectHashes && tick % 10 == 0)
            hashes.Add(sim.ComputeStateHash());
    }

    Array.Sort(times);
    return new RunResult(startAlive, sim.Units.AliveCount, times.Average(), times[(int)(tickCount * 0.95)], times[^1], hashes);
}

static MapDefinition ParseMap(string value)
{
    switch (value)
    {
        case "single": return MapCatalog.Get(MapId.SingleLane);
        case "two": return MapCatalog.Get(MapId.TwoLanes);
        case "three": return MapCatalog.Get(MapId.ThreeLanes);
    }
    var size = value.Split('x');
    return MapCatalog.CreateLaneMap($"Custom {value}", laneCount: 1, laneWidth: int.Parse(size[1]), length: int.Parse(size[0]));
}

record RunResult(int StartAlive, int EndAlive, double Average, double P95, double Max, List<ulong> Hashes);
