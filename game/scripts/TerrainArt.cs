using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace CastleFight.Game;

/// <summary>
/// Seamlessly tiling textures for the map: grass, dirt, cliff stone and forest canopy, a large-scale variation
/// noise, and the mask that paints dirt paths onto the ground. Painted once in code like the menu art
/// (<see cref="UI.UiArt"/>), so there are no image assets and every colour can be tuned here. To use hand-made
/// art instead, load a texture in the matching property and set its tile size.
/// </summary>
static class TerrainArt
{
    /// <summary>World units covered by one repeat of each texture.</summary>
    public const float GrassTile = 16f, DirtTile = 12f, StoneTile = 4f, ForestTile = 8f, VariationTile = 90f;

    static ImageTexture? _grass, _dirt, _stone, _forest, _variation;

    public static ImageTexture Grass => _grass ??= PaintGrass(1024, seed: 11);
    public static ImageTexture Dirt => _dirt ??= PaintDirt(1024, seed: 23);
    public static ImageTexture Stone => _stone ??= PaintStone(512, seed: 37);
    public static ImageTexture Forest => _forest ??= PaintForest(512, seed: 41);

    /// <summary>Soft grey blotches, used to break up the repetition of the ground textures.</summary>
    public static ImageTexture Variation => _variation ??= PaintVariation(256, seed: 53);

    // ---- Ground ----

    static ImageTexture PaintGrass(int size, int seed)
    {
        Color dark = new(0.13f, 0.3f, 0.08f), mid = new(0.25f, 0.47f, 0.12f);
        Color light = new(0.42f, 0.62f, 0.18f), dry = new(0.55f, 0.57f, 0.22f);
        var canvas = new Canvas(size);
        canvas.Fill((u, v) =>
        {
            float patches = Fbm(u, v, 4, seed, 3);
            float clumps = Fbm(u, v, 24, seed + 1, 3);
            var c = dark.Lerp(mid, SmoothStep(0.25f, 0.6f, patches));
            c = c.Lerp(light, 0.6f * SmoothStep(0.5f, 0.8f, clumps * 0.6f + patches * 0.4f));
            c = c.Lerp(dry, 0.35f * SmoothStep(0.62f, 0.8f, Fbm(u, v, 6, seed + 2, 3)));
            return c * (0.85f + 0.3f * clumps);
        });

        // Blades: short tapered strokes, a little lighter or darker than the ground under them.
        var random = new Random(seed);
        int blades = size * size / 22;
        for (int i = 0; i < blades; i++)
        {
            float x = random.NextSingle() * size, y = random.NextSingle() * size;
            float angle = random.NextSingle() * MathF.Tau, length = 4f + random.NextSingle() * 7f;
            var under = canvas.Get((int)x, (int)y);
            var color = random.NextSingle() < 0.6f ? (under * 1.35f).Lerp(light, 0.25f) : under * 0.6f;
            canvas.Stroke(x, y, angle, length, color, 0.55f);
        }

        // A few tiny flowers.
        for (int i = 0; i < size / 6; i++)
        {
            var petal = random.NextSingle() < 0.5f ? new Color(0.95f, 0.92f, 0.75f) : new Color(0.95f, 0.8f, 0.3f);
            canvas.Disc(random.NextSingle() * size, random.NextSingle() * size, 1.6f, (_, _, _, _) => petal, 0.85f);
        }
        return canvas.ToTexture();
    }

