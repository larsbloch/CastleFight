using System;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>
/// Procedural textures for the in-game HUD (Warcraft 3 style): castle stone, bronze-rimmed wells for the minimap
/// and unit stats, metal-rimmed slots for inventory and commands, and navy buttons with a gold rim.
/// </summary>
static partial class UiArt
{
    // ---- Stone ----

    /// <summary>
    /// Dressed grey stone in courses of blocks with mortar joints and bevelled, chipped edges. Seamless in both
    /// directions (block noise is evaluated in block-local coordinates), so it can be repeated over any shape.
    /// </summary>
    public static ImageTexture StoneTile(int size = 320, int seed = 4)
    {
        const int Courses = 7;
        float courseHeight = size / (float)Courses;
        var random = new Random(seed);

        // Per course: block edges as a running total, and a random offset so joints never line up.
        var offsets = new float[Courses];
        var edges = new float[Courses][];
        for (int course = 0; course < Courses; course++)
        {
            int blocks = 2 + random.Next(3);
            var widths = new float[blocks];
            float total = 0;
            for (int b = 0; b < blocks; b++)
                total += widths[b] = 1f + 0.9f * random.NextSingle();
            edges[course] = new float[blocks + 1];
            for (int b = 0; b < blocks; b++)
                edges[course][b + 1] = edges[course][b] + widths[b] / total * size;
            offsets[course] = random.NextSingle() * size;
        }

        var pixels = new Canvas(size, size);
        for (int y = 0; y < size; y++)
        {
            int course = Math.Min((int)(y / courseHeight), Courses - 1);
            float inY = y + 0.5f - course * courseHeight;
            var e = edges[course];
            for (int x = 0; x < size; x++)
            {
                float along = (x + 0.5f - offsets[course] + size) % size;
                int block = 0;
                while (block < e.Length - 2 && along >= e[block + 1])
                    block++;
                float toRight = e[block + 1] - along, toBottom = courseHeight - inY;
                int id = course * 16 + block;
                float lx = along - e[block] + id * 97, ly = inY + id * 31; // block-local, so the noise wraps with the block

                // Rough-hewn edges: the distances to each side wobble along the edge.
                float wobble = 3.5f * (Fbm(lx * 0.07f, ly * 0.07f, seed + 5, 2) - 0.5f);
                float left = along - e[block] + wobble, right = toRight - wobble, top = inY + wobble, bottom = toBottom - wobble;

                // Mortar, with the block corners chipped round.
                float edge = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
                float cornerX = Math.Min(left, right), cornerY = Math.Min(top, bottom);
                bool chipped = cornerX < 7 && cornerY < 7 && (7 - cornerX) * (7 - cornerX) + (7 - cornerY) * (7 - cornerY) > 38;
                if (edge < 2.5f || chipped)
                {
                    pixels.Set(x, y, new Color(0.06f, 0.055f, 0.05f) * (0.8f + 0.4f * Hash(x, y, seed + 1)));
                    continue;
                }

                float tint = Hash(id, 0, seed);
                var c = new Color(0.31f, 0.32f, 0.34f).Lerp(new Color(0.5f, 0.46f, 0.4f), tint);
                if (Hash(id, 1, seed) > 0.82f)
                    c *= 0.74f; // the odd darker block
                c *= 0.74f + 0.44f * Fbm(lx * 0.045f, ly * 0.045f, seed + 2, 4);
                c *= 0.9f + 0.2f * Hash(x, y, seed + 3);
                c *= 1.04f - 0.16f * (inY / courseHeight); // grime settles low
                // Pits and the odd crack.
                if (Fbm(lx * 0.22f, ly * 0.22f, seed + 4, 2) > 0.73f)
                    c *= 0.6f;

                // Bevel: lit from the top left.
                if (top < 6f)
                    c *= 1.4f - 0.05f * top;
                else if (left < 5f)
                    c *= 1.16f;
                else if (bottom < 7f)
                    c *= 0.5f + 0.05f * bottom;
                else if (right < 6f)
                    c *= 0.72f;
                pixels.Set(x, y, c);
            }
        }
        return pixels.ToTexture();
    }

    // ---- Wells and slots ----

    public const int WellMargin = 8;

