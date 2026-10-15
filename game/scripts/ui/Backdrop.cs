using Godot;

namespace CastleFight.Game.UI;

/// <summary>Animated full-screen menu background (shaders/menu_backdrop.gdshader).</summary>
public partial class Backdrop : ColorRect
{
    ShaderMaterial _material = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/menu_backdrop.gdshader") };
        Material = _material;
        Resized += UpdateAspect;
        UpdateAspect();
    }

    void UpdateAspect() => _material.SetShaderParameter("aspect", Size.Y > 0 ? Size.X / Size.Y : 16f / 9f);
}
