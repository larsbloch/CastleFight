using CastleFight.Sim;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Translucent preview of the selected building, snapped to the tile grid under the mouse. Green when it
/// can be placed, amber when only the gold is missing, red when the spot is blocked or outside the zone.
/// </summary>
public partial class PlacementGhost : MeshInstance3D
{
    const float Height = 0.6f;

    static readonly Color Valid = new(0.25f, 1f, 0.35f, 0.45f);
    static readonly Color NoGold = new(1f, 0.75f, 0.2f, 0.45f);
    static readonly Color Blocked = new(1f, 0.2f, 0.2f, 0.45f);

    readonly StandardMaterial3D _material = new()
    {
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    public override void _Ready()
    {
        Mesh = new BoxMesh { Size = Vector3.One, Material = _material };
        CastShadow = ShadowCastingSetting.Off;
        Visible = false;
    }

    /// <summary>Lowest tile of a <paramref name="footprintTiles"/>-wide footprint centred as close as possible to <paramref name="ground"/>.</summary>
    public static (int X, int Y) Snap(Vector3 ground, int footprintTiles) =>
        (Mathf.FloorToInt(ground.X - footprintTiles * 0.5f + 0.5f), Mathf.FloorToInt(ground.Z - footprintTiles * 0.5f + 0.5f));

    /// <param name="typeId">Selected building type, or -1 to hide the ghost.</param>
    public void UpdatePreview(Simulation sim, int playerId, int typeId, Vector3? ground)
    {
        if (typeId < 0 || ground is not { } point || sim.IsGameOver)
        {
            Visible = false;
            return;
        }

        int size = sim.Catalog[typeId].FootprintTiles;
        var (tileX, tileY) = Snap(point, size);
        var result = sim.CheckPlacement(playerId, typeId, tileX, tileY);
        _material.AlbedoColor = result switch
        {
            PlacementResult.Ok => Valid,
            PlacementResult.NotEnoughGold => NoGold,
            _ => Blocked,
        };

        Scale = new Vector3(size * 0.98f, Height, size * 0.98f);
        Position = new Vector3(tileX + size * 0.5f, Height * 0.5f, tileY + size * 0.5f);
        Visible = true;
    }
}
