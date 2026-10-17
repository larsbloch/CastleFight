using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Tests;

public class BuilderTests
{
    static Simulation TwoVsTwo(int gold = 1000) =>
        new(new GameSetup(1, [0, 0, 1, 1]) { StartingGold = gold });

    [Fact]
    public void Every_player_starts_with_an_idle_builder_in_front_of_their_castle()
    {
        var sim = TwoVsTwo();
        foreach (var player in sim.Players)
        {
            var builder = player.Builder;
            Assert.Equal(BuilderOrder.Idle, builder.Order);
            Assert.True(sim.Map.IsInOwnBase(player.Team, builder.Pos.X));
            Assert.False(sim.Map.IsInsideWall(builder.Pos, Fix.Zero));
        }
        Assert.Equal(4, sim.Players.Select(p => p.Builder.Pos).Distinct().Count());
    }

    [Fact]
    public void A_moved_builder_walks_at_builder_speed_and_stops_at_the_destination()
    {
        var sim = TwoVsTwo();
        var builder = sim.Players[0].Builder;
        var destination = builder.Pos + new FixVec2(Fix.FromInt(20), Fix.FromInt(3));

        sim.Step([Command.MoveBuilder(0, destination)]);
        Assert.Equal(BuilderOrder.Move, builder.Order);
        Assert.True((builder.Pos - builder.PrevPos).Length <= Simulation.BuilderSpeed + Fix.FromRatio(1, 100));

        for (int tick = 0; tick < Simulation.TicksPerSecond * 4; tick++)
            sim.Step([]);
        Assert.Equal(BuilderOrder.Idle, builder.Order);
        Assert.Equal(destination, builder.Pos);
    }

    [Fact]
    public void The_building_is_done_after_the_builder_stood_next_to_the_site_for_five_seconds()
    {
        var sim = TwoVsTwo();
        var player = sim.Players[0];
        var zone = sim.Map.BuildZones(0)[0];
        int cost = sim.Catalog[UnitCatalog.Barracks].Cost;

        sim.Step([Command.Build(0, UnitCatalog.Barracks, zone.MinX + 6, zone.MinY + 6)]);
        int goldWhenOrdered = player.Gold;
        Assert.Equal(BuilderOrder.GoToSite, player.Builder.Order);

        int ticks = 0;
        while (player.Builder.Order == BuilderOrder.GoToSite && ticks++ < 1000)
            sim.Step([]);
        Assert.Equal(BuilderOrder.Construct, player.Builder.Order);
        AssertNextToSite(sim, player.Builder);
        Assert.True(player.Gold < goldWhenOrdered); // paid when construction starts
        Assert.Equal(0, CountAlive(sim, UnitCatalog.Barracks));

        for (int tick = 1; tick < Simulation.BuildTicks; tick++)
            sim.Step([]);
        Assert.Equal(0, CountAlive(sim, UnitCatalog.Barracks));
        sim.Step([]);
        Assert.Equal(1, CountAlive(sim, UnitCatalog.Barracks));
        Assert.Equal(BuilderOrder.Idle, player.Builder.Order);
        Assert.True(sim.ValidateOccupancy());
        Assert.Equal(sim.Players[1].Gold - cost, player.Gold); // the teammate got the same income
    }

    [Fact]
    public void Walking_away_from_a_site_abandons_it_and_refunds_the_gold()
    {
        var sim = TwoVsTwo();
        var player = sim.Players[0];
        var zone = sim.Map.BuildZones(0)[0];
        sim.Step([Command.Build(0, UnitCatalog.Barracks, zone.MinX, zone.MinY)]);
        while (player.Builder.Order != BuilderOrder.Construct)
            sim.Step([]);
        for (int tick = 0; tick < Simulation.BuildTicks / 2; tick++)
            sim.Step([]);

        sim.Step([Command.MoveBuilder(0, player.Builder.Pos + new FixVec2(Fix.FromInt(5), Fix.Zero))]);
        for (int tick = 0; tick < Simulation.BuildTicks * 2; tick++)
            sim.Step([]);

        Assert.Equal(0, CountAlive(sim, UnitCatalog.Barracks));
        Assert.Equal(sim.Players[1].Gold, player.Gold);
        Assert.True(sim.CanPlaceBuilding(1, UnitCatalog.Barracks, zone.MinX, zone.MinY));
    }

