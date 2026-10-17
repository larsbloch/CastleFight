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
    public void The_building_is_done_after_the_builder_stood_on_the_site_for_five_seconds()
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
        Assert.Equal(sim.SiteOf(player.Builder).Center, player.Builder.Pos);
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

    static int CountAlive(Simulation sim, int typeId) =>
        Enumerable.Range(0, sim.Units.HighWater).Count(i => sim.Units.Alive[i] && sim.Units.Type[i] == typeId);
}
