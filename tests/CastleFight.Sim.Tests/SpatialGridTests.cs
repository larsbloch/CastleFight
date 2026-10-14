using CastleFight.Sim.FixedMath;

namespace CastleFight.Sim.Tests;

public class SpatialGridTests
{
    [Fact]
    public void Cells_contain_exactly_the_units_of_the_team_in_slot_order()
    {
        var sim = new Simulation(GameSetup.ForTeams(7, 1));
        sim.Step([Command.DebugSpawn(0, UnitCatalog.Footman, 500), Command.DebugSpawn(1, UnitCatalog.Archer, 500)]);
        var units = sim.Units;
        var grid = new SpatialGrid(sim.Map.Length, sim.Map.Width, Fix.FromInt(2));
        grid.Rebuild(units, team: 1);

        var seen = new List<int>();
        for (int y = 0; y < grid.Rows; y++)
        {
            for (int x = 0; x < grid.Columns; x++)
            {
                var cell = grid.Cell(x, y).ToArray();
                Assert.Equal(cell.Order(), cell);
                foreach (int slot in cell)
                {
                    Assert.Equal(x, grid.CellX(units.Pos[slot].X));
                    Assert.Equal(y, grid.CellY(units.Pos[slot].Y));
                }
                seen.AddRange(cell);
            }
        }

        var expected = Enumerable.Range(0, units.HighWater).Where(i => units.Alive[i] && units.Team[i] == 1);
        Assert.Equal(expected.Order(), seen.Order());
        Assert.Equal(seen.Count, grid.Count);
    }
}