    /// <summary>A near-black recess with a raised bronze rim, for the minimap, portrait and unit stats.</summary>
    public static ImageTexture Well(int size = 96, int seed = 6)
    {
        var pixels = new Canvas(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int fromTop = y, fromLeft = x, fromBottom = size - 1 - y, fromRight = size - 1 - x;
                int d = Math.Min(Math.Min(fromTop, fromLeft), Math.Min(fromBottom, fromRight));
                bool lit = Math.Min(fromTop, fromLeft) == d && Math.Min(fromBottom, fromRight) > d;
                Color c;
                if (d == 0 || d == 5)
                    c = new Color(0.015f, 0.012f, 0.01f);
                else if (d < 5)
                    c = Bronze(x, y, seed, lit ? 1.25f - 0.08f * d : 0.55f);
                else
                {
                    c = new Color(0.03f, 0.03f, 0.036f) * (0.85f + 0.3f * Hash(x, y, seed));
                    c *= 0.4f + 0.6f * SmoothStep(5, 14, Math.Min(fromTop, fromLeft)); // inner shadow
                }
                pixels.Set(x, y, c);
            }
        }
        return pixels.ToTexture();
    }

    public const int SlotMargin = 6;

    /// <summary>An empty inventory or command slot: dark blue-black with a bevelled gunmetal rim.</summary>
    public static ImageTexture Slot(int size = 64, int seed = 8)
    {
        var pixels = new Canvas(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int fromTop = y, fromLeft = x, fromBottom = size - 1 - y, fromRight = size - 1 - x;
                int d = Math.Min(Math.Min(fromTop, fromLeft), Math.Min(fromBottom, fromRight));
                bool lit = Math.Min(fromTop, fromLeft) == d && Math.Min(fromBottom, fromRight) > d;
                Color c;
                if (d == 0 || d == 3)
                    c = new Color(0.01f, 0.01f, 0.012f);
                else if (d < 3)
                    c = Iron(x, y, seed, 1f) * (lit ? 2.6f : 0.9f);
                else
                {
                    c = new Color(0.035f, 0.045f, 0.075f) * (0.8f + 0.4f * Fbm(x * 0.12f, y * 0.12f, seed, 3));
                    c *= 0.35f + 0.65f * SmoothStep(3, 12, Math.Min(fromTop, fromLeft));
                }
                pixels.Set(x, y, c);
            }
        }
        return pixels.ToTexture();
    }

    // ---- Buttons ----

    public const int HudButtonMargin = 5;

    /// <summary>A top-bar button: deep navy with a gloss and a bevelled gold rim, like Warcraft 3's.</summary>
    public static ImageTexture HudButton(ButtonState state, int width = 192, int height = 36, int seed = 10)
    {
        var pixels = new Canvas(width, height);
        bool pressed = state == ButtonState.Pressed;
        float glow = state == ButtonState.Hover ? 1.3f : 1f;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int fromTop = y, fromLeft = x, fromBottom = height - 1 - y, fromRight = width - 1 - x;
                int d = Math.Min(Math.Min(fromTop, fromLeft), Math.Min(fromBottom, fromRight));
                bool lit = (Math.Min(fromTop, fromLeft) == d && Math.Min(fromBottom, fromRight) > d) != pressed;
                Color c;
                if (d == 0 || d == 3)
                    c = new Color(0.01f, 0.008f, 0.005f);
                else if (d < 3)
                    c = (lit ? new Color(0.98f, 0.8f, 0.4f) : new Color(0.52f, 0.36f, 0.13f)) * glow;
                else
                {
                    float t = (y - 4) / (float)(height - 8);
                    c = new Color(0.11f, 0.15f, 0.36f).Lerp(new Color(0.025f, 0.04f, 0.13f), t);
                    if (t < 0.42f)
                        c += new Color(0.05f, 0.06f, 0.1f) * (1 - t / 0.42f); // gloss
                    c *= (0.92f + 0.16f * Fbm(x * 0.1f, y * 0.3f, seed, 2)) * glow * (pressed ? 0.7f : 1f);
                }
                if (state == ButtonState.Disabled)
                    c = Desaturate(c, 0.75f) * 0.6f;
                pixels.Set(x, y, c);
            }
        }
        return pixels.ToTexture();
    }

    static Color Bronze(int x, int y, int seed, float light)
    {
        var c = new Color(0.62f, 0.45f, 0.2f) * (0.85f + 0.3f * Fbm(x * 0.2f, y * 0.2f, seed + 40, 2)) * light;
        return c * (0.94f + 0.12f * Hash(x, y, seed + 41));
    }
}