    [Fact]
    public void A_site_under_construction_is_taken_and_the_late_builder_gives_up()
    {
        var sim = TwoVsTwo();
        var zone = sim.Map.BuildZones(0)[0];
        int x = zone.MinX + 2, y = zone.MinY + 2;
        // Player 0 builds right away; player 1 is sent somewhere far first, then to the same spot.
        sim.Step([Command.Build(0, UnitCatalog.Barracks, x, y)]);
        while (sim.Players[0].Builder.Order != BuilderOrder.Construct)
            sim.Step([]);

        Assert.Equal(PlacementResult.Occupied, sim.CheckPlacement(1, UnitCatalog.Kennel, x + 1, y + 1));
        sim.Step([Command.Build(1, UnitCatalog.Kennel, x + 1, y + 1)]);
        Assert.Equal(BuilderOrder.Idle, sim.Players[1].Builder.Order); // rejected when ordered

        Assert.True(sim.CanPlaceBuilding(1, UnitCatalog.Kennel, x + 2, y));
        sim.Step([Command.Build(1, UnitCatalog.Kennel, x + 2, y)]); // touching is fine
        Assert.Equal(BuilderOrder.GoToSite, sim.Players[1].Builder.Order);
    }

    [Fact]
    public void A_spot_taken_while_walking_there_fails_on_arrival()
    {
        var sim = TwoVsTwo();
        var zone = sim.Map.BuildZones(0)[0];
        var builder = sim.Players[1].Builder;
        sim.Step([Command.Build(1, UnitCatalog.Barracks, zone.MaxX - 4, zone.MinY)]);
        sim.Step([Command.PlaceBuilding(0, UnitCatalog.Kennel, zone.MaxX - 4, zone.MinY)]);
        int gold = sim.Players[1].Gold;

        for (int tick = 0; tick < 200 && builder.IsBusy; tick++)
            sim.Step([]);

        Assert.Equal(BuilderOrder.Idle, builder.Order);
        Assert.Equal(PlacementResult.Occupied, builder.BuildFailure);
        Assert.True(builder.BuildFailedTick > 0);
        Assert.True(sim.Players[1].Gold >= gold); // nothing paid
        Assert.Equal(0, CountAlive(sim, UnitCatalog.Barracks));
    }

    [Fact]
    public void Invalid_build_orders_are_ignored()
    {
        var sim = TwoVsTwo(gold: 50);
        var zone = sim.Map.BuildZones(0)[0];
        var enemyZone = sim.Map.BuildZones(1)[0];
        sim.Step([
            Command.Build(0, UnitCatalog.Barracks, zone.MinX, zone.MinY), // not enough gold
            Command.Build(1, UnitCatalog.Footman, zone.MinX, zone.MinY), // not a building
            Command.Build(1, UnitCatalog.Kennel, enemyZone.MinX, enemyZone.MinY), // enemy zone
        ]);
        Assert.All(sim.Players, p => Assert.Equal(BuilderOrder.Idle, p.Builder.Order));
    }

    [Fact]
    public void Move_destinations_survive_the_command_encoding_to_a_sixteenth_of_a_tile()
    {
        var at = new FixVec2(Fix.FromRatio(12345, 100), Fix.FromRatio(-77, 10));
        var decoded = Command.MoveBuilder(3, at).Position;
        Assert.True(Fix.Abs(decoded.X - at.X) < Fix.FromRatio(1, Command.PositionSteps));
        Assert.True(Fix.Abs(decoded.Y - at.Y) < Fix.FromRatio(1, Command.PositionSteps));
    }

