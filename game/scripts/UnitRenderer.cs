using System;
using System.Collections.Generic;
using CastleFight.Sim;
using CastleFight.Sim.FixedMath;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Draws every unit with one MultiMesh per visual style, i.e. one draw call per style no matter
/// how many units exist. Only reads the simulation; never changes it.
/// Simulation (x, y) maps to world (x, ground height, y).
/// </summary>
public partial class UnitRenderer : Node3D
{
    const int FloatsPerInstance = 16; // 12 for the Transform3D + 4 for the colour
    const int MinCapacity = 256;

    public static readonly Color[] TeamColors = [new(0.25f, 0.5f, 1f), new(1f, 0.32f, 0.25f)];

    sealed class Style
    {
        public required MultiMesh Multimesh { get; init; }

        /// <summary>World size in multiples of the unit radius. Meshes are 1x1x1, centred on the origin.</summary>
        public required Vector3 SizePerRadius { get; init; }

        public float[] Buffer = [];
        public int Count;
    }

    readonly List<Style> _styles = new();
    Style[] _styleOfType = [];
    float[] _brightnessOfType = [];
    TerrainHeights _terrain = null!;

    public void Build(Simulation sim, TerrainHeights terrain)
    {
        _terrain = terrain;
        if (_styles.Count > 0)
            return;

        var catalog = sim.Catalog;
        var bounds = new Aabb(new Vector3(-10f, -1f, -10f), new Vector3(sim.Map.Length.ToFloat() + 20f, 30f, sim.Map.Width.ToFloat() + 20f));

        var castle = AddStyle(new BoxMesh { Size = Vector3.One }, new Vector3(2f, 1.8f, 2f), bounds);
        var building = AddStyle(new BoxMesh { Size = Vector3.One }, new Vector3(1.8f, 1.4f, 1.8f), bounds);
        var footman = AddStyle(new SphereMesh { Radius = 0.5f, Height = 1f }, new Vector3(2f, 3f, 2f), bounds);
        var archer = AddStyle(new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.5f, Height = 1f }, new Vector3(2f, 3.4f, 2f), bounds);
        var skirmisher = AddStyle(new BoxMesh { Size = Vector3.One }, new Vector3(3f, 1.6f, 1.8f), bounds);
        var hunter = AddStyle(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.5f, Height = 1f }, new Vector3(2.2f, 3.4f, 2.2f), bounds);

        _styleOfType = new Style[catalog.Count];
        _brightnessOfType = new float[catalog.Count];
        for (int id = 0; id < catalog.Count; id++)
        {
            var type = catalog[id];
            _styleOfType[id] = type.IsCastle ? castle
                : type.IsStructure ? building
                : id == UnitCatalog.Archer ? archer
                : id == UnitCatalog.Skirmisher ? skirmisher
                : id == UnitCatalog.Hunter ? hunter
                : footman;
            _brightnessOfType[id] = type.IsStructure ? 0.6f : 1f;
        }
    }

    Style AddStyle(PrimitiveMesh mesh, Vector3 sizePerRadius, Aabb bounds)
    {
        mesh.Material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.8f };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            CustomAabb = bounds, // skip recomputing bounds from thousands of instances every frame
        };
        AddChild(new MultiMeshInstance3D { Multimesh = multimesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        var style = new Style { Multimesh = multimesh, SizePerRadius = sizePerRadius };
        _styles.Add(style);
        return style;
    }

    /// <param name="alpha">How far we are between the previous and the current tick (0..1).</param>
    public void Draw(Simulation sim, float alpha)
    {
        var u = sim.Units;
        var alive = u.Alive;
        var types = u.Type;

        foreach (var style in _styles)
            style.Count = 0;
        for (int i = 0; i < u.HighWater; i++)
        {
            if (alive[i])
                _styleOfType[types[i]].Count++;
        }
        foreach (var style in _styles)
        {
            EnsureCapacity(style, style.Count);
            style.Count = 0;
        }

        for (int i = 0; i < u.HighWater; i++)
        {
            if (alive[i])
                WriteInstance(sim, i, alpha);
        }

        foreach (var style in _styles)
        {
            style.Multimesh.Buffer = style.Buffer;
            style.Multimesh.VisibleInstanceCount = style.Count;
        }
    }

    void WriteInstance(Simulation sim, int i, float alpha)
    {
        var u = sim.Units;
        int typeId = u.Type[i];
        var type = sim.Catalog[typeId];
        var style = _styleOfType[typeId];

        FixVec2 previous = u.PrevPos[i];
        FixVec2 current = u.Pos[i];
        float x = Mathf.Lerp(previous.X.ToFloat(), current.X.ToFloat(), alpha);
        float z = Mathf.Lerp(previous.Y.ToFloat(), current.Y.ToFloat(), alpha);

        // Face the target when fighting, otherwise the direction of travel, otherwise down the lane.
        FixVec2 facing = type.IsStructure ? sim.Map.Forward(u.Team[i])
            : u.HasValidTarget(i) ? u.Pos[u.Target[i]] - current
            : !u.Vel[i].IsZero ? u.Vel[i]
            : sim.Map.Forward(u.Team[i]);
        float fx = facing.X.ToFloat(), fz = facing.Y.ToFloat();
        float length = Mathf.Sqrt(fx * fx + fz * fz);
        if (length < 1e-5f)
            (fx, fz, length) = (1f, 0f, 1f);
        fx /= length;
        fz /= length;

        float radius = u.Radius[i].ToFloat();
        float sy = style.SizePerRadius.Y * radius;
        // Structures fill their tile footprint (with a small gap so neighbours stay distinguishable).
        float sx = type.IsStructure ? type.FootprintTiles * 0.92f : style.SizePerRadius.X * radius;
        float sz = type.IsStructure ? type.FootprintTiles * 0.92f : style.SizePerRadius.Z * radius;

        float health = Mathf.Clamp(u.Hp[i] / (float)type.MaxHp, 0f, 1f);
        Color color = TeamColors[u.Team[i]] * _brightnessOfType[typeId] * (0.45f + 0.55f * health);

        // Rows of the basis (rotation around Y so local +X points along `facing`, then scale), then origin.
        var b = style.Buffer;
        int o = style.Count++ * FloatsPerInstance;
        b[o + 0] = fx * sx; b[o + 1] = 0f; b[o + 2] = -fz * sz; b[o + 3] = x;
        b[o + 4] = 0f; b[o + 5] = sy; b[o + 6] = 0f; b[o + 7] = _terrain.At(x, z) + sy * 0.5f;
        b[o + 8] = fz * sx; b[o + 9] = 0f; b[o + 10] = fx * sz; b[o + 11] = z;
        b[o + 12] = color.R; b[o + 13] = color.G; b[o + 14] = color.B; b[o + 15] = 1f;
    }

    static void EnsureCapacity(Style style, int needed)
    {
        int capacity = style.Multimesh.InstanceCount;
        bool tooSmall = needed > capacity;
        bool wasteful = capacity > MinCapacity && needed * 4 < capacity; // keep per-frame uploads small after big fights
        if (!tooSmall && !wasteful)
            return;

        capacity = Math.Max(MinCapacity, needed * 2);
        style.Multimesh.InstanceCount = capacity;
        style.Buffer = new float[capacity * FloatsPerInstance];
    }
}
