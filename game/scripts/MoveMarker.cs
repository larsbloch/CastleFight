using Godot;

namespace CastleFight.Game;

/// <summary>
/// Shows where the builder was sent: green circles on the ground that close in on the point for two seconds, then
/// fade out. Frees itself when it is done.
/// </summary>
public partial class MoveMarker : MeshInstance3D
{
    const float FlowSeconds = 2f;
    const float FadeSeconds = 0.4f;
    const int RingCount = 3;
    const float RingSeconds = 1f; // how long one circle takes from the edge to the middle
    const float OuterRadius = 1.6f;
    const float RingWidth = 0.12f;
    const float DotRadius = 0.18f;
    const float Lift = 0.08f;
    const int Segments = 40;

    static readonly Color Green = new(0.3f, 1f, 0.35f);

    readonly ImmediateMesh _mesh = new();
    float _age;

    public TerrainHeights Terrain { get; set; } = null!;

    /// <summary>The point on the ground (x, z).</summary>
    public Vector2 Center { get; set; }

    public override void _Ready()
    {
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;
        MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Redraw();
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= FlowSeconds + FadeSeconds)
            QueueFree();
        else
            Redraw();
    }

    void Redraw()
    {
        float fade = 1f - Mathf.Clamp((_age - FlowSeconds) / FadeSeconds, 0f, 1f);
        _mesh.ClearSurfaces();
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < RingCount; i++)
        {
            // The circles follow each other inwards; each fades in at the edge and out near the middle.
            float phase = Mathf.PosMod(_age / RingSeconds + i / (float)RingCount, 1f);
            float alpha = Mathf.Min(phase * 6f, 1f) * Mathf.Min((1f - phase) * 3f, 1f) * fade;
            float radius = OuterRadius * (1f - phase) + DotRadius * phase;
            AddRing(radius - RingWidth * 0.5f, radius + RingWidth * 0.5f, Green with { A = alpha });
        }
        AddRing(0f, DotRadius, Green with { A = 0.9f * fade });
        _mesh.SurfaceEnd();
    }

    void AddRing(float inner, float outer, Color color)
    {
        _mesh.SurfaceSetColor(color);
        for (int s = 0; s < Segments; s++)
        {
            float a0 = s * Mathf.Tau / Segments, a1 = (s + 1) * Mathf.Tau / Segments;
            var inner0 = Point(a0, inner);
            var inner1 = Point(a1, inner);
            var outer0 = Point(a0, outer);
            var outer1 = Point(a1, outer);
            _mesh.SurfaceAddVertex(inner0);
            _mesh.SurfaceAddVertex(outer0);
            _mesh.SurfaceAddVertex(outer1);
            _mesh.SurfaceAddVertex(inner0);
            _mesh.SurfaceAddVertex(outer1);
            _mesh.SurfaceAddVertex(inner1);
        }
    }

    Vector3 Point(float angle, float radius)
    {
        float x = Center.X + Mathf.Cos(angle) * radius, z = Center.Y + Mathf.Sin(angle) * radius;
        return new Vector3(x, Terrain.At(x, z) + Lift, z);
    }
}
