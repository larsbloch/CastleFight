using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim;

public sealed partial class Simulation
{
    /// <summary>How long a builder has to stand on a site before the building is done (5 s).</summary>
    public const int BuildTicks = 5 * TicksPerSecond;

    /// <summary>Builder speed in tiles per tick (8 tiles/s, about four times a footman).</summary>
    public static readonly Fix BuilderSpeed = UnitCatalog.TilesPerSecond(80);

    /// <summary>Builders start in front of their castle, 2 tiles apart across the map.</summary>
    const int BuilderStartGap = 2;
    const int BuilderSpacing = 2;

    /// <summary>The tiles a builder is walking to or building on (empty when it has no building order).</summary>
    public TileRect SiteOf(Builder builder)
    {
        if (builder.BuildTypeId < 0)
            return default;
        int size = Catalog[builder.BuildTypeId].FootprintTiles;
        return new TileRect(builder.BuildTileX, builder.BuildTileY, builder.BuildTileX + size, builder.BuildTileY + size);
    }

    FixVec2 BuilderStart(IReadOnlyList<byte> teams, int playerId)
    {
        byte team = teams[playerId];
        int index = 0, count = 0;
        for (int i = 0; i < teams.Count; i++)
        {
            if (teams[i] != team)
                continue;
            if (i < playerId)
                index++;
            count++;
        }

        var castle = Map.CastleFootprint(team);
        Fix x = team == 0 ? Fix.FromInt(castle.MaxX + BuilderStartGap) : Fix.FromInt(castle.MinX - BuilderStartGap);
        Fix y = castle.Center.Y + Fix.FromInt(BuilderSpacing * index * 2 - BuilderSpacing * (count - 1)) / 2;
        return new FixVec2(x, y);
    }

    void ApplyBuilderCommand(Player player, in Command command)
    {
        var builder = player.Builder;
        if (command.Kind == CommandKind.MoveBuilder)
        {
            StopBuilder(player);
            var to = command.Position;
            builder.Destination = new FixVec2(Fix.Clamp(to.X, Fix.Zero, Map.Length), Fix.Clamp(to.Y, Fix.Zero, Map.Width));
            builder.Order = BuilderOrder.Move;
            return;
        }

        // Build: the spot must be valid (and affordable) when ordered; it is checked again on arrival.
        if (!CanPlaceBuilding(player.Id, command.TypeId, command.TileX, command.TileY))
            return;
        StopBuilder(player);
        builder.BuildTypeId = command.TypeId;
        builder.BuildTileX = command.TileX;
        builder.BuildTileY = command.TileY;
        builder.Destination = SiteOf(builder).Center;
        builder.Order = BuilderOrder.GoToSite;
    }

    /// <summary>Drops the current order. A building under construction is abandoned and its gold refunded.</summary>
    void StopBuilder(Player player)
    {
        var builder = player.Builder;
        if (builder.Order == BuilderOrder.Construct)
            player.Gold += Catalog[builder.BuildTypeId].Cost;
        builder.Order = BuilderOrder.Idle;
        builder.BuildTypeId = -1;
        builder.BuildTileX = 0;
        builder.BuildTileY = 0;
        builder.BuildProgress = 0;
    }

    void StepBuilders()
    {
        foreach (var player in _players)
        {
            var builder = player.Builder;
            builder.PrevPos = builder.Pos;
            switch (builder.Order)
            {
                case BuilderOrder.Move:
                    if (WalkBuilder(builder))
                        builder.Order = BuilderOrder.Idle;
                    break;

                case BuilderOrder.GoToSite:
                    if (WalkBuilder(builder))
                        StartConstruction(player);
                    break;

                case BuilderOrder.Construct:
                    if (++builder.BuildProgress >= BuildTicks)
                        FinishConstruction(player);
                    break;
            }
        }
    }

    /// <summary>Moves the builder straight towards its destination; true once it is there.</summary>
    static bool WalkBuilder(Builder builder)
    {
        FixVec2 toGo = builder.Destination - builder.Pos;
        Fix distanceSq = toGo.LengthSquared;
        if (distanceSq <= BuilderSpeed * BuilderSpeed)
        {
            builder.Pos = builder.Destination;
            return true;
        }
        builder.Pos += toGo / Fix.Sqrt(distanceSq) * BuilderSpeed;
        return false;
    }

    void StartConstruction(Player player)
    {
        var builder = player.Builder;
        var result = CheckPlacement(player.Id, builder.BuildTypeId, builder.BuildTileX, builder.BuildTileY);
        if (result != PlacementResult.Ok)
        {
            builder.BuildFailedTick = Tick;
            builder.BuildFailure = result;
            StopBuilder(player);
            return;
        }
        player.Gold -= Catalog[builder.BuildTypeId].Cost;
        builder.Order = BuilderOrder.Construct;
        builder.BuildProgress = 0;
    }

    void FinishConstruction(Player player)
    {
        var builder = player.Builder;
        SpawnUnit(builder.BuildTypeId, player.Team, (byte)player.Id, SiteOf(builder).Center);
        builder.Order = BuilderOrder.Idle; // already paid: not a refund
        StopBuilder(player);
    }

    /// <summary>Sites being built on are taken, so no one else can build there in the meantime.</summary>
    bool OverlapsConstruction(TileRect footprint)
    {
        foreach (var player in _players)
        {
            if (player.Builder.Order == BuilderOrder.Construct && SiteOf(player.Builder).Overlaps(footprint))
                return true;
        }
        return false;
    }
}