    [Fact]
    public void The_tile_the_builder_builds_from_is_taken_while_it_builds()
    {
        var sim = TwoVsTwo();
        var zone = sim.Map.BuildZones(0)[0];
        var builder = sim.Players[0].Builder;
        sim.Step([Command.Build(0, UnitCatalog.Barracks, zone.MinX + 6, zone.MinY + 6)]);
        while (builder.Order != BuilderOrder.Construct)
            sim.Step([]);

        int x = builder.Pos.X.FloorToInt(), y = builder.Pos.Y.FloorToInt();
        Assert.Equal(PlacementResult.Occupied, sim.CheckPlacement(1, UnitCatalog.Kennel, x, y));
        while (builder.IsBusy)
            sim.Step([]);
        Assert.True(sim.CanPlaceBuilding(1, UnitCatalog.Kennel, x, y));
    }

    [Fact]
    public void The_builder_walks_around_buildings_instead_of_through_them()
    {
        var sim = TwoVsTwo(gold: 100_000);
        var builder = sim.Players[0].Builder;
        // A fence of barracks across the castle terrace, between the builder and where it is sent.
        for (int y = 12; y < 36; y += 2)
            sim.Step([Command.PlaceBuilding(0, UnitCatalog.Barracks, 20, y)]);
        var destination = new FixVec2(Fix.FromInt(28), builder.Pos.Y);

        sim.Step([Command.MoveBuilder(0, destination)]);
        int ticks = 0;
        for (; ticks < 400 && builder.IsBusy; ticks++)
        {
            sim.Step([]);
            Assert.Equal(-1, sim.TileOccupant(builder.Pos.X.FloorToInt(), builder.Pos.Y.FloorToInt()));
        }
        Assert.Equal(destination, builder.Pos);
        Assert.True(ticks > Simulation.TicksPerSecond * 3); // straight through would take under 2.5 s
    }

    [Fact]
    public void The_builder_takes_the_ramp_instead_of_crossing_a_cliff()
    {
        var sim = TwoVsTwo();
        var builder = sim.Players[0].Builder;
        var destination = new FixVec2(Fix.FromInt(45), Fix.FromInt(5)); // middle terrace, far from the ramp
        sim.Step([Command.MoveBuilder(0, destination)]);
        for (int tick = 0; tick < 400 && builder.IsBusy; tick++)
        {
            sim.Step([]);
            Assert.False(sim.Map.IsInsideWall(builder.Pos, Fix.Zero));
        }
        Assert.Equal(destination, builder.Pos);
    }

    [Theory]
    [InlineData(MapId.SingleLane)]
    [InlineData(MapId.TwoLanes)]
    [InlineData(MapId.ThreeLanes)]
    public void The_builder_never_crosses_a_wall_on_the_way_anywhere(MapId map)
    {
        var sim = new Simulation(new GameSetup(7, [0, 1]) { Map = map });
        var builder = sim.Players[0].Builder;
        var rng = new DeterministicRandom(42);
        for (int order = 0; order < 40; order++)
        {
            var destination = new FixVec2(rng.NextFix(Fix.Zero, sim.Map.Length), rng.NextFix(Fix.Zero, sim.Map.Width));
            sim.Step([Command.MoveBuilder(0, destination)]);
            for (int tick = 0; tick < 2000 && builder.IsBusy; tick++)
            {
                sim.Step([]);
                Assert.False(sim.Map.IsInsideWall(builder.Pos, Fix.Zero), $"order {order}: {builder.Pos} is in a wall");
                Assert.False(sim.Map.SegmentCrossesWall(builder.PrevPos, builder.Pos), $"order {order}: {builder.PrevPos} -> {builder.Pos}");
            }
            Assert.False(builder.IsBusy);
        }
    }

