using System.Globalization;
using CastleFight.Game.UI;
using CastleFight.Sim;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// The end of a match: the fallen castle stays standing while it is torn down (a teardown animation goes here later),
/// then a round hole replaces it with a counter above it of how many units dropped in. Only reads the simulation.
/// </summary>
public partial class EndgameView : Node3D
{
    const float CounterHeight = 9f;
    const float CounterPop = 1.3f;
    const float CounterPopDecay = 6f;

    static readonly Color HoleColor = new(0.02f, 0.015f, 0.01f);
    static readonly Color RimColor = new(0.3f, 0.2f, 0.12f);

    MeshInstance3D _ruin = null!;
    Node3D _hole = null!;
    Label3D _counter = null!;
    int _shownDrops = -1;
    float _pop = 1f;

    public TerrainHeights Terrain { get; set; } = null!;

    public void Build()
    {
        _ruin = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = Vector3.One },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_ruin);

        _hole = new Node3D { Visible = false };
        AddChild(_hole);
        _hole.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 48, Rings = 1 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = HoleColor, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = new Vector3(1f, 0.04f, 1f),
        });
        _hole.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.2f, Rings = 48, RingSegments = 8 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = RimColor, Roughness = 1f },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Scale = new Vector3(1f, 0.6f, 1f),
        });

        _counter = new Label3D
        {
            Font = UiTheme.Shared.GetFont("font", "HeaderLabel"),
            FontSize = 96,
            PixelSize = 0.05f,
            OutlineSize = 24,
            Modulate = UiTheme.Gold,
            OutlineModulate = new Color(0.08f, 0.05f, 0.02f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Visible = false,
        };
        AddChild(_counter);
    }

    public void Draw(Simulation sim, double delta)
    {
        bool tearingDown = sim.IsGameOver && !sim.IsHoleOpen;
        _ruin.Visible = tearingDown;
        _hole.Visible = _counter.Visible = sim.IsHoleOpen;
        if (!sim.IsGameOver)
            return;

        float x = sim.HolePosition.X.ToFloat(), z = sim.HolePosition.Y.ToFloat();
        float ground = Terrain.At(x, z);
        if (tearingDown)
        {
            // Drawn like the castle in UnitRenderer, at no health. The teardown animation replaces this.
            var castle = sim.Catalog[sim.Catalog.CastleTypeId];
            float height = 1.8f * castle.Radius.ToFloat();
            float side = castle.FootprintTiles * 0.92f;
            _ruin.Scale = new Vector3(side, height, side);
            _ruin.Position = new Vector3(x, ground + height * 0.5f, z);
            if (_ruin.MaterialOverride is null)
            {
                var color = UnitRenderer.TeamColors[1 - sim.WinningTeam] * 0.6f * 0.45f;
                _ruin.MaterialOverride = new StandardMaterial3D { AlbedoColor = color with { A = 1f }, Roughness = 0.8f };
            }
            return;
        }

        float radius = sim.HoleRadius.ToFloat();
        _hole.Position = new Vector3(x, ground + 0.03f, z);
        _hole.Scale = new Vector3(radius, radius, radius);

        if (sim.HoleDrops != _shownDrops)
        {
            if (_shownDrops >= 0)
                _pop = CounterPop;
            _shownDrops = sim.HoleDrops;
            _counter.Text = _shownDrops.ToString("N0", CultureInfo.InvariantCulture);
        }
        _pop = Mathf.Lerp(_pop, 1f, 1f - Mathf.Exp(-CounterPopDecay * (float)delta));
        _counter.Position = new Vector3(x, ground + CounterHeight, z);
        _counter.Scale = Vector3.One * _pop;
    }
}
