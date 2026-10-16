using System;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// Procedural textures for the menu theme: wood boards with an iron frame, wooden buttons, dark insets and
/// check boxes. Painted once at startup into byte arrays (no per-pixel engine calls), so there are no image
/// assets to maintain and every colour can be tuned here.
/// </summary>
static partial class UiArt
{
    public enum ButtonState { Normal, Hover, Pressed, Disabled }

    // ---- Boards ----

    /// <summary>Margin of the iron frame (and its corner brackets) in board textures.</summary>
    public const int BoardMargin = 30;

    /// <summary>A planked wood board with an iron frame, corner brackets and rivets. Stretch it as a 9-patch.</summary>
    public static ImageTexture Board(int width = 768, int height = 768, int seed = 1, float plankHeight = 46, float brightness = 1f)
    {
        var pixels = new Canvas(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var wood = Wood(x, y, plankHeight, seed) * brightness;
                int d = Math.Min(Math.Min(x, y), Math.Min(width - 1 - x, height - 1 - y));
                int cx = Math.Min(x, width - 1 - x), cy = Math.Min(y, height - 1 - y);
                bool bracket = cx < BoardMargin && cy < BoardMargin; // corner plate
                pixels.Set(x, y, bracket ? CornerBracket(cx, cy, x, y, seed, wood) : Frame(d, x, y, seed, wood));
            }
        }
        return pixels.ToTexture();
    }

    static Color Frame(int d, int x, int y, int seed, Color wood)
    {
        if (d < 2)
            return new Color(0.03f, 0.02f, 0.015f);
        if (d < 13)
            return Iron(x, y, seed, 1f + 0.35f * (1f - Math.Abs(d - 6.5f) / 5.5f) - 0.12f);
        if (d < 15)
            return new Color(0.04f, 0.03f, 0.02f);
        // Inner shadow cast by the frame onto the wood.
        float shade = 0.55f + 0.45f * SmoothStep(15, 28, d);
        return wood * shade;
    }

    static Color CornerBracket(int cx, int cy, int x, int y, int seed, Color wood)
    {
        int edge = Math.Min(cx, cy);
        int outer = Math.Max(cx, cy);
        if (edge < 2 || outer >= BoardMargin - 1)
            return outer >= BoardMargin - 1 && edge >= 2 ? wood * 0.45f : new Color(0.03f, 0.02f, 0.015f);
        if (outer >= BoardMargin - 3)
            return new Color(0.05f, 0.04f, 0.03f);

        // Bevelled plate: lighter towards the outer corner.
        float bevel = 1.15f - 0.35f * (outer / (float)BoardMargin);
        var plate = Iron(x, y, seed + 5, bevel);

        // Rivet near the corner.
        float rx = cx - 11.5f, ry = cy - 11.5f;
        float r = MathF.Sqrt(rx * rx + ry * ry);
        if (r < 5f)
        {
            float light = 0.8f + 0.9f * Math.Clamp(-(rx + ry) / 7f, -0.5f, 1f); // lit from the top left
            return new Color(0.42f, 0.38f, 0.33f) * light;
        }
        if (r < 6.2f)
            return plate * 0.45f;
        return plate;
    }

    // ---- Buttons ----

    public const int ButtonMargin = 8;

    /// <summary>A single wooden plank with a dark rim and bevel, tinted for the button state.</summary>
    public static ImageTexture Button(ButtonState state, int width = 320, int height = 72, int seed = 7)
    {
        var pixels = new Canvas(width, height);
        bool pressed = state == ButtonState.Pressed;
        Color rim = state == ButtonState.Hover ? new Color(0.86f, 0.64f, 0.28f) : new Color(0.06f, 0.04f, 0.03f);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int d = Math.Min(Math.Min(x, y), Math.Min(width - 1 - x, height - 1 - y));
                Color c;
                if (d < 2)
                {
                    c = d == 0 ? new Color(0.02f, 0.015f, 0.01f) : rim;
                }
                else
                {
                    c = Wood(x + 40, y + 13, 200, seed) * 1.25f;
                    // Bevel: top/left catch the light, bottom/right fall into shadow (inverted when pressed).
                    bool topLeft = x < 5 || y < 5;
                    bool bottomRight = width - 1 - x < 5 || height - 1 - y < 5;
                    if (d < 5 && topLeft && !bottomRight)
                        c *= pressed ? 0.6f : 1.35f;
                    else if (d < 5 && bottomRight)
                        c *= pressed ? 1.2f : 0.55f;
                    // A faint metal band near both ends, like a reinforced plank.
                    int end = Math.Min(x, width - 1 - x);
                    if (end is >= 10 and < 14)
                        c = c.Lerp(new Color(0.16f, 0.15f, 0.14f), 0.7f);
                    else if (end == 14)
                        c *= 0.6f;
                }
                c = state switch
                {
                    ButtonState.Hover => (c * 1.22f).Lerp(new Color(0.75f, 0.5f, 0.2f), 0.08f),
                    ButtonState.Pressed => c * 0.72f,
                    ButtonState.Disabled => Desaturate(c, 0.7f) * 0.55f,
                    _ => c,
                };
                pixels.Set(x, y, c);
            }
        }
        return pixels.ToTexture();
    }

    // ---- Insets ----

    public const int InsetMargin = 8;

    /// <summary>A dark recessed well for text fields, lists and the chat log.</summary>
    public static ImageTexture Inset(int width = 192, int height = 192, int seed = 3, float alpha = 0.92f)
    {
        var pixels = new Canvas(width, height, alpha);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float grit = 0.85f + 0.3f * Fbm(x * 0.08f, y * 0.08f, seed, 3) + 0.12f * (Hash(x, y, seed) - 0.5f);
                var c = new Color(0.075f, 0.055f, 0.04f) * grit;
                int fromTop = y, fromLeft = x, fromBottom = height - 1 - y, fromRight = width - 1 - x;
                int d = Math.Min(Math.Min(fromTop, fromLeft), Math.Min(fromBottom, fromRight));
                if (d == 0)
                    c = fromBottom == 0 || fromRight == 0 ? new Color(0.42f, 0.31f, 0.19f) : new Color(0.01f, 0.01f, 0.005f);
                else
                    c *= 0.45f + 0.55f * SmoothStep(0, 7, Math.Min(fromTop, fromLeft)); // inner shadow from the top left
                pixels.Set(x, y, c);
            }
        }
        return pixels.ToTexture();
    }

    // ---- Check boxes ----

    public static ImageTexture CheckBox(bool checkedState, bool disabled = false, int size = 26)
    {
        var pixels = new Canvas(size, size);
        var gold = disabled ? new Color(0.45f, 0.4f, 0.32f) : new Color(0.95f, 0.75f, 0.35f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int d = Math.Min(Math.Min(x, y), Math.Min(size - 1 - x, size - 1 - y));
                Color c = d switch
                {
                    0 => new Color(0.02f, 0.015f, 0.01f),
                    1 or 2 => new Color(0.48f, 0.36f, 0.2f) * (disabled ? 0.6f : 1f),
                    _ => new Color(0.06f, 0.045f, 0.035f) * (0.8f + 0.4f * Hash(x, y, 9)),
                };
                if (checkedState && d >= 3 && DistanceToCheck(x + 0.5f, y + 0.5f, size) < size * 0.085f)
                    c = gold * (1.1f - 0.4f * (y / (float)size));
                pixels.Set(x, y, c);
            }
        }
        return pixels.ToTexture();
    }

    static float DistanceToCheck(float x, float y, int size)
    {
        var a = new Vector2(size * 0.24f, size * 0.52f);
        var b = new Vector2(size * 0.43f, size * 0.72f);
        var c = new Vector2(size * 0.78f, size * 0.28f);
        var p = new Vector2(x, y);
        return Math.Min(DistanceToSegment(p, a, b), DistanceToSegment(p, b, c));
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    // ---- Materials ----

    /// <summary>Weathered wood: planks of <paramref name="plankHeight"/> pixels with grain running along X.</summary>
    static Color Wood(int x, int y, float plankHeight, int seed)
    {
        int plank = (int)(y / plankHeight);
        float py = y - plank * plankHeight;
        int s = seed * 7919 + plank * 31;
        float shift = Hash(plank, 0, seed) * 1000f;
        float fx = x + shift;

        float warp = Fbm(fx * 0.012f, py * 0.09f, s, 3);
        float rings = MathF.Sin(py * 0.5f + warp * 16f + Fbm(fx * 0.004f, plank * 3.1f, s + 7, 2) * 18f);
        float fibre = Fbm(fx * 0.02f, py * 0.7f, s + 3, 4);
        float t = Math.Clamp(0.48f + 0.2f * rings + 0.55f * (fibre - 0.5f), 0, 1);

        var dark = new Color(0.17f, 0.10f, 0.06f);
        var light = new Color(0.42f, 0.27f, 0.15f);
        var c = dark.Lerp(light, t) * (0.82f + 0.3f * Hash(plank, 1, seed)); // every plank a bit different

        // Grit: fine speckle, darker patches of grime and the odd scratch.
        c *= 0.9f + 0.2f * Hash(x, y, seed + 99);
        c *= 0.75f + 0.25f * SmoothStep(0.25f, 0.6f, Fbm(x * 0.01f, y * 0.01f, seed + 11, 3));
        if (Hash(x / 3, (int)py / 2 + plank * 50, seed + 5) > 0.997f)
            c *= 0.55f;

        // Seams between planks: a dark gap, a lit top edge and a shadowed bottom edge.
        if (plankHeight < 150)
        {
            if (py < 2f)
                return new Color(0.04f, 0.025f, 0.015f);
            if (py < 3.5f)
                c *= 1.3f;
            else if (py > plankHeight - 3f)
                c *= 0.6f;
        }
        return c;
    }

    static Color Iron(int x, int y, int seed, float light)
    {
        float grain = Fbm(x * 0.15f, y * 0.15f, seed + 21, 3);
        var c = new Color(0.2f, 0.19f, 0.18f) * (0.75f + 0.5f * grain) * light;
        float rust = SmoothStep(0.55f, 0.75f, Fbm(x * 0.05f, y * 0.05f, seed + 33, 3));
        c = c.Lerp(new Color(0.3f, 0.15f, 0.07f), rust * 0.6f);
        c *= 0.92f + 0.16f * Hash(x, y, seed + 2);
        return c;
    }

    static Color Desaturate(Color c, float amount)
    {
        float grey = c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
        return c.Lerp(new Color(grey, grey, grey, c.A), amount);
    }

    // ---- Noise ----

    static float Hash(int x, int y, int seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777215f;
    }

    static float ValueNoise(float x, float y, int seed)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed);
        float c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
        return float.Lerp(float.Lerp(a, b, tx), float.Lerp(c, d, tx), ty);
    }

    /// <summary>Fractal value noise in 0..1.</summary>
    static float Fbm(float x, float y, int seed, int octaves)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += ValueNoise(x, y, seed + i * 101) * amplitude;
            total += amplitude;
            x *= 2.03f;
            y *= 2.03f;
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    static float SmoothStep(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>RGBA8 pixels in a plain array, turned into a texture in one call. Every pixel gets the same alpha.</summary>
    sealed class Canvas(int width, int height, float alpha = 1f)
    {
        readonly byte[] _data = new byte[width * height * 4];

        public void Set(int x, int y, Color c)
        {
            int i = (y * width + x) * 4;
            _data[i] = (byte)(Math.Clamp(c.R, 0, 1) * 255);
            _data[i + 1] = (byte)(Math.Clamp(c.G, 0, 1) * 255);
            _data[i + 2] = (byte)(Math.Clamp(c.B, 0, 1) * 255);
            _data[i + 3] = (byte)(alpha * 255);
        }

        public ImageTexture ToTexture()
        {
            var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, _data);
            image.GenerateMipmaps();
            return ImageTexture.CreateFromImage(image);
        }
    }
}