    static ImageTexture PaintDirt(int size, int seed)
    {
        Color dark = new(0.3f, 0.21f, 0.13f), mid = new(0.47f, 0.35f, 0.22f), light = new(0.6f, 0.49f, 0.33f);
        var canvas = new Canvas(size);
        canvas.Fill((u, v) =>
        {
            // Gentle patches only: strong ones would show the texture repeating along the lanes.
            float patches = Fbm(u, v, 4, seed, 3);
            float clods = Fbm(u, v, 24, seed + 2, 3);
            float grain = Fbm(u, v, 96, seed + 1, 2);
            var c = mid.Lerp(dark, 0.35f * SmoothStep(0.55f, 0.25f, patches));
            c = c.Lerp(light, 0.35f * SmoothStep(0.5f, 0.75f, patches * 0.5f + clods * 0.5f));
            return c * (0.82f + 0.22f * clods + 0.14f * grain);
        });

        // Pebbles: small rounded stones lit from the top left, each with a shadow on the other side.
        var random = new Random(seed);
        int pebbles = size * size / 700;
        for (int i = 0; i < pebbles; i++)
        {
            float x = random.NextSingle() * size, y = random.NextSingle() * size;
            float r = 1.5f + 5f * random.NextSingle() * random.NextSingle();
            var stone = new Color(0.52f, 0.47f, 0.4f) * (0.7f + 0.5f * random.NextSingle());
            canvas.Disc(x + r * 0.35f, y + r * 0.35f, r * 1.1f, (_, _, _, _) => Colors.Black, 0.35f);
            canvas.Disc(x, y, r, (dx, dy, _, _) => stone * (0.8f + 0.5f * Math.Clamp(-(dx + dy) * 0.7f, -0.6f, 1f)), 1f);
        }
        return canvas.ToTexture();
    }

    // ---- Walls ----

    /// <summary>Courses of dressed stone blocks with mortar joints, bevelled edges and a little moss.</summary>
    static ImageTexture PaintStone(int size, int seed)
    {
        const int Courses = 6;
        float courseHeight = size / (float)Courses;
        var random = new Random(seed);

        // Per course: where the blocks start, as a running total from a random offset (so joints never line up).
        var offsets = new float[Courses];
        var starts = new float[Courses][];
        for (int course = 0; course < Courses; course++)
        {
            int blocks = 3 + random.Next(2);
            var widths = new float[blocks];
            for (int b = 0; b < blocks; b++)
                widths[b] = 1f + 0.8f * random.NextSingle();
            float total = 0;
            foreach (float w in widths)
                total += w;
            starts[course] = new float[blocks + 1];
            for (int b = 0; b < blocks; b++)
                starts[course][b + 1] = starts[course][b] + widths[b] / total * size;
            offsets[course] = random.NextSingle() * size;
        }

        Color mortar = new(0.17f, 0.15f, 0.13f), moss = new(0.22f, 0.32f, 0.12f);
        var canvas = new Canvas(size);
        canvas.Fill((u, v) =>
        {
            float x = u * size, y = v * size;
            int course = Math.Min((int)(y / courseHeight), Courses - 1);
            float inY = y - course * courseHeight;
            float along = (x - offsets[course] + size) % size;
            var edges = starts[course];
            int block = 0;
            while (along >= edges[block + 1])
                block++;
            float inX = along - edges[block];

            // Distance to the block's edge, wobbled so the joints are not ruler-straight.
            float edge = Math.Min(Math.Min(inX, edges[block + 1] - along), Math.Min(inY, courseHeight - inY));
            edge += (Fbm(u, v, 32, seed + 3, 2) - 0.5f) * 5f;
            if (edge < 2.5f)
                return mortar * (0.8f + 0.4f * Fbm(u, v, 64, seed + 4, 2));

            float tint = Hash(course, block, seed);
            var c = new Color(0.5f, 0.48f, 0.43f).Lerp(new Color(0.66f, 0.61f, 0.52f), tint);
            c *= 0.8f + 0.35f * Fbm(u, v, 16, seed + 5, 3) + 0.1f * (Hash((int)x, (int)y, seed) - 0.5f);

            // Bevel: the top of each block catches the light, the bottom falls into shadow.
            if (inY < 7f)
                c *= 1.15f;
            else if (courseHeight - inY < 8f)
                c *= 0.72f;
            else if (Math.Min(inX, edges[block + 1] - along) < 6f)
                c *= 0.88f;

            float mossy = SmoothStep(0.6f, 0.78f, Fbm(u, v, 6, seed + 6, 3)) * SmoothStep(0.3f, 1f, inY / courseHeight);
            return c.Lerp(moss, 0.65f * mossy);
        });
        return canvas.ToTexture();
    }

