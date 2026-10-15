using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// The menu look: weathered wood, iron and brass, Cinzel for headings and buttons, Alegreya for text.
/// Built once and set on the root window, so every Control (menus and the in-game HUD) inherits it.
/// Variations: <c>HeaderLabel</c>, <c>SubHeaderLabel</c>, <c>MutedLabel</c>, <c>BigButton</c>, <c>SmallButton</c>,
/// <c>InsetPanel</c>, <c>CardButton</c>.
/// </summary>
static class UiTheme
{
    public static readonly Color Parchment = new(0.93f, 0.86f, 0.72f);
    public static readonly Color Gold = new(0.96f, 0.76f, 0.38f);
    public static readonly Color Muted = new(0.68f, 0.6f, 0.5f);
    public static readonly Color Warning = new(1f, 0.56f, 0.42f);
    public static readonly Color Good = new(0.6f, 0.85f, 0.45f);

    /// <summary>Lighter team colours for text on dark wood (the unit colours are too dark for small text).</summary>
    public static readonly Color[] TeamText = [new(0.5f, 0.7f, 1f), new(1f, 0.5f, 0.42f)];
    public static readonly string[] TeamNames = ["Blue Army", "Red Army"];

    public static Font TitleFont { get; private set; } = null!;
    public static Font HeadingFont { get; private set; } = null!;
    public static Font BodyFont { get; private set; } = null!;
    public static Font BodyBoldFont { get; private set; } = null!;

