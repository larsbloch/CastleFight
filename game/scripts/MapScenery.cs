using System.Linq;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Static map visuals: terraces as solid blocks of ground, cliff walls with ramps between them, lane roads,
/// forest walls, team build zones with a tile grid, plus light and environment. Built once per match from the
/// <see cref="MapDefinition"/>. Sim (x, y) is world (x, height, y); see <see cref="TerrainHeights"/>.
/// </summary>
public partial class MapScenery : Node3D
{
    const float ForestHeight = 2.5f;
    const float ParapetHeight = 0.5f;
    const float GroundDepth = 1f;
    const float RoadWidth = 6f;

    // Base roads lie over the zone tint (so they keep their colour) and under the grid.
    const float RoadLift = 0.025f;

    public void Build(MapDefinition map)
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.11f, 0.12f, 0.15f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.65f, 0.65f, 0.7f),
            AmbientLightEnergy = 0.7f,
        };
        AddChild(new WorldEnvironment { Environment = environment });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55f, -35f, 0f), LightEnergy = 1.1f });

        // Each terrace is a block from below the lanes up to its level, so the map edge looks solid.
        foreach (var terrace in map.Terraces)
        {
            var grass = new Color(0.27f, 0.34f, 0.22f).Lightened(0.06f * terrace.Level);
            AddBlock(terrace.Area, -GroundDepth, TerrainHeights.LevelToHeight(terrace.Level), Lit(grass));
        }

        var road = Lit(new Color(0.42f, 0.38f, 0.28f));
        foreach (var lane in map.Lanes)
            AddPlane(lane.Corridor, 0.01f, road);
        AddBaseRoads(map, road);

        var forest = Lit(new Color(0.12f, 0.2f, 0.11f));
        foreach (var wall in map.LaneWalls)
            AddBlock(wall, 0f, ForestHeight, forest);

        var stone = Lit(new Color(0.62f, 0.58f, 0.48f));
        foreach (var cliff in map.Cliffs)
        {
            float top = TerrainHeights.LevelToHeight(cliff.UpperLevel) + ParapetHeight;
            foreach (var wall in map.CliffWalls.Where(w => w.MinX == cliff.MinX))
                AddBlock(wall, -GroundDepth, top, stone);
        }

        var rampMaterial = Lit(new Color(0.45f, 0.4f, 0.3f));
        rampMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        foreach (var ramp in map.Ramps)
            AddRamp(ramp, rampMaterial);

        var keepClear = Unshaded(new Color(0.85f, 0.12f, 0.05f, 0.45f));
        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            foreach (var zone in map.BuildZones(team))
            {
                float height = TerrainHeights.LevelToHeight(map.TerraceAt(zone.MinX)?.Level ?? 0);
                AddPlane(zone, height + 0.02f, Unshaded(UnitRenderer.TeamColors[team] with { A = 0.15f }));
                AddGrid(zone, height + 0.03f, Unshaded(new Color(1f, 1f, 1f, 0.22f)));

                // Mark where the zone runs over a ramp's landing: nothing can be built there.
                foreach (var ramp in map.Ramps)
                {
                    if (Intersect(zone, ramp.KeepClear) is { } blocked)
                        AddPlane(blocked, height + 0.028f, keepClear);
                }
            }
        }
    }

    /// <summary>Dirt roads on the terraces: from each ramp to the next, joined up in front of the castle.</summary>
    void AddBaseRoads(MapDefinition map, Material road)
    {
        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            var castle = map.CastleFootprint(team);
            var cliffs = map.Cliffs.Where(c => team == 0 ? c.MaxX <= map.LaneMinX : c.MinX >= map.LaneMaxX)
                .OrderBy(c => team == 0 ? c.MinX : -c.MinX).ToArray();
            if (cliffs.Length == 0)
                continue;

            float castleFront = team == 0 ? castle.MaxX + 2f : castle.MinX - 2f;
            var gaps = cliffs[0].Gaps;
            foreach (var (minY, maxY) in gaps)
            {
                float y = (minY + maxY) * 0.5f;
                float x0 = castleFront;
                foreach (var cliff in cliffs)
                {
                    float x1 = team == 0 ? cliff.MinX : cliff.MaxX;
                    AddRoad(x0, x1, y, TerrainHeights.LevelToHeight(cliff.UpperLevel), road);
                    x0 = team == 0 ? cliff.MaxX : cliff.MinX;
                }
            }

            // Join the roads with a cross road in front of the castle, and lead it to the castle gate.
            float firstY = (gaps[0].MinY + gaps[0].MaxY) * 0.5f, lastY = (gaps[^1].MinY + gaps[^1].MaxY) * 0.5f;
            float castleHeight = TerrainHeights.LevelToHeight(cliffs[0].UpperLevel);
            var cross = new Rect2(castleFront - RoadWidth / 2, firstY - RoadWidth / 2, RoadWidth, lastY - firstY + RoadWidth);
            AddPlane(cross, castleHeight + RoadLift, road);
            float gateX = team == 0 ? castle.MaxX : castle.MinX;
            AddRoad(gateX, castleFront, castle.Center.Y.ToFloat(), castleHeight, road);
        }
    }

    void AddRoad(float fromX, float toX, float y, float height, Material material) =>
        AddPlane(new Rect2(Mathf.Min(fromX, toX), y - RoadWidth / 2, Mathf.Abs(toX - fromX), RoadWidth), height + RoadLift, material);

    /// <summary>A wedge filling the cliff gap: sloped on top, solid down to below the lanes.</summary>
    void AddRamp(Ramp ramp, Material material)
    {
        var s = ramp.Slope;
        float high = TerrainHeights.LevelToHeight(ramp.HighLevel), low = TerrainHeights.LevelToHeight(ramp.LowLevel);
        float h0 = ramp.HighAtMinX ? high : low, h1 = ramp.HighAtMinX ? low : high;
        float bottom = -GroundDepth;
        Vector3 Corner(float x, float y, float z) => new(x, y, z);

        // Top surface and the four sides; the bottom is never seen.
        var t00 = Corner(s.MinX, h0, s.MinY); var t01 = Corner(s.MinX, h0, s.MaxY);
        var t10 = Corner(s.MaxX, h1, s.MinY); var t11 = Corner(s.MaxX, h1, s.MaxY);
        var b00 = Corner(s.MinX, bottom, s.MinY); var b01 = Corner(s.MinX, bottom, s.MaxY);
        var b10 = Corner(s.MaxX, bottom, s.MinY); var b11 = Corner(s.MaxX, bottom, s.MaxY);

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            // Clockwise seen from outside (Godot's front faces).
            tool.AddVertex(a); tool.AddVertex(b); tool.AddVertex(c);
            tool.AddVertex(a); tool.AddVertex(c); tool.AddVertex(d);
        }
        Quad(t00, t10, t11, t01); // top
        Quad(b00, b10, t10, t00); // -Z side
        Quad(b11, b01, t01, t11); // +Z side
        Quad(b01, b00, t00, t01); // -X end
        Quad(b10, b11, t11, t10); // +X end
        tool.GenerateNormals();
        tool.SetMaterial(material);
        AddChild(new MeshInstance3D { Mesh = tool.Commit() });
    }

    void AddBlock(TileRect rect, float bottom, float top, Material material)
    {
        var center = rect.Center;
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(rect.Width, top - bottom, rect.Height), Material = material },
            Position = new Vector3(center.X.ToFloat(), (top + bottom) / 2f, center.Y.ToFloat()),
        });
    }

    void AddPlane(TileRect rect, float height, Material material) =>
        AddPlane(new Rect2(rect.MinX, rect.MinY, rect.Width, rect.Height), height, material);

    void AddPlane(Rect2 rect, float height, Material material)
    {
        var center = rect.GetCenter();
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = rect.Size, Material = material },
            Position = new Vector3(center.X, height, center.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>One line per tile edge across the rectangle, so players can see exactly where a building will go.</summary>
    void AddGrid(TileRect rect, float height, Material material)
    {
        var vertices = new Vector3[2 * (rect.Width + 1 + rect.Height + 1)];
        int v = 0;
        for (int x = rect.MinX; x <= rect.MaxX; x++)
        {
            vertices[v++] = new Vector3(x, height, rect.MinY);
            vertices[v++] = new Vector3(x, height, rect.MaxY);
        }
        for (int y = rect.MinY; y <= rect.MaxY; y++)
        {
            vertices[v++] = new Vector3(rect.MinX, height, y);
            vertices[v++] = new Vector3(rect.MaxX, height, y);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        mesh.SurfaceSetMaterial(0, material);
        AddChild(new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    static TileRect? Intersect(TileRect a, TileRect b)
    {
        var r = new TileRect(Mathf.Max(a.MinX, b.MinX), Mathf.Max(a.MinY, b.MinY), Mathf.Min(a.MaxX, b.MaxX), Mathf.Min(a.MaxY, b.MaxY));
        return r.IsEmpty ? null : r;
    }

    static StandardMaterial3D Lit(Color color) => new() { AlbedoColor = color, Roughness = 0.9f };

    static StandardMaterial3D Unshaded(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