    /// <summary>Tree crowns seen from above, packed tightly over deep shade.</summary>
    static ImageTexture PaintForest(int size, int seed)
    {
        var canvas = new Canvas(size);
        canvas.Fill((u, v) => new Color(0.03f, 0.07f, 0.03f) * (0.7f + 0.6f * Fbm(u, v, 8, seed, 3)));

        var random = new Random(seed);
        for (int i = 0; i < 170; i++)
        {
            float x = random.NextSingle() * size, y = random.NextSingle() * size;
            float r = size * (0.05f + 0.045f * random.NextSingle());
            var leaf = random.NextSingle() < 0.2f
                ? new Color(0.06f, 0.2f, 0.09f) // pine
                : new Color(0.11f, 0.27f, 0.08f).Lerp(new Color(0.22f, 0.38f, 0.1f), random.NextSingle());
            int leafSeed = seed + 7 + i % 3;

            // Shadow on whatever is below, then the crown, lit from the top left with leafy clumps and a ragged rim.
            canvas.Disc(x + r * 0.25f, y + r * 0.3f, r * 1.05f, (_, _, _, _) => Colors.Black, 0.45f);
            canvas.Disc(x, y, r, (dx, dy, u, v) =>
            {
                float leaves = Fbm(u, v, 40, leafSeed, 2);
                float rim = MathF.Sqrt(dx * dx + dy * dy);
                if (rim > 0.82f + 0.25f * leaves)
                    return null;
                float nz = MathF.Sqrt(MathF.Max(0f, 1f - rim * rim));
                float light = Math.Clamp(-0.45f * dx - 0.45f * dy + 0.75f * nz, 0f, 1f);
                return leaf * (0.45f + 0.9f * light) * (0.65f + 0.7f * leaves);
            }, 1f);
        }
        return canvas.ToTexture();
    }

    static ImageTexture PaintVariation(int size, int seed)
    {
        var canvas = new Canvas(size);
        canvas.Fill((u, v) =>
        {
            float n = SmoothStep(0.2f, 0.8f, Fbm(u, v, 4, seed, 4));
            return new Color(n, n, n);
        });
        return canvas.ToTexture();
    }

    // ---- Paths ----

    /// <summary>Mask pixels per map tile.</summary>
    public const int PathMaskResolution = 4;