    public static Theme Build()
    {
        TitleFont = GD.Load<FontFile>("res://assets/fonts/CinzelDecorative-Black.ttf");
        var cinzel = GD.Load<FontFile>("res://assets/fonts/Cinzel-Variable.ttf");
        var alegreya = GD.Load<FontFile>("res://assets/fonts/Alegreya-Variable.ttf");
        HeadingFont = Weight(cinzel, 700);
        BodyFont = Weight(alegreya, 500);
        BodyBoldFont = Weight(alegreya, 800);

        var theme = new Theme { DefaultFont = BodyFont, DefaultFontSize = 20 };

        // Textures
        var board = Box(UiArt.Board(), UiArt.BoardMargin, content: UiArt.BoardMargin + 6);
        var inset = Box(UiArt.Inset(), UiArt.InsetMargin, content: 10);
        var buttonNormal = Box(UiArt.Button(UiArt.ButtonState.Normal), UiArt.ButtonMargin, content: 14, vertical: 6);
        var buttonHover = Box(UiArt.Button(UiArt.ButtonState.Hover), UiArt.ButtonMargin, content: 14, vertical: 6);
        var buttonPressed = Box(UiArt.Button(UiArt.ButtonState.Pressed), UiArt.ButtonMargin, content: 14, vertical: 6);
        var buttonDisabled = Box(UiArt.Button(UiArt.ButtonState.Disabled), UiArt.ButtonMargin, content: 14, vertical: 6);
        var focus = new StyleBoxFlat
        {
            DrawCenter = false,
            BorderColor = Gold with { A = 0.75f },
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        };
        focus.SetBorderWidthAll(2);
        focus.SetExpandMarginAll(2);

        // Panels
        theme.SetStylebox("panel", "PanelContainer", board);
        theme.SetStylebox("panel", "Panel", board);
        theme.SetTypeVariation("InsetPanel", "PanelContainer");
        theme.SetStylebox("panel", "InsetPanel", inset);

        // Labels
        theme.SetColor("font_color", "Label", Parchment);
        theme.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.55f));
        theme.SetConstant("shadow_offset_x", "Label", 1);
        theme.SetConstant("shadow_offset_y", "Label", 2);
        theme.SetConstant("line_spacing", "Label", 1);

        theme.SetTypeVariation("HeaderLabel", "Label");
        theme.SetFont("font", "HeaderLabel", HeadingFont);
        theme.SetFontSize("font_size", "HeaderLabel", 30);
        theme.SetColor("font_color", "HeaderLabel", Gold);
        theme.SetColor("font_outline_color", "HeaderLabel", new Color(0.05f, 0.03f, 0.02f));
        theme.SetConstant("outline_size", "HeaderLabel", 6);

        theme.SetTypeVariation("SubHeaderLabel", "Label");
        theme.SetFont("font", "SubHeaderLabel", HeadingFont);
        theme.SetFontSize("font_size", "SubHeaderLabel", 19);
        theme.SetColor("font_color", "SubHeaderLabel", new Color(0.86f, 0.72f, 0.5f));
        theme.SetColor("font_outline_color", "SubHeaderLabel", new Color(0.05f, 0.03f, 0.02f));
        theme.SetConstant("outline_size", "SubHeaderLabel", 4);

        theme.SetTypeVariation("MutedLabel", "Label");
        theme.SetFontSize("font_size", "MutedLabel", 17);
        theme.SetColor("font_color", "MutedLabel", Muted);

        // Buttons
        foreach (string type in new[] { "Button", "OptionButton", "MenuButton" })
        {
            theme.SetStylebox("normal", type, buttonNormal);
            theme.SetStylebox("hover", type, buttonHover);
            theme.SetStylebox("pressed", type, buttonPressed);
            theme.SetStylebox("hover_pressed", type, buttonPressed);
            theme.SetStylebox("disabled", type, buttonDisabled);
            theme.SetStylebox("focus", type, focus);
            theme.SetFont("font", type, HeadingFont);
            theme.SetFontSize("font_size", type, 19);
            theme.SetColor("font_color", type, Parchment);
            theme.SetColor("font_hover_color", type, new Color(1f, 0.93f, 0.75f));
            theme.SetColor("font_pressed_color", type, Gold);
            theme.SetColor("font_hover_pressed_color", type, Gold);
            theme.SetColor("font_focus_color", type, Parchment);
            theme.SetColor("font_disabled_color", type, new Color(0.55f, 0.5f, 0.44f));
            theme.SetColor("font_outline_color", type, new Color(0.06f, 0.035f, 0.02f));
            theme.SetConstant("outline_size", type, 5);
        }

        theme.SetTypeVariation("BigButton", "Button");
        theme.SetFontSize("font_size", "BigButton", 27);

        theme.SetTypeVariation("SmallButton", "Button");
        theme.SetFontSize("font_size", "SmallButton", 15);
        foreach (var (state, box) in new[] { ("normal", buttonNormal), ("hover", buttonHover), ("pressed", buttonPressed), ("hover_pressed", buttonPressed), ("disabled", buttonDisabled) })
        {
            var small = (StyleBoxTexture)box.Duplicate();
            small.ContentMarginLeft = small.ContentMarginRight = 9;
            small.ContentMarginTop = small.ContentMarginBottom = 3;
            theme.SetStylebox(state, "SmallButton", small);
        }

        // Map cards: dark wells that light up on hover and get a gold rim when chosen.
        theme.SetTypeVariation("CardButton", "Button");
        var card = Box(UiArt.Inset(seed: 12), UiArt.InsetMargin, content: 8);
        var cardHover = (StyleBoxTexture)card.Duplicate();
        cardHover.ModulateColor = new Color(1.6f, 1.45f, 1.25f);
        var cardChosen = new StyleBoxFlat { BgColor = new Color(0.16f, 0.11f, 0.06f, 0.95f), BorderColor = Gold };
        cardChosen.SetBorderWidthAll(3);
        cardChosen.SetCornerRadiusAll(3);
        cardChosen.SetContentMarginAll(8);
        cardChosen.ShadowColor = Gold with { A = 0.35f };
        cardChosen.ShadowSize = 8;
        var cardDisabled = (StyleBoxTexture)card.Duplicate();
        cardDisabled.ModulateColor = new Color(0.6f, 0.6f, 0.6f);
        theme.SetStylebox("normal", "CardButton", card);
        theme.SetStylebox("hover", "CardButton", cardHover);
        theme.SetStylebox("pressed", "CardButton", cardChosen);
        theme.SetStylebox("hover_pressed", "CardButton", cardChosen);
        theme.SetStylebox("disabled", "CardButton", cardDisabled);
        theme.SetStylebox("focus", "CardButton", new StyleBoxEmpty());

        // Rows in server lists: clickable, highlighted when chosen.
        theme.SetTypeVariation("ListRow", "Button");
        var row = new StyleBoxFlat { BgColor = new Color(0.16f, 0.11f, 0.07f, 0.55f) };
        row.SetCornerRadiusAll(3);
        var rowHover = new StyleBoxFlat { BgColor = new Color(0.32f, 0.21f, 0.1f, 0.7f) };
        rowHover.SetCornerRadiusAll(3);
        var rowChosen = new StyleBoxFlat { BgColor = new Color(0.3f, 0.2f, 0.09f, 0.85f), BorderColor = Gold with { A = 0.8f } };
        rowChosen.SetBorderWidthAll(2);
        rowChosen.SetCornerRadiusAll(3);
        theme.SetStylebox("normal", "ListRow", row);
        theme.SetStylebox("hover", "ListRow", rowHover);
        theme.SetStylebox("pressed", "ListRow", rowChosen);
        theme.SetStylebox("hover_pressed", "ListRow", rowChosen);
        theme.SetStylebox("focus", "ListRow", new StyleBoxEmpty());

        // Check boxes
        foreach (string type in new[] { "CheckBox", "CheckButton" })
        {
            foreach (string style in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
                theme.SetStylebox(style, type, new StyleBoxEmpty { ContentMarginLeft = 2, ContentMarginRight = 2 });
            theme.SetFont("font", type, BodyFont);
            theme.SetFontSize("font_size", type, 20);
            theme.SetColor("font_color", type, Parchment);
            theme.SetColor("font_hover_color", type, new Color(1f, 0.95f, 0.82f));
            theme.SetColor("font_pressed_color", type, Parchment);
            theme.SetColor("font_hover_pressed_color", type, new Color(1f, 0.95f, 0.82f));
            theme.SetColor("font_disabled_color", type, Muted);
            theme.SetConstant("h_separation", type, 10);
        }
        var unchecked_ = UiArt.CheckBox(false);
        var checked_ = UiArt.CheckBox(true);
        theme.SetIcon("unchecked", "CheckBox", unchecked_);
        theme.SetIcon("checked", "CheckBox", checked_);
        theme.SetIcon("unchecked_disabled", "CheckBox", UiArt.CheckBox(false, disabled: true));
        theme.SetIcon("checked_disabled", "CheckBox", UiArt.CheckBox(true, disabled: true));

        // Text fields
        foreach (string type in new[] { "LineEdit", "TextEdit" })
        {
            theme.SetStylebox("normal", type, inset);
            theme.SetStylebox("read_only", type, inset);
            theme.SetStylebox("focus", type, focus);
            theme.SetFont("font", type, BodyFont);
            theme.SetFontSize("font_size", type, 20);
            theme.SetColor("font_color", type, Parchment);
            theme.SetColor("font_placeholder_color", type, Muted with { A = 0.7f });
            theme.SetColor("caret_color", type, Gold);
            theme.SetColor("selection_color", type, new Color(0.6f, 0.4f, 0.15f, 0.6f));
            theme.SetColor("font_uneditable_color", type, Muted);
        }

        // Rich text (chat)
        theme.SetFont("normal_font", "RichTextLabel", BodyFont);
        theme.SetFont("bold_font", "RichTextLabel", BodyBoldFont);
        var italic = Weight(alegreya, 500);
        italic.VariationTransform = new Transform2D(new Vector2(1, 0), new Vector2(0.2f, 1), Vector2.Zero); // slanted
        theme.SetFont("italics_font", "RichTextLabel", italic);
        theme.SetFontSize("normal_font_size", "RichTextLabel", 19);
        theme.SetFontSize("bold_font_size", "RichTextLabel", 19);
        theme.SetFontSize("italics_font_size", "RichTextLabel", 18);
        theme.SetColor("default_color", "RichTextLabel", Parchment);
        theme.SetConstant("line_separation", "RichTextLabel", 3);

        // Pop-up menus (option buttons) and tooltips
        var popup = new StyleBoxFlat { BgColor = new Color(0.1f, 0.07f, 0.05f, 0.98f), BorderColor = new Color(0.45f, 0.33f, 0.18f) };
        popup.SetBorderWidthAll(2);
        popup.SetContentMarginAll(6);
        var popupHover = new StyleBoxFlat { BgColor = new Color(0.5f, 0.33f, 0.14f, 0.85f) };
        theme.SetStylebox("panel", "PopupMenu", popup);
        theme.SetStylebox("hover", "PopupMenu", popupHover);
        theme.SetFont("font", "PopupMenu", BodyFont);
        theme.SetFontSize("font_size", "PopupMenu", 19);
        theme.SetColor("font_color", "PopupMenu", Parchment);
        theme.SetColor("font_hover_color", "PopupMenu", new Color(1f, 0.95f, 0.8f));
        theme.SetConstant("v_separation", "PopupMenu", 8);
        theme.SetStylebox("panel", "TooltipPanel", popup);
        theme.SetColor("font_color", "TooltipLabel", Parchment);
        theme.SetFontSize("font_size", "TooltipLabel", 17);

        // Scroll bars: a thin iron groove with a wooden grip.
        var groove = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.7f) };
        groove.SetCornerRadiusAll(4);
        groove.ContentMarginLeft = groove.ContentMarginRight = 5;
        var grip = new StyleBoxFlat { BgColor = new Color(0.42f, 0.29f, 0.16f), BorderColor = new Color(0.08f, 0.05f, 0.03f) };
        grip.SetBorderWidthAll(1);
        grip.SetCornerRadiusAll(4);
        var gripHover = (StyleBoxFlat)grip.Duplicate();
        gripHover.BgColor = new Color(0.6f, 0.42f, 0.2f);
        theme.SetStylebox("scroll", "VScrollBar", groove);
        theme.SetStylebox("grabber", "VScrollBar", grip);
        theme.SetStylebox("grabber_highlight", "VScrollBar", gripHover);
        theme.SetStylebox("grabber_pressed", "VScrollBar", gripHover);

        // Separators
        var line = new StyleBoxLine { Color = new Color(0.55f, 0.4f, 0.2f, 0.5f), Thickness = 2 };
        theme.SetStylebox("separator", "HSeparator", line);
        theme.SetConstant("separation", "HSeparator", 12);

        return theme;
    }

    static FontVariation Weight(FontFile font, int weight)
    {
        var variation = new FontVariation { BaseFont = font };
        variation.VariationOpentype = new Godot.Collections.Dictionary { { TextServerManager.GetPrimaryInterface().NameToTag("wght"), weight } };
        return variation;
    }

    static StyleBoxTexture Box(Texture2D texture, int margin, int content, int? vertical = null)
    {
        var box = new StyleBoxTexture { Texture = texture };
        box.SetTextureMarginAll(margin);
        box.ContentMarginLeft = box.ContentMarginRight = content;
        box.ContentMarginTop = box.ContentMarginBottom = vertical ?? content;
        return box;
    }
}