    [Fact]
    public void A_move_into_a_wall_stops_as_close_as_it_can()
    {
        var sim = TwoVsTwo();
        var builder = sim.Players[0].Builder;
        sim.Step([Command.MoveBuilder(0, new FixVec2(Fix.FromInt(37), Fix.FromInt(5)))]); // inside the cliff
        for (int tick = 0; tick < 400 && builder.IsBusy; tick++)
            sim.Step([]);
        Assert.Equal(BuilderOrder.Idle, builder.Order);
        Assert.False(sim.Map.IsInsideWall(builder.Pos, Fix.Zero));
        Assert.True(FixVec2.DistanceSquared(builder.Pos, new FixVec2(Fix.FromInt(37), Fix.FromInt(5))) < Fix.FromInt(4));
    }

    [Fact]
    public void A_building_put_in_the_way_makes_the_builder_go_around_it()
    {
        var sim = TwoVsTwo(gold: 100_000);
        var builder = sim.Players[0].Builder;
        var destination = new FixVec2(Fix.FromInt(30), builder.Pos.Y);
        sim.Step([Command.MoveBuilder(0, destination)]);
        sim.Step([]);
        int y = builder.Pos.Y.FloorToInt();
        sim.Step([Command.PlaceBuilding(1, UnitCatalog.Barracks, 20, y - 1)]);
        for (int tick = 0; tick < 400 && builder.IsBusy; tick++)
        {
            sim.Step([]);
            Assert.Equal(-1, sim.TileOccupant(builder.Pos.X.FloorToInt(), builder.Pos.Y.FloorToInt()));
        }
        Assert.Equal(destination, builder.Pos);
    }

    [Fact]
    public void Queued_orders_run_one_after_another()
    {
        var sim = TwoVsTwo();
        var player = sim.Players[0];
        var builder = player.Builder;
        var zone = sim.Map.BuildZones(0)[0];
        var end = new FixVec2(Fix.FromInt(14), Fix.FromInt(6));
        sim.Step([
            Command.Build(0, UnitCatalog.Barracks, zone.MinX + 4, zone.MinY + 4),
            Command.Build(0, UnitCatalog.Kennel, zone.MinX + 10, zone.MinY + 4, queued: true),
            Command.MoveBuilder(0, end, queued: true),
        ]);
        Assert.Equal(BuilderOrder.GoToSite, builder.Order);
        Assert.Equal(2, builder.Queue.Count);

        for (int tick = 0; tick < 1000 && builder.IsBusy; tick++)
            sim.Step([]);
        Assert.Equal(1, CountAlive(sim, UnitCatalog.Barracks));
        Assert.Equal(1, CountAlive(sim, UnitCatalog.Kennel));
        Assert.Equal(end, builder.Pos);
        Assert.Empty(builder.Queue);
    }

    [Fact]
    public void An_order_without_shift_clears_the_queue()
    {
        var sim = TwoVsTwo();
        var builder = sim.Players[0].Builder;
        var zone = sim.Map.BuildZones(0)[0];
        sim.Step([
            Command.Build(0, UnitCatalog.Barracks, zone.MinX + 4, zone.MinY + 4),
            Command.Build(0, UnitCatalog.Kennel, zone.MinX + 10, zone.MinY + 4, queued: true),
        ]);
        var destination = builder.Pos + new FixVec2(Fix.FromInt(3), Fix.Zero);
        sim.Step([Command.MoveBuilder(0, destination)]);
        Assert.Equal(BuilderOrder.Move, builder.Order);
        Assert.Empty(builder.Queue);

        for (int tick = 0; tick < 200 && builder.IsBusy; tick++)
            sim.Step([]);
        Assert.Equal(0, CountAlive(sim, UnitCatalog.Barracks) + CountAlive(sim, UnitCatalog.Kennel));
    }