    /// <summary>
    /// Where the ground is dirt instead of grass, over the whole map: 0.5 on the edge of a path, rising to 1 inside
    /// and falling to 0 outside over <paramref name="softness"/> tiles, so the shader can fray the edge with noise.
    /// </summary>
    public static ImageTexture PathMask(int length, int width, IReadOnlyList<Rect2> paths, float softness = 2f)
    {
        int w = length * PathMaskResolution, h = width * PathMaskResolution;
        var data = new byte[w * h];
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                var p = new Vector2(px + 0.5f, py + 0.5f) / PathMaskResolution;
                float distance = float.MaxValue;
                foreach (var path in paths)
                    distance = MathF.Min(distance, SignedDistance(path, p));
                data[py * w + px] = (byte)(Math.Clamp(0.5f - distance / (2f * softness), 0f, 1f) * 255f);
            }
        });
        return ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.R8, data));
    }

    /// <summary>Distance from <paramref name="p"/> to the rectangle's edge; negative inside.</summary>
    static float SignedDistance(Rect2 rect, Vector2 p)
    {
        var d = (p - rect.GetCenter()).Abs() - rect.Size / 2f;
        return new Vector2(MathF.Max(d.X, 0f), MathF.Max(d.Y, 0f)).Length() + MathF.Min(MathF.Max(d.X, d.Y), 0f);
    }

    // ---- Tiling noise ----

    static float Hash(int x, int y, int seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777215f;
    }

    /// <summary>Value noise on a lattice that wraps every <paramref name="period"/> cells.</summary>
    static float TileNoise(float x, float y, int period, int seed)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period), ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
        float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed);
        float c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
        return float.Lerp(float.Lerp(a, b, tx), float.Lerp(c, d, tx), ty);
    }

    /// <summary>
    /// Fractal value noise in 0..1 at texture coordinates <paramref name="u"/>, <paramref name="v"/> in 0..1, with
    /// <paramref name="cells"/> lattice cells across in the first octave. Tiles seamlessly.
    /// </summary>
    static float Fbm(float u, float v, int cells, int seed, int octaves)
    {
        float sum = 0, amplitude = 0.5f, total = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += TileNoise(u * cells, v * cells, cells, seed + i * 101) * amplitude;
            total += amplitude;
            cells *= 2;
            amplitude *= 0.5f;
        }
        return sum / total;
    }

    static int Wrap(int value, int period) => ((value % period) + period) % period;

    static float SmoothStep(float from, float to, float value)
    {
        float t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>A square of colours that wraps at the edges, so everything painted on it tiles.</summary>
    sealed class Canvas(int size)
    {
        readonly Color[] _pixels = new Color[size * size];

        /// <summary>Sets every pixel from texture coordinates (u, v) in 0..1, rows in parallel.</summary>
        public void Fill(Func<float, float, Color> paint) =>
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                    _pixels[y * size + x] = paint((x + 0.5f) / size, (y + 0.5f) / size);
            });

        public Color Get(int x, int y) => _pixels[Wrap(y, size) * size + Wrap(x, size)];

        public void Blend(int x, int y, Color color, float alpha)
        {
            int i = Wrap(y, size) * size + Wrap(x, size);
            _pixels[i] = _pixels[i].Lerp(color, alpha);
        }

        /// <summary>A one-pixel line that fades out towards its tip.</summary>
        public void Stroke(float x, float y, float angle, float length, Color color, float alpha)
        {
            float dx = MathF.Cos(angle), dy = MathF.Sin(angle);
            int lastX = int.MinValue, lastY = int.MinValue;
            for (float t = 0; t < length; t += 0.5f)
            {
                int px = (int)MathF.Floor(x + dx * t), py = (int)MathF.Floor(y + dy * t);
                if (px == lastX && py == lastY)
                    continue;
                Blend(px, py, color, alpha * (1f - t / length));
                (lastX, lastY) = (px, py);
            }
        }

        /// <summary>
        /// A soft-edged disc. <paramref name="shade"/> gets the offset from the centre in radii (dx, dy) and the
        /// texture coordinates (u, v), and returns the colour there, or null to leave the pixel alone.
        /// </summary>
        public void Disc(float cx, float cy, float r, Func<float, float, float, float, Color?> shade, float alpha)
        {
            for (int py = (int)MathF.Floor(cy - r); py <= (int)MathF.Ceiling(cy + r); py++)
            {
                for (int px = (int)MathF.Floor(cx - r); px <= (int)MathF.Ceiling(cx + r); px++)
                {
                    float dx = (px + 0.5f - cx) / r, dy = (py + 0.5f - cy) / r;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d > 1f)
                        continue;
                    var color = shade(dx, dy, (Wrap(px, size) + 0.5f) / size, (Wrap(py, size) + 0.5f) / size);
                    if (color is { } c)
                        Blend(px, py, c, alpha * Math.Clamp((1f - d) * r, 0f, 1f));
                }
            }
        }

        public ImageTexture ToTexture()
        {
            var data = new byte[size * size * 4];
            for (int i = 0; i < _pixels.Length; i++)
            {
                var c = _pixels[i];
                data[i * 4] = (byte)(Math.Clamp(c.R, 0, 1) * 255);
                data[i * 4 + 1] = (byte)(Math.Clamp(c.G, 0, 1) * 255);
                data[i * 4 + 2] = (byte)(Math.Clamp(c.B, 0, 1) * 255);
                data[i * 4 + 3] = 255;
            }
            var image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
            image.GenerateMipmaps();
            return ImageTexture.CreateFromImage(image);
        }
    }
}
