using System.Globalization;

namespace CastleFight.Sim;

/// <summary>Receives every field of the simulation state in a fixed order (see <see cref="Simulation.VisitState{TSink}"/>).</summary>
internal interface IStateSink
{
    void Add(string name, long value);

    /// <summary>Ends a logical record (header, one player, one unit slot). Dumps write one record per line.</summary>
    void EndRecord();
}

public sealed partial class Simulation
{
    /// <summary>
    /// Hash of the full simulation state. Clients exchange this every few ticks; a mismatch
    /// means a desync (some code path was not deterministic).
    /// </summary>
    public ulong ComputeStateHash()
    {
        var sink = new HashSink();
        VisitState(ref sink);
        return sink.Value;
    }

    /// <summary>
    /// Human-readable dump of exactly the fields <see cref="ComputeStateHash"/> covers, one unit per line,
    /// so the dumps of two desynced clients can be diffed.
    /// </summary>
    public void DumpState(TextWriter writer)
    {
        writer.WriteLine($"# CastleFight state dump, map '{Map.Name}'");
        var sink = new TextSink(writer);
        VisitState(ref sink);
    }

    /// <summary>The single walker behind both the hash and the dump, so they can never cover different fields.</summary>
    internal void VisitState<TSink>(ref TSink sink) where TSink : struct, IStateSink
    {
        sink.Add("tick", Tick);
        sink.Add("winner", WinningTeam);
        sink.Add("rng", (long)_rng.State);
        sink.Add("nextUnitId", _nextUnitId);
        sink.Add("map", (long)Map.Fingerprint);
        sink.EndRecord();

        foreach (var player in _players)
        {
            sink.Add("player", player.Id);
            sink.Add("gold", player.Gold);
            var builder = player.Builder;
            sink.Add("builderX", builder.Pos.X.Raw);
            sink.Add("builderY", builder.Pos.Y.Raw);
            sink.Add("destX", builder.Destination.X.Raw);
            sink.Add("destY", builder.Destination.Y.Raw);
            sink.Add("order", (long)builder.Order);
            sink.Add("buildType", builder.BuildTypeId);
            sink.Add("siteX", builder.BuildTileX);
            sink.Add("siteY", builder.BuildTileY);
            sink.Add("progress", builder.BuildProgress);
            sink.Add("failedTick", builder.BuildFailedTick);
            sink.Add("failure", (long)builder.BuildFailure);
            sink.EndRecord();
        }

        var u = Units;
        sink.Add("highWater", u.HighWater);
        sink.EndRecord();
        for (int i = 0; i < u.HighWater; i++)
        {
            sink.Add("slot", i);
            sink.Add("alive", u.Alive[i] ? 1 : 0);
            if (u.Alive[i])
            {
                sink.Add("id", u.Id[i]);
                sink.Add("gen", u.Generation[i]);
                sink.Add("type", u.Type[i]);
                sink.Add("team", u.Team[i]);
                sink.Add("owner", u.Owner[i]);
                sink.Add("x", u.Pos[i].X.Raw);
                sink.Add("y", u.Pos[i].Y.Raw);
                sink.Add("hp", u.Hp[i]);
                sink.Add("target", u.Target[i]);
                sink.Add("targetGen", u.TargetGeneration[i]);
                sink.Add("cooldown", u.AttackCooldown[i]);
                sink.Add("retarget", u.RetargetTimer[i]);
                sink.Add("spawnTimer", u.SpawnTimer[i]);
                sink.Add("spread", u.SpreadFraction[i].Raw);
            }
            sink.EndRecord();
        }

        foreach (int slot in u.FreeSlots)
            sink.Add("free", slot);
        sink.EndRecord();
    }

    struct HashSink : IStateSink
    {
        ulong _value;

        public HashSink() => _value = 14695981039346656037UL;

        public void Add(string name, long value)
        {
            _value ^= (ulong)value;
            _value *= 0x9E3779B97F4A7C15UL;
            _value ^= _value >> 32;
        }

        public readonly void EndRecord() { }

        public readonly ulong Value => _value;
    }

    struct TextSink(TextWriter writer) : IStateSink
    {
        bool _lineStarted;

        public void Add(string name, long value)
        {
            if (_lineStarted)
                writer.Write(' ');
            writer.Write(name);
            writer.Write('=');
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
            _lineStarted = true;
        }

        public void EndRecord()
        {
            writer.WriteLine();
            _lineStarted = false;
        }
    }
}