    [Fact]
    public void A_queued_move_waits_for_the_building_to_be_done()
    {
        var sim = TwoVsTwo();
        var builder = sim.Players[0].Builder;
        var zone = sim.Map.BuildZones(0)[0];
        sim.Step([Command.Build(0, UnitCatalog.Barracks, zone.MinX + 4, zone.MinY + 4)]);
        while (builder.Order != BuilderOrder.Construct)
            sim.Step([]);
        sim.Step([Command.MoveBuilder(0, builder.Pos + new FixVec2(Fix.FromInt(4), Fix.Zero), queued: true)]);
        Assert.Equal(BuilderOrder.Construct, builder.Order);
        while (builder.Order == BuilderOrder.Construct)
            sim.Step([]);
        Assert.Equal(1, CountAlive(sim, UnitCatalog.Barracks));
        Assert.Equal(BuilderOrder.Move, builder.Order);
    }

    [Fact]
    public void Queued_builds_may_wait_for_gold_but_not_overlap_each_other()
    {
        var sim = TwoVsTwo(gold: 50);
        var builder = sim.Players[0].Builder;
        var zone = sim.Map.BuildZones(0)[0];
        int x = zone.MinX + 4, y = zone.MinY + 4;
        sim.Step([
            Command.Build(0, UnitCatalog.Barracks, x, y, queued: true), // an empty queue starts at once
            Command.Build(0, UnitCatalog.Barracks, x + 4, y, queued: true),
            Command.Build(0, UnitCatalog.Kennel, x + 5, y + 1, queued: true), // overlaps the queued site: ignored
        ]);
        Assert.Single(builder.Queue);
        Assert.Equal(PlacementResult.Occupied, sim.CheckPlacement(0, UnitCatalog.Kennel, x + 1, y + 1, queued: true));
        Assert.Equal(PlacementResult.NotEnoughGold, sim.CheckPlacement(0, UnitCatalog.Kennel, x + 1, y + 1));
    }

    [Fact]
    public void A_builder_inside_a_new_building_walks_out_of_it()
    {
        var sim = TwoVsTwo(gold: 100_000);
        var builder = sim.Players[0].Builder;
        var zone = sim.Map.BuildZones(0)[0];
        var inside = new FixVec2(Fix.FromInt(zone.MinX + 5), Fix.FromInt(zone.MinY + 5));
        sim.Step([Command.MoveBuilder(0, inside)]);
        while (builder.IsBusy)
            sim.Step([]);
        sim.Step([Command.PlaceBuilding(1, UnitCatalog.Barracks, zone.MinX + 4, zone.MinY + 4)]);
        Assert.NotEqual(-1, sim.TileOccupant(builder.Pos.X.FloorToInt(), builder.Pos.Y.FloorToInt()));

        var destination = inside + new FixVec2(Fix.FromInt(6), Fix.Zero);
        sim.Step([Command.MoveBuilder(0, destination)]);
        for (int tick = 0; tick < 200 && builder.IsBusy; tick++)
            sim.Step([]);
        Assert.Equal(destination, builder.Pos);
    }

    [Fact]
    public void The_queued_flag_survives_the_command_encoding()
    {
        Assert.True(Command.MoveBuilder(0, FixVec2.Zero, queued: true).IsQueued);
        Assert.True(Command.Build(0, UnitCatalog.Barracks, 1, 2, queued: true).IsQueued);
        Assert.False(Command.Build(0, UnitCatalog.Barracks, 1, 2).IsQueued);
    }

    static void AssertNextToSite(Simulation sim, Builder builder)
    {
        var site = sim.SiteOf(builder);
        int x = builder.Pos.X.FloorToInt(), y = builder.Pos.Y.FloorToInt();
        Assert.False(site.Contains(x, y));
        Assert.True(x >= site.MinX - 1 && x <= site.MaxX && y >= site.MinY - 1 && y <= site.MaxY);
    }

    static int CountAlive(Simulation sim, int typeId) =>
        Enumerable.Range(0, sim.Units.HighWater).Count(i => sim.Units.Alive[i] && sim.Units.Type[i] == typeId);
}
