using Godot;

namespace CastleFight.Game.UI;

/// <summary>"CASTLE FIGHT" in worn gold lettering (shaders/gold_text.gdshader) over an ornamental rule.</summary>
public partial class TitleBanner : VBoxContainer
{
    public void Build(int fontSize)
    {
        AddThemeConstantOverride("separation", 0);
        MouseFilter = MouseFilterEnum.Ignore;
        var font = new FontVariation { BaseFont = UiTheme.TitleFont, SpacingGlyph = fontSize / 24 };
        var label = new Label { Text = "CASTLE FIGHT", HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", new Color(0.07f, 0.035f, 0.015f));
        label.AddThemeConstantOverride("outline_size", Mathf.Max(4, fontSize / 9));
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.75f));
        label.AddThemeConstantOverride("shadow_offset_x", 0);
        label.AddThemeConstantOverride("shadow_offset_y", Mathf.Max(3, fontSize / 18));
        label.AddThemeConstantOverride("shadow_outline_size", Mathf.Max(6, fontSize / 6));

        // The gradient runs over the capitals: from cap height down to the baseline.
        float baseline = font.GetAscent(fontSize);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/gold_text.gdshader") };
        material.SetShaderParameter("text_top", baseline - fontSize * 0.7f);
        material.SetShaderParameter("text_bottom", baseline);
        label.Material = material;
        AddChild(label);

        AddChild(new Ornament { CustomMinimumSize = new Vector2(fontSize * 7.5f, 18), SizeFlagsHorizontal = SizeFlags.ShrinkCenter });
    }
}
