using CastleFight.Sim;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Static map visuals: ground, lane roads, walls, team build zones with a tile grid, plus light and
/// environment. Built once per match from the <see cref="MapDefinition"/>. Sim (x, y) is world (x, 0, y).
/// </summary>
public partial class MapScenery : Node3D
{
    const float WallHeight = 2.5f;

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

        AddPlane(new TileRect(0, 0, map.LengthTiles, map.WidthTiles), 0f, Lit(new Color(0.27f, 0.34f, 0.22f)));

        var road = Lit(new Color(0.42f, 0.38f, 0.28f));
        foreach (var lane in map.Lanes)
            AddPlane(lane.Corridor, 0.01f, road);

        var stone = Lit(new Color(0.36f, 0.35f, 0.38f));
        foreach (var wall in map.Walls)
        {
            var center = wall.Center;
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(wall.Width, WallHeight, wall.Height), Material = stone },
                Position = new Vector3(center.X.ToFloat(), WallHeight / 2f, center.Y.ToFloat()),
            });
        }

        for (int team = 0; team < Simulation.TeamCount; team++)
        {
            var zone = map.BuildZone(team);
            AddPlane(zone, 0.02f, Unshaded(UnitRenderer.TeamColors[team] with { A = 0.15f }));
            AddGrid(zone, 0.03f, Unshaded(new Color(1f, 1f, 1f, 0.22f)));
        }
    }

    void AddPlane(TileRect rect, float height, Material material)
    {
        var center = rect.Center;
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(rect.Width, rect.Height), Material = material },
            Position = new Vector3(center.X.ToFloat(), height, center.Y.ToFloat()),
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

    static StandardMaterial3D Lit(Color color) => new() { AlbedoColor = color, Roughness = 0.9f };

    static StandardMaterial3D Unshaded(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
}
