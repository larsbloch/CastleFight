using System.Collections.Generic;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Draws every player's builder as a spinning star floating over the ground, a green ring under the local builder
/// while it is selected, and the construction sites: a box that rises while a builder stands on it, plus a faint
/// marker where the local builder is walking to build. Only reads the simulation.
/// </summary>
public partial class BuilderView : Node3D
{
    const float StarRadius = 1.5f;
    const float StarInnerRadius = 0.6f;
    const float StarThickness = 0.4f;
    const float HoverHeight = 2.2f;
    const float SpinSpeed = 2.2f;
    const float SiteHeight = 1.7f;

    static readonly Color SelectionColor = new(0.3f, 1f, 0.35f);
    static readonly Color PendingSiteColor = new(0.3f, 1f, 0.35f, 0.25f);

    readonly List<MeshInstance3D> _stars = new();
    readonly List<MeshInstance3D> _sites = new();
    MeshInstance3D _ring = null!;
    MeshInstance3D _pendingSite = null!;
    float _spin;

    public TerrainHeights Terrain { get; set; } = null!;

    public void Build(Simulation sim)
    {
        var starMesh = StarMesh();
        foreach (var player in sim.Players)
        {
            var color = UnitRenderer.TeamColors[player.Team].Lightened(0.35f);
            var star = new MeshInstance3D
            {
                Mesh = starMesh,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = color, Roughness = 0.4f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    EmissionEnabled = true, Emission = color, EmissionEnergyMultiplier = 0.35f,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(star);
            _stars.Add(star);

            var site = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = Vector3.One },
                MaterialOverride = Translucent(UnitRenderer.TeamColors[player.Team] with { A = 0.55f }),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(site);
            _sites.Add(site);
        }

        _ring = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 1.15f, OuterRadius = 1.35f, Rings = 32, RingSegments = 6 },
            MaterialOverride = Translucent(SelectionColor),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = new Vector3(1f, 0.15f, 1f),
            Visible = false,
        };
        AddChild(_ring);
        _pendingSite = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One },
            MaterialOverride = Translucent(PendingSiteColor),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_pendingSite);
    }

    /// <summary>Where the builder is drawn (the middle of the star), interpolated between ticks.</summary>
    public Vector3 WorldPosition(Builder builder, float alpha)
    {
        float x = Mathf.Lerp(builder.PrevPos.X.ToFloat(), builder.Pos.X.ToFloat(), alpha);
        float z = Mathf.Lerp(builder.PrevPos.Y.ToFloat(), builder.Pos.Y.ToFloat(), alpha);
        return new Vector3(x, Terrain.At(x, z) + HoverHeight, z);
    }

    public void Draw(Simulation sim, float alpha, int localPlayerId, bool localSelected, double delta)
    {
        _spin = Mathf.Wrap(_spin + SpinSpeed * (float)delta, 0f, Mathf.Tau);
        for (int i = 0; i < sim.Players.Count; i++)
        {
            var builder = sim.Players[i].Builder;
            var at = WorldPosition(builder, alpha);
            // Spin faster while building, so it is clear that it is working.
            float spin = builder.Order == BuilderOrder.Construct ? _spin * 3f : _spin;
            _stars[i].Transform = new Transform3D(new Basis(Vector3.Up, spin + i), at);
            DrawSite(_sites[i], sim, builder);
        }

        var local = sim.Players[localPlayerId].Builder;
        _ring.Visible = localSelected;
        if (localSelected)
        {
            var at = WorldPosition(local, alpha);
            _ring.Position = new Vector3(at.X, at.Y - HoverHeight + 0.08f, at.Z);
        }

        _pendingSite.Visible = local.Order == BuilderOrder.GoToSite;
        if (_pendingSite.Visible)
            PlaceBox(_pendingSite, sim.SiteOf(local), 0.15f);
    }

    void DrawSite(MeshInstance3D site, Simulation sim, Builder builder)
    {
        site.Visible = builder.Order == BuilderOrder.Construct;
        if (!site.Visible)
            return;
        float progress = builder.BuildProgress / (float)Simulation.BuildTicks;
        PlaceBox(site, sim.SiteOf(builder), Mathf.Max(0.1f, progress * SiteHeight));
    }

    void PlaceBox(MeshInstance3D box, TileRect tiles, float height)
    {
        float x = (tiles.MinX + tiles.MaxX) * 0.5f, z = (tiles.MinY + tiles.MaxY) * 0.5f;
        box.Scale = new Vector3(tiles.Width * 0.92f, height, tiles.Height * 0.92f);
        box.Position = new Vector3(x, Terrain.At(x, z) + height * 0.5f, z);
    }

    static StandardMaterial3D Translucent(Color color) => new()
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    /// <summary>A five-pointed star standing upright (in the XY plane), with a ridge on both faces.</summary>
    static ArrayMesh StarMesh()
    {
        var outline = new Vector3[10];
        for (int i = 0; i < outline.Length; i++)
        {
            float angle = i * Mathf.Tau / outline.Length + Mathf.Pi / 2;
            float radius = i % 2 == 0 ? StarRadius : StarInnerRadius;
            outline[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }

        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        foreach (float side in new[] { 1f, -1f })
        {
            var apex = new Vector3(0f, 0f, side * StarThickness);
            for (int i = 0; i < outline.Length; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Length];
                var normal = (b - a).Cross(apex - a).Normalized();
                if (normal.Dot((a + b + apex) / 3f) < 0f)
                    normal = -normal; // face outwards; culling is off, so the winding does not matter
                foreach (var vertex in new[] { apex, a, b })
                {
                    tool.SetNormal(normal);
                    tool.AddVertex(vertex);
                }
            }
        }
        return tool.Commit();
    }
}
