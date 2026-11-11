using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.Graphics;

namespace TanksRebirth.GameContent;

/// <summary>
/// The looks of the new block types (shapes and painted textures, all made in code). This is only how they LOOK:
/// nothing here collides, breaks or affects tanks and shells. See docs/NEW_BLOCKS.md for how each one is meant to
/// behave and how to hook it up.
///
/// One block is a cube <see cref="Size"/> units across, standing on the floor at the position given to
/// <see cref="Draw"/>. "state" means different things per block (how damaged, how far open, how far out);
/// each look's meaning is listed on <see cref="BlockLook"/>.
/// </summary>
public enum BlockLook {
    /// <summary>state 0 = sound, 1 = cracked after the first hit.</summary>
    RottenWood,
    /// <summary>See-through; shatters in one hit.</summary>
    Glass,
    Crate,
    ExplosiveBarrel,
    /// <summary>state 0 = whole, 0.5 = chipped, 1 = nearly gone (it gets shorter).</summary>
    Ice,
    Steel,
    Rubber,
    Mirror,
    HayBale,
    /// <summary>Bars: shells pass, tanks don't.</summary>
    Grate,
    Bush,
    /// <summary>Floor patch.</summary>
    Mud,
    /// <summary>Floor patch.</summary>
    IceFloor,
    /// <summary>Floor patch; it moves things toward +Z (turn it with "yaw").</summary>
    Conveyor,
    /// <summary>state 0 = shut, 1 = open (the bars sink into the floor).</summary>
    Gate,
    /// <summary>state 0 = hidden in the floor, 1 = fully out.</summary>
    Spikes,
    /// <summary>Floor patch that glows.</summary>
    Lava,
    Magnet,
}

public static class BlockDesigns {
    /// <summary>A block's width (the same as the game's wood and cork blocks).</summary>
    public const float Size = Block.SIDE_LENGTH;

    // ------------------------------------------------------------------ painting textures

    private const int Tile = 128;

    private static float Hash(int x, int y) {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }

    /// <summary>Smooth noise that repeats every <paramref name="period"/> cells, so textures tile.</summary>
    private static float Noise(float x, float y, int period = 1024) {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        int W(int v) => ((v % period) + period) % period;
        float a = MathHelper.Lerp(Hash(W(xi), W(yi)), Hash(W(xi + 1), W(yi)), fx);
        float b = MathHelper.Lerp(Hash(W(xi), W(yi + 1)), Hash(W(xi + 1), W(yi + 1)), fx);
        return MathHelper.Lerp(a, b, fy);
    }
    private static float Fbm(float x, float y, int period) => Noise(x, y, period) * 0.6f + Noise(x * 2, y * 2, period * 2) * 0.28f + Noise(x * 4, y * 4, period * 4) * 0.12f;

    private static float Frac(float x) => x - MathF.Floor(x);
    private static float Step(float a, float b, float x) => MathHelper.Clamp((x - a) / (b - a), 0f, 1f);
    private static Vector4 Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);
    private static Vector4 Mix(Vector4 a, Vector4 b, float t) => Vector4.Lerp(a, b, MathHelper.Clamp(t, 0f, 1f));
    private static Vector4 Shade(Vector4 c, float k) => new(c.X * k, c.Y * k, c.Z * k, c.W);

    /// <summary>A beveled edge all round a tile: bright along the top and left, dark along the bottom and right.</summary>
    private static Vector4 Bevel(Vector4 c, float u, float v, float width = 0.06f, float strength = 0.35f) {
        float e = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        if (e > width)
            return c;
        bool lit = u < width && u <= v && u <= 1 - v || v < width && v <= u && v <= 1 - u;
        float k = (1f - e / width) * strength;
        return lit ? Mix(c, new Vector4(1, 1, 1, c.W), k) : Shade(c, 1f - k);
    }

    /// <summary>How close a point is to a wandering crack line (0 = on it).</summary>
    private static float Crack(float u, float v, float seed) {
        float n = Fbm(u * 5f + seed, v * 5f + seed * 1.7f, 5);
        return MathF.Abs(n - 0.5f);
    }

    private delegate Vector4 Painter(float u, float v);

    private static readonly Dictionary<string, Texture2D> _textures = [];

    private static Texture2D Texture(GraphicsDevice gd, string key, Painter paint) {
        if (_textures.TryGetValue(key, out var tex) && !tex.IsDisposed)
            return tex;
        var data = new Color[Tile * Tile];
        for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++) {
                var c = paint((x + 0.5f) / Tile, (y + 0.5f) / Tile);
                data[y * Tile + x] = new Color(MathHelper.Clamp(c.X, 0, 1), MathHelper.Clamp(c.Y, 0, 1), MathHelper.Clamp(c.Z, 0, 1), MathHelper.Clamp(c.W, 0, 1));
            }
        tex = new Texture2D(gd, Tile, Tile, true, SurfaceFormat.Color);
        tex.SetData(0, null, data, 0, data.Length);
        // smaller sizes, each the average of the one before
        int w = Tile;
        for (int level = 1; level < tex.LevelCount; level++) {
            int nw = Math.Max(1, w / 2);
            var next = new Color[nw * nw];
            for (int y = 0; y < nw; y++)
                for (int x = 0; x < nw; x++) {
                    Color a = data[y * 2 * w + x * 2], b = data[y * 2 * w + Math.Min(w - 1, x * 2 + 1)],
                        c = data[Math.Min(w - 1, y * 2 + 1) * w + x * 2], e = data[Math.Min(w - 1, y * 2 + 1) * w + Math.Min(w - 1, x * 2 + 1)];
                    next[y * nw + x] = new Color((a.R + b.R + c.R + e.R) / 4, (a.G + b.G + c.G + e.G) / 4, (a.B + b.B + c.B + e.B) / 4, (a.A + b.A + c.A + e.A) / 4);
                }
            tex.SetData(level, null, next, 0, next.Length);
            data = next;
            w = nw;
        }
        _textures[key] = tex;
        return tex;
    }

    // ---- the materials

    private static Vector4 Planks(float u, float v, Vector4 light, Vector4 dark, int planks, float seed) {
        float p = v * planks;
        int row = (int)MathF.Floor(p);
        float inRow = p - row;
        // grain running along each plank, a little different on each
        float grain = Noise(u * 3f + row * 7.3f + seed, v * 46f + seed, 1024);
        var c = Mix(dark, light, 0.35f + 0.65f * grain);
        c = Shade(c, 0.9f + 0.2f * Hash(row, (int)seed));
        if (inRow < 0.07f || inRow > 0.95f)
            c = Shade(c, 0.5f); // the gap between planks
        // a butt joint somewhere along each plank
        float joint = MathF.Abs(u - (0.2f + 0.6f * Hash(row, 3 + (int)seed)));
        if (joint < 0.012f)
            c = Shade(c, 0.55f);
        return c;
    }

    private static Vector4 RottenWood(float u, float v, bool cracked) {
        var c = Planks(u, v, Rgb(126, 108, 82), Rgb(74, 62, 48), 4, 11f);
        // damp, mossy patches, and worm holes
        float moss = Fbm(u * 4f + 2f, v * 4f + 9f, 4);
        c = Mix(c, Rgb(74, 98, 52), Step(0.52f, 0.7f, moss) * 0.75f);
        float rot = Fbm(u * 6f + 5f, v * 6f + 1f, 6);
        c = Mix(c, Rgb(44, 36, 30), Step(0.62f, 0.75f, rot) * 0.7f);
        for (int i = 0; i < 9; i++) {
            float hx = Hash(i, 41), hy = Hash(i, 57), hr = 0.012f + 0.014f * Hash(i, 77);
            if ((u - hx) * (u - hx) + (v - hy) * (v - hy) < hr * hr)
                c = Rgb(24, 18, 14);
        }
        if (cracked) {
            // split right across, with splinters of pale fresh wood along the breaks and chunks missing
            float k = Crack(u, v, 3.3f), k2 = Crack(u, v, 12.1f);
            if (k < 0.022f || k2 < 0.014f)
                c = Rgb(18, 14, 12);
            else if (k < 0.05f || k2 < 0.03f)
                c = Mix(c, Rgb(196, 170, 124), 0.7f);
            float gone = Fbm(u * 3f + 7.7f, v * 3f + 3.1f, 3);
            if (gone > 0.7f)
                c = Mix(Rgb(30, 24, 20), c, Step(0.76f, 0.7f, gone));
        }
        return Bevel(c, u, v, 0.05f, 0.3f);
    }

    private static Vector4 Glass(float u, float v) {
        // nearly clear, with a frame of thicker glass at the edges and streaks of reflected light
        float e = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        var c = Rgb(190, 232, 244, 0.26f);
        float streak = Frac((u + v) * 1.5f);
        if (streak > 0.36f && streak < 0.44f || streak > 0.5f && streak < 0.53f)
            c = Rgb(255, 255, 255, 0.62f);
        if (e < 0.07f)
            c = Mix(Rgb(236, 250, 255, 0.9f), Rgb(150, 210, 232, 0.7f), e / 0.07f);
        return c;
    }

    private static Vector4 CrateSide(float u, float v) {
        var c = Planks(v, u, Rgb(214, 170, 104), Rgb(160, 116, 62), 5, 23f);
        // a frame all round and a diagonal brace
        float e = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        float diag = MathF.Abs(u - v);
        bool frame = e < 0.14f, brace = diag < 0.075f;
        if (frame || brace) {
            c = Planks(frame && (u < 0.14f || u > 0.86f) ? v : u, frame && (u < 0.14f || u > 0.86f) ? u : v, Rgb(186, 138, 76), Rgb(132, 92, 46), 1, 31f);
            float edge = frame ? MathF.Abs(e - 0.14f) : MathF.Abs(diag - 0.075f);
            if (edge < 0.012f)
                c = Shade(c, 0.55f);
            // nails at the corners of the frame
            foreach (var (nx, ny) in new[] { (0.07f, 0.07f), (0.93f, 0.07f), (0.07f, 0.93f), (0.93f, 0.93f) })
                if ((u - nx) * (u - nx) + (v - ny) * (v - ny) < 0.0005f)
                    c = Rgb(70, 66, 64);
        }
        return Bevel(c, u, v, 0.03f, 0.3f);
    }

    private static Vector4 BarrelSide(float u, float v) {
        // u goes round the barrel, v from the top (0) down
        var c = Rgb(206, 48, 40);
        // lit from one side: brighter round the front
        c = Shade(c, 0.74f + 0.34f * MathF.Pow(0.5f + 0.5f * MathF.Cos((u - 0.2f) * MathHelper.TwoPi), 2f));
        // hoops
        foreach (float hoop in new[] { 0.04f, 0.34f, 0.66f, 0.96f }) {
            float d = MathF.Abs(v - hoop);
            if (d < 0.035f)
                c = Mix(Rgb(86, 22, 20), Rgb(238, 96, 80), 1f - MathF.Abs(d - 0.012f) / 0.035f);
        }
        // a warning diamond with a flame, twice round
        float du = MathF.Abs(Frac(u * 2f) - 0.5f) * 2f, dv = MathF.Abs(v - 0.5f) / 0.15f;
        if (du * 1.9f + dv < 1f) {
            c = du * 1.9f + dv > 0.82f ? Rgb(30, 26, 24) : Rgb(250, 206, 50);
            float fx = (Frac(u * 2f) - 0.5f) * 2f * 1.9f, fy = (v - 0.5f) / 0.15f;
            // the flame: a drop shape with a notch
            float drop = fx * fx * 5f + (fy - 0.15f) * (fy - 0.15f) * 2.2f - (fy < 0.15f ? (0.15f - fy) * 0.35f : 0f);
            if (drop < 0.3f && du * 1.9f + dv < 0.8f)
                c = drop < 0.09f ? Rgb(250, 206, 50) : Rgb(30, 26, 24);
        }
        return c;
    }

    private static Vector4 BarrelTop(float u, float v) {
        float r = MathF.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
        var c = Rgb(190, 44, 38);
        if (r > 0.86f) c = Rgb(240, 100, 84);          // the rim
        else if (r > 0.78f) c = Rgb(110, 26, 24);
        else if (MathF.Abs(r - 0.5f) < 0.03f) c = Rgb(140, 32, 28);
        // the bung, off-centre
        float b = MathF.Sqrt((u - 0.64f) * (u - 0.64f) + (v - 0.42f) * (v - 0.42f));
        if (b < 0.07f) c = b < 0.045f ? Rgb(60, 58, 60) : Rgb(190, 190, 196);
        return Shade(c, 0.94f + 0.12f * Noise(u * 9f, v * 9f));
    }

    private static Vector4 Ice(float u, float v, int stage) {
        // cloudy blue-white, frosty at the edges, with cracks that multiply as it's chipped
        float cloud = Fbm(u * 3f + 4f, v * 3f + 8f, 3);
        var c = Mix(Rgb(150, 208, 240, 0.78f), Rgb(226, 246, 255, 0.9f), cloud);
        float e = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        c = Mix(Rgb(246, 252, 255, 0.96f), c, Step(0f, 0.1f, e));
        float streak = Frac((u - v) * 1.2f + 0.1f);
        if (streak > 0.4f && streak < 0.46f)
            c = Mix(c, Rgb(255, 255, 255, 0.95f), 0.6f);
        for (int i = 0; i < 1 + stage * 2; i++) {
            float k = Crack(u, v, 2.1f + i * 5.7f);
            if (k < 0.012f)
                c = Rgb(250, 254, 255, 1f);
            else if (k < 0.03f)
                c = Mix(c, Rgb(96, 160, 210, 0.95f), 0.55f);
        }
        return c;
    }

    private static Vector4 Steel(float u, float v) {
        // a riveted plate with a brushed sheen
        var c = Rgb(140, 152, 168);
        c = Shade(c, 0.86f + 0.2f * Noise(u * 2f, v * 60f));
        float sheen = Frac((u + v * 0.6f) * 0.9f + 0.15f);
        c = Mix(c, Rgb(216, 226, 238), Step(0.5f, 0.56f, sheen) * Step(0.7f, 0.62f, sheen) * 0.55f);
        float e = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        // a seam just inside the edge, and rivets along it
        if (MathF.Abs(e - 0.13f) < 0.012f)
            c = Shade(c, 0.55f);
        float ru = MathF.Abs(Frac(u * 5f) - 0.5f) / 5f, rv = MathF.Abs(Frac(v * 5f) - 0.5f) / 5f;
        bool along = e > 0.045f && e < 0.085f;
        float rd = along ? (MathF.Min(u, 1 - u) < MathF.Min(v, 1 - v) ? rv : ru) : 1f;
        float off = MathF.Abs(e - 0.065f);
        if (rd * rd + off * off < 0.00075f)
            c = rd * rd + off * off < 0.00028f ? Rgb(226, 234, 244) : Rgb(72, 80, 94);
        return Bevel(c, u, v, 0.035f, 0.5f);
    }

    private static Vector4 Rubber(float u, float v) {
        // black tread with chevrons, and a bright band so it reads as bouncy
        var c = Rgb(42, 42, 48);
        float chev = Frac(v * 5f + MathF.Abs(u - 0.5f) * 2.2f);
        if (chev < 0.3f)
            c = Rgb(70, 70, 80);
        else if (chev < 0.36f)
            c = Rgb(22, 22, 26);
        if (MathF.Abs(v - 0.5f) < 0.085f)
            c = MathF.Abs(v - 0.5f) > 0.062f ? Rgb(20, 20, 24) : Frac(u * 6f) < 0.5f ? Rgb(255, 150, 40) : Rgb(255, 196, 60);
        c = Shade(c, 0.92f + 0.16f * Noise(u * 30f, v * 30f));
        return Bevel(c, u, v, 0.09f, 0.3f);
    }

    private static Vector4 Mirror(float u, float v) {
        float e = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        if (e < 0.1f) {
            // a gilt frame
            var f = Mix(Rgb(150, 110, 40), Rgb(250, 214, 110), 0.5f + 0.5f * MathF.Sin(e * 120f));
            return Bevel(f, u, v, 0.03f, 0.4f);
        }
        // the glass: a pale sky fading to a darker floor, with slanting glints
        var c = Mix(Rgb(232, 246, 255), Rgb(120, 156, 190), Step(0.1f, 0.9f, v));
        float glint = Frac((u + v) * 1.3f + 0.2f);
        if (glint > 0.3f && glint < 0.4f || glint > 0.46f && glint < 0.49f)
            c = Mix(c, Rgb(255, 255, 255), 0.75f);
        if (e < 0.125f)
            c = Shade(c, 0.6f);
        return c;
    }

    private static Vector4 Hay(float u, float v, bool end) {
        // straw: many fine strands, all running one way (across the sides, end-on in the ends)
        float strand = end ? Noise(u * 40f, v * 40f) : Noise(u * 4f, v * 70f) * 0.7f + Noise(u * 9f + 3f, v * 130f) * 0.3f;
        var c = Mix(Rgb(176, 132, 50), Rgb(246, 214, 112), strand);
        c = Shade(c, 0.9f + 0.2f * Fbm(u * 3f, v * 3f, 3));
        if (!end)
            foreach (float band in new[] { 0.28f, 0.72f }) {
                float d = MathF.Abs(u - band);
                if (d < 0.022f)
                    c = d < 0.014f ? Rgb(190, 52, 44) : Rgb(96, 60, 30); // twine
                else if (d < 0.06f)
                    c = Shade(c, 0.8f + 0.2f * (d - 0.022f) / 0.038f); // pulled in under the twine
            }
        return Bevel(c, u, v, 0.05f, 0.22f);
    }

    private static Vector4 Leaves(float u, float v) {
        // overlapping leaves in a few greens, darker in the gaps
        float cell = Fbm(u * 9f, v * 9f, 9);
        float fine = Noise(u * 26f, v * 26f, 26);
        var c = Mix(Rgb(40, 96, 44), Rgb(116, 184, 70), cell * 0.7f + fine * 0.3f);
        if (fine < 0.2f)
            c = Rgb(24, 62, 32);
        if (Hash((int)(u * 14f), (int)(v * 14f)) > 0.965f)
            c = Rgb(226, 60, 70); // a berry
        return c;
    }

    private static Vector4 Mud(float u, float v) {
        // wet brown, with shiny puddles; ragged at the edges so patches blend into the floor
        float f = Fbm(u * 4f, v * 4f, 4);
        var c = Mix(Rgb(84, 58, 40), Rgb(128, 94, 62), f);
        float puddle = Fbm(u * 3f + 9f, v * 3f + 2f, 3);
        c = Mix(c, Rgb(60, 44, 36), Step(0.56f, 0.62f, puddle));
        c = Mix(c, Rgb(196, 190, 176), Step(0.66f, 0.7f, puddle) * 0.6f);
        float r = MathF.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;
        float edge = r + (Noise(u * 7f, v * 7f) - 0.5f) * 0.3f;
        c.W = Step(1.0f, 0.82f, edge) * 0.95f;
        return c;
    }

    private static Vector4 IceFloor(float u, float v) {
        var c = Mix(Rgb(176, 222, 246), Rgb(232, 248, 255), Fbm(u * 3f, v * 3f, 3));
        float streak = Frac((u * 0.8f - v) * 2f);
        if (streak > 0.45f && streak < 0.5f || streak > 0.7f && streak < 0.72f)
            c = Rgb(255, 255, 255);
        float scratch = Noise(u * 3f, v * 50f);
        if (scratch > 0.86f)
            c = Shade(c, 0.9f);
        float e = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        c.W = 0.55f + 0.37f * Step(0f, 0.08f, e);
        return e < 0.03f ? Rgb(250, 254, 255, 0.95f) : c;
    }

    private static Vector4 Belt(float u, float v) {
        // a ribbed black belt with yellow arrows pointing the way it moves (down the texture = +Z)
        var c = Frac(v * 8f) < 0.12f ? Rgb(26, 26, 30) : Rgb(58, 58, 66);
        float arrow = Frac(v * 2f - MathF.Abs(u - 0.5f) * 1.1f);
        if (arrow < 0.22f && MathF.Abs(u - 0.5f) < 0.3f)
            c = Rgb(250, 204, 50);
        if (u < 0.09f || u > 0.91f)
            c = Frac(v * 4f + (u < 0.5f ? 0f : 0.5f)) < 0.5f ? Rgb(150, 156, 166) : Rgb(112, 118, 130); // the side rails
        return c;
    }

    private static Vector4 Stone(float u, float v) {
        var c = Mix(Rgb(120, 116, 110), Rgb(170, 166, 156), Fbm(u * 5f, v * 5f, 5));
        float course = Frac(v * 3f);
        float brick = Frac(u * 2f + (MathF.Floor(v * 3f) % 2 == 0 ? 0f : 0.5f));
        if (course < 0.06f || brick < 0.04f)
            c = Shade(c, 0.55f);
        return Bevel(c, u, v, 0.03f, 0.25f);
    }

    private static Vector4 Iron(float u, float v) {
        var c = Mix(Rgb(64, 68, 78), Rgb(150, 158, 172), MathF.Pow(MathF.Sin(u * MathHelper.Pi), 2f));
        return Shade(c, 0.9f + 0.15f * Noise(u * 20f, v * 20f));
    }

    private static Vector4 SpikePlate(float u, float v) {
        var c = Shade(Rgb(96, 100, 110), 0.9f + 0.2f * Noise(u * 20f, v * 20f));
        // a hole for each spike, and hazard stripes round the edge
        float hu = (Frac(u * 3f) - 0.5f) / 3f, hv = (Frac(v * 3f) - 0.5f) / 3f;
        if (hu * hu + hv * hv < 0.0042f)
            c = Rgb(18, 18, 22);
        else if (hu * hu + hv * hv < 0.0062f)
            c = Rgb(170, 176, 188);
        float e = MathF.Min(MathF.Min(u, 1 - u), MathF.Min(v, 1 - v));
        if (e < 0.06f)
            c = Frac((u + v) * 6f) < 0.5f ? Rgb(240, 196, 40) : Rgb(30, 28, 26);
        return c;
    }

    private static Vector4 Lava(float u, float v, float phase) {
        // glowing cracks between plates of dark crust that drift as it flows
        float f = Fbm(u * 3f + phase * 0.6f, v * 3f - phase * 0.35f, 3);
        float g = Fbm(u * 6f - phase * 0.4f, v * 6f + phase * 0.5f, 6);
        float heat = MathF.Abs(f - 0.5f) * 2f; // 0 in a crack
        var c = heat < 0.1f ? Rgb(255, 244, 170) : heat < 0.22f ? Rgb(255, 170, 40) : heat < 0.36f ? Rgb(226, 78, 20) : Mix(Rgb(92, 32, 20), Rgb(46, 24, 22), g);
        return c;
    }

    private static Vector4 Solid(Vector4 c, float u, float v, float grain = 0.12f) => Bevel(Shade(c, 1f - grain / 2f + grain * Noise(u * 24f, v * 24f)), u, v, 0.05f, 0.3f);

    // ------------------------------------------------------------------ building shapes

    private sealed class Part {
        public string Texture = "";
        public VertexPositionNormalTexture[] Verts = [];
    }

    private sealed class Builder {
        private readonly Dictionary<string, List<VertexPositionNormalTexture>> _by = [];
        public Matrix M = Matrix.Identity;

        private List<VertexPositionNormalTexture> L(string tex) {
            if (!_by.TryGetValue(tex, out var l))
                _by[tex] = l = [];
            return l;
        }

        public void Quad(string tex, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1) {
            // corners: a = top-left, b = top-right, c = bottom-right, d = bottom-left of the picture
            a = Vector3.Transform(a, M); b = Vector3.Transform(b, M); c = Vector3.Transform(c, M); d = Vector3.Transform(d, M);
            var n = Vector3.Normalize(Vector3.Cross(c - a, b - a));
            var l = L(tex);
            l.Add(new(a, n, new Vector2(u0, v0))); l.Add(new(b, n, new Vector2(u1, v0))); l.Add(new(c, n, new Vector2(u1, v1)));
            l.Add(new(a, n, new Vector2(u0, v0))); l.Add(new(c, n, new Vector2(u1, v1))); l.Add(new(d, n, new Vector2(u0, v1)));
        }

        /// <summary>A box standing on y = <paramref name="y0"/>, centred at x, z.</summary>
        public void Box(string side, string top, float x, float z, float hx, float hz, float y0, float y1) {
            Vector3 P(float px, float py, float pz) => new(x + px * hx, py, z + pz * hz);
            Quad(top, P(-1, y1, -1), P(1, y1, -1), P(1, y1, 1), P(-1, y1, 1));
            Quad(side, P(-1, y1, 1), P(1, y1, 1), P(1, y0, 1), P(-1, y0, 1));       // +z (toward the camera)
            Quad(side, P(1, y1, -1), P(-1, y1, -1), P(-1, y0, -1), P(1, y0, -1));   // -z
            Quad(side, P(1, y1, 1), P(1, y1, -1), P(1, y0, -1), P(1, y0, 1));       // +x
            Quad(side, P(-1, y1, -1), P(-1, y1, 1), P(-1, y0, 1), P(-1, y0, -1));   // -x
        }

        /// <summary>An upright round pillar; the side texture wraps round it <paramref name="wraps"/> times.</summary>
        public void Cylinder(string side, string top, float x, float z, float r0, float r1, float y0, float y1, int sides = 18, float wraps = 1f) {
            for (int i = 0; i < sides; i++) {
                float a0 = i * MathHelper.TwoPi / sides, a1 = (i + 1) * MathHelper.TwoPi / sides;
                Vector3 d0 = new(MathF.Sin(a0), 0, MathF.Cos(a0)), d1 = new(MathF.Sin(a1), 0, MathF.Cos(a1));
                Vector3 c = new(x, 0, z);
                var l = L(side);
                Vector3 b0 = Vector3.Transform(c + d0 * r0 + Vector3.Up * y0, M), b1 = Vector3.Transform(c + d1 * r0 + Vector3.Up * y0, M);
                Vector3 t0 = Vector3.Transform(c + d0 * r1 + Vector3.Up * y1, M), t1 = Vector3.Transform(c + d1 * r1 + Vector3.Up * y1, M);
                Vector3 n0 = Vector3.Normalize(Vector3.TransformNormal(d0, M)), n1 = Vector3.Normalize(Vector3.TransformNormal(d1, M));
                float u0 = i / (float)sides * wraps, u1 = (i + 1) / (float)sides * wraps;
                l.Add(new(t0, n0, new Vector2(u0, 0))); l.Add(new(t1, n1, new Vector2(u1, 0))); l.Add(new(b1, n1, new Vector2(u1, 1)));
                l.Add(new(t0, n0, new Vector2(u0, 0))); l.Add(new(b1, n1, new Vector2(u1, 1))); l.Add(new(b0, n0, new Vector2(u0, 1)));
                if (r1 > 0.01f) {
                    var tl = L(top);
                    var up = Vector3.Normalize(Vector3.TransformNormal(Vector3.Up, M));
                    var tc = Vector3.Transform(c + Vector3.Up * y1, M);
                    tl.Add(new(tc, up, new Vector2(0.5f, 0.5f)));
                    tl.Add(new(t0, up, new Vector2(0.5f + d0.X * 0.5f, 0.5f + d0.Z * 0.5f)));
                    tl.Add(new(t1, up, new Vector2(0.5f + d1.X * 0.5f, 0.5f + d1.Z * 0.5f)));
                }
            }
        }

        /// <summary>A ball (stretched by the radii), its texture wrapped round.</summary>
        public void Ball(string tex, Vector3 c, float rx, float ry, float rz, int rings = 6, int sides = 10) {
            var l = L(tex);
            (Vector3 P, Vector3 N, Vector2 Uv) At(int ring, int side) {
                float e = -MathHelper.PiOver2 + ring / (float)rings * MathHelper.Pi, a = side * MathHelper.TwoPi / sides;
                var u = new Vector3(MathF.Cos(e) * MathF.Sin(a), MathF.Sin(e), MathF.Cos(e) * MathF.Cos(a));
                return (Vector3.Transform(c + new Vector3(u.X * rx, u.Y * ry, u.Z * rz), M), Vector3.Normalize(u), new Vector2(side / (float)sides * 2f, ring / (float)rings));
            }
            for (int ring = 0; ring < rings; ring++)
                for (int side = 0; side < sides; side++) {
                    var a = At(ring, side); var b = At(ring, side + 1); var d = At(ring + 1, side); var e = At(ring + 1, side + 1);
                    l.Add(new(a.P, a.N, a.Uv)); l.Add(new(e.P, e.N, e.Uv)); l.Add(new(b.P, b.N, b.Uv));
                    l.Add(new(a.P, a.N, a.Uv)); l.Add(new(d.P, d.N, d.Uv)); l.Add(new(e.P, e.N, e.Uv));
                }
        }

        public List<Part> Take() {
            var parts = new List<Part>();
            foreach (var (tex, list) in _by)
                parts.Add(new Part { Texture = tex, Verts = list.ToArray() });
            _by.Clear();
            M = Matrix.Identity;
            return parts;
        }
    }

    // ------------------------------------------------------------------ the blocks

    private static readonly Dictionary<string, List<Part>> _meshes = [];
    private static readonly Dictionary<string, Painter> _painters = new() {
        ["rotten0"] = (u, v) => RottenWood(u, v, false),
        ["rotten1"] = (u, v) => RottenWood(u, v, true),
        ["glass"] = Glass,
        ["crate"] = CrateSide,
        ["barrelSide"] = BarrelSide,
        ["barrelTop"] = BarrelTop,
        ["ice0"] = (u, v) => Ice(u, v, 0),
        ["ice1"] = (u, v) => Ice(u, v, 1),
        ["ice2"] = (u, v) => Ice(u, v, 2),
        ["steel"] = Steel,
        ["rubber"] = Rubber,
        ["mirror"] = Mirror,
        ["mirrorBack"] = (u, v) => Solid(Rgb(170, 128, 52), u, v),
        ["haySide"] = (u, v) => Hay(u, v, false),
        ["hayEnd"] = (u, v) => Hay(u, v, true),
        ["leaves"] = Leaves,
        ["mud"] = Mud,
        ["iceFloor"] = IceFloor,
        ["belt"] = Belt,
        ["stone"] = Stone,
        ["iron"] = Iron,
        ["spikePlate"] = SpikePlate,
        ["lava0"] = (u, v) => Lava(u, v, 0f),
        ["lava1"] = (u, v) => Lava(u, v, 1f),
        ["lava2"] = (u, v) => Lava(u, v, 2f),
        ["lava3"] = (u, v) => Lava(u, v, 3f),
        ["darkSteel"] = (u, v) => Solid(Rgb(72, 78, 90), u, v),
        ["magnetRed"] = (u, v) => Solid(Rgb(214, 50, 46), u, v),
        ["magnetBlue"] = (u, v) => Solid(Rgb(48, 96, 214), u, v),
        ["magnetTip"] = (u, v) => Solid(Rgb(214, 220, 228), u, v, 0.05f),
    };

    private static List<Part> Mesh(BlockLook look, int stage) {
        string key = $"{look}{stage}";
        if (_meshes.TryGetValue(key, out var m))
            return m;
        var b = new Builder();
        float h = Size / 2f, s = Size;
        switch (look) {
            case BlockLook.RottenWood:
                // once cracked it sags: a little shorter, and askew
                if (stage == 1)
                    b.M = Matrix.CreateRotationZ(0.045f) * Matrix.CreateRotationX(-0.03f);
                b.Box($"rotten{stage}", $"rotten{stage}", 0, 0, h, h, 0, stage == 1 ? s * 0.9f : s);
                break;
            case BlockLook.Glass:
                b.Box("glass", "glass", 0, 0, h, h, 0, s);
                break;
            case BlockLook.Crate:
                b.Box("crate", "crate", 0, 0, h * 0.94f, h * 0.94f, 0, s * 0.94f);
                break;
            case BlockLook.ExplosiveBarrel:
                b.Cylinder("barrelSide", "barrelTop", 0, 0, h * 0.8f, h * 0.8f, 0, s * 1.05f, 18, 1f);
                break;
            case BlockLook.Ice: {
                float top = stage == 0 ? s : stage == 1 ? s * 0.72f : s * 0.42f;
                float w = stage == 0 ? h : stage == 1 ? h * 0.94f : h * 0.84f;
                if (stage > 0)
                    b.M = Matrix.CreateRotationY(0.09f * stage);
                b.Box($"ice{stage}", $"ice{stage}", 0, 0, w, w, 0, top);
                break;
            }
            case BlockLook.Steel:
                b.Box("steel", "steel", 0, 0, h, h, 0, s);
                break;
            case BlockLook.Rubber:
                b.Box("rubber", "rubber", 0, 0, h, h, 0, s);
                break;
            case BlockLook.Mirror:
                b.Box("mirror", "mirrorBack", 0, 0, h, h, 0, s);
                break;
            case BlockLook.HayBale: {
                // a bale lying on its side: strands run along it, cut ends face left and right
                Vector3 P(float x, float y, float z) => new(x * h * 0.98f, y, z * h * 0.9f);
                float top = s * 0.82f;
                b.Quad("haySide", P(-1, top, -1), P(1, top, -1), P(1, top, 1), P(-1, top, 1));
                b.Quad("haySide", P(-1, top, 1), P(1, top, 1), P(1, 0, 1), P(-1, 0, 1));
                b.Quad("haySide", P(1, top, -1), P(-1, top, -1), P(-1, 0, -1), P(1, 0, -1));
                b.Quad("hayEnd", P(1, top, 1), P(1, top, -1), P(1, 0, -1), P(1, 0, 1));
                b.Quad("hayEnd", P(-1, top, -1), P(-1, top, 1), P(-1, 0, 1), P(-1, 0, -1));
                break;
            }
            case BlockLook.Grate: {
                // a cage of bars: a frame along every edge, and bars across each face
                float t = s * 0.07f, bar = s * 0.035f;
                foreach (float x in new[] { -h + t / 2, h - t / 2 })
                    foreach (float z in new[] { -h + t / 2, h - t / 2 })
                        b.Box("iron", "iron", x, z, t / 2, t / 2, 0, s);
                foreach (float y in new[] { 0f, s - t }) {
                    foreach (float z in new[] { -h + t / 2, h - t / 2 }) b.Box("iron", "iron", 0, z, h, t / 2, y, y + t);
                    foreach (float x in new[] { -h + t / 2, h - t / 2 }) b.Box("iron", "iron", x, 0, t / 2, h, y, y + t);
                }
                for (int i = 1; i <= 3; i++) {
                    float o = -h + i * s / 4f;
                    foreach (float z in new[] { -h + bar, h - bar }) b.Box("iron", "iron", o, z, bar, bar, 0, s);
                    foreach (float x in new[] { -h + bar, h - bar }) b.Box("iron", "iron", x, o, bar, bar, 0, s);
                    // and across the top
                    b.Box("iron", "iron", o, 0, bar, h, s - bar * 2, s);
                }
                foreach (float y in new[] { s * 0.33f, s * 0.66f }) {
                    foreach (float z in new[] { -h + bar, h - bar }) b.Box("iron", "iron", 0, z, h, bar, y - bar, y + bar);
                    foreach (float x in new[] { -h + bar, h - bar }) b.Box("iron", "iron", x, 0, bar, h, y - bar, y + bar);
                }
                break;
            }
            case BlockLook.Bush: {
                // a clump of leafy balls
                (float X, float Y, float Z, float R)[] balls = [
                    (0f, 0.42f, 0f, 0.5f), (-0.3f, 0.3f, 0.2f, 0.36f), (0.32f, 0.3f, 0.16f, 0.36f), (0.12f, 0.3f, -0.32f, 0.36f),
                    (-0.22f, 0.3f, -0.26f, 0.32f), (0.06f, 0.66f, 0.08f, 0.34f), (-0.2f, 0.56f, -0.04f, 0.28f), (0.26f, 0.54f, -0.1f, 0.27f),
                ];
                foreach (var (x, y, z, r) in balls)
                    b.Ball("leaves", new Vector3(x * s, y * s, z * s), r * s, r * s * 0.92f, r * s);
                break;
            }
            case BlockLook.Mud:
            case BlockLook.IceFloor:
            case BlockLook.Lava: {
                string tex = look == BlockLook.Mud ? "mud" : look == BlockLook.IceFloor ? "iceFloor" : $"lava{stage}";
                float y = look == BlockLook.Mud ? 0.5f : 0.35f;
                float r = look == BlockLook.Mud ? h * 1.12f : h;
                b.Quad(tex, new(-r, y, -r), new(r, y, -r), new(r, y, r), new(-r, y, r));
                break;
            }
            case BlockLook.Conveyor:
                // (the belt itself is added each frame, so its arrows can roll; these are the rails)
                foreach (float x in new[] { -h + s * 0.04f, h - s * 0.04f })
                    b.Box("darkSteel", "darkSteel", x, 0, s * 0.04f, h, 0, s * 0.09f);
                break;
            case BlockLook.Gate:
                // two stone posts (the bars between them are added each frame, at their height)
                foreach (float x in new[] { -h + s * 0.13f, h - s * 0.13f })
                    b.Box("stone", "stone", x, 0, s * 0.13f, s * 0.2f, 0, s * 1.08f);
                break;
            case BlockLook.Spikes:
                b.Quad("spikePlate", new(-h, 0.4f, -h), new(h, 0.4f, -h), new(h, 0.4f, h), new(-h, 0.4f, h));
                break;
            case BlockLook.Magnet: {
                // a horseshoe magnet standing on a steel base: two poles joined across the back
                b.Box("darkSteel", "darkSteel", 0, 0, h, h, 0, s * 0.3f);
                foreach (var (x, tex) in new[] { (-0.24f, "magnetRed"), (0.24f, "magnetBlue") }) {
                    b.Box(tex, tex, x * s, -s * 0.06f, s * 0.13f, s * 0.2f, s * 0.3f, s * 0.86f);
                    b.Box("magnetTip", "magnetTip", x * s, -s * 0.06f, s * 0.135f, s * 0.205f, s * 0.86f, s * 1.04f);
                }
                b.Box("magnetRed", "magnetRed", -0.06f * s, -s * 0.31f, s * 0.19f, s * 0.07f, s * 0.3f, s * 0.62f);
                b.Box("magnetBlue", "magnetBlue", 0.19f * s, -s * 0.31f, s * 0.07f + s * 0.11f, s * 0.07f, s * 0.3f, s * 0.62f);
                break;
            }
        }
        return _meshes[key] = b.Take();
    }

    // ------------------------------------------------------------------ drawing

    private static BasicEffect? _fx;
    private static readonly Builder _scratch = new();

    /// <summary>True for looks you can partly see through (draw these after the solid things).</summary>
    public static bool IsSeeThrough(BlockLook look) => look is BlockLook.Glass or BlockLook.Ice or BlockLook.IceFloor or BlockLook.Mud;

    /// <summary>The light a block gives off, if any (color, strength 0..2, how far in world units).</summary>
    public static (Color Color, float Intensity, float Range)? GlowOf(BlockLook look, float time) => look switch {
        BlockLook.Lava => (new Color(255, 130, 40), 1.3f + 0.25f * MathF.Sin(time * 2.2f), 80f),
        BlockLook.Magnet => (new Color(150, 120, 255), 0.5f + 0.3f * MathF.Sin(time * 4f), 55f),
        _ => null,
    };

    /// <summary>
    /// Draws one block standing on the floor at <paramref name="position"/> (its centre; y is the floor).
    /// </summary>
    /// <param name="state">What it means depends on the look (see <see cref="BlockLook"/>): damage, how open, how far out.</param>
    /// <param name="time">Seconds, for the looks that move (the conveyor's arrows, lava, the bush's sway, the magnet's pulse).</param>
    /// <param name="yaw">Turns the block (the conveyor's direction, which way the mirror or the gate faces).</param>
    public static void Draw(GraphicsDevice gd, BlockLook look, Vector3 position, float state, float time, Matrix view, Matrix projection, float yaw = 0f) {
        _fx ??= new BasicEffect(gd) { TextureEnabled = true };
        var fx = _fx;
        fx.View = view;
        fx.Projection = projection;
        fx.Alpha = 1f;
        fx.SetDefaultGameLighting_IngameEntities(0.9f);
        fx.DirectionalLight1.Enabled = true;
        fx.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(-0.5f, -0.35f, -0.8f));
        fx.DirectionalLight1.DiffuseColor = new Vector3(0.4f);
        fx.DirectionalLight1.SpecularColor = Vector3.Zero;
        fx.EmissiveColor = Vector3.Zero;

        var oldRaster = gd.RasterizerState;
        var oldBlend = gd.BlendState;
        var oldDepth = gd.DepthStencilState;
        var oldSampler = gd.SamplerStates[0];
        gd.RasterizerState = RasterizerState.CullNone;
        gd.SamplerStates[0] = SamplerState.LinearWrap;
        bool seeThrough = IsSeeThrough(look);
        gd.BlendState = seeThrough ? BlendState.NonPremultiplied : BlendState.Opaque;
        gd.DepthStencilState = seeThrough ? DepthStencilState.DepthRead : DepthStencilState.Default;

        state = MathHelper.Clamp(state, 0f, 1f);
        int stage = look switch {
            BlockLook.RottenWood => state >= 0.5f ? 1 : 0,
            BlockLook.Ice => state >= 0.75f ? 2 : state >= 0.25f ? 1 : 0,
            BlockLook.Lava => (int)(time * 1.5f) % 4,
            _ => 0,
        };
        var world = Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(position);
        if (look == BlockLook.Bush) // it stirs in the breeze
            world = Matrix.CreateScale(1f + 0.03f * MathF.Sin(time * 1.7f + position.X), 1f + 0.025f * MathF.Sin(time * 2.3f + position.Z), 1f + 0.03f * MathF.Cos(time * 1.9f)) * world;
        if (look == BlockLook.Lava)
            fx.EmissiveColor = new Vector3(0.55f + 0.1f * MathF.Sin(time * 2.2f), 0.3f, 0.12f);
        if (look is BlockLook.Glass or BlockLook.Ice)
            fx.EmissiveColor = new Vector3(0.12f); // (so it stays bright where the light doesn't reach)

        void Parts(List<Part> parts, Matrix w) {
            fx.World = w;
            foreach (var part in parts) {
                if (part.Verts.Length == 0)
                    continue;
                fx.Texture = Texture(gd, part.Texture, _painters[part.Texture]);
                foreach (var pass in fx.CurrentTechnique.Passes) {
                    pass.Apply();
                    gd.DrawUserPrimitives(PrimitiveType.TriangleList, part.Verts, 0, part.Verts.Length / 3);
                }
            }
        }

        Parts(Mesh(look, stage), world);

        // the parts that move
        float h = Size / 2f, s = Size;
        switch (look) {
            case BlockLook.Conveyor: {
                // the belt, its arrows rolling toward +z
                float roll = -Frac(time * 0.8f);
                _scratch.Quad("belt", new(-h, 0.6f, -h), new(h, 0.6f, -h), new(h, 0.6f, h), new(-h, 0.6f, h), 0, roll, 1, roll + 1);
                Parts(_scratch.Take(), world);
                break;
            }
            case BlockLook.Gate: {
                // the bars: up when shut, sunk into the floor when open (their points still show)
                float top = s * (1f - 0.9f * state);
                for (int i = 0; i < 4; i++) {
                    float x = (-0.3f + i * 0.2f) * s;
                    _scratch.Cylinder("iron", "iron", x, 0, s * 0.035f, s * 0.035f, 0, top - s * 0.1f, 8);
                    _scratch.Cylinder("iron", "iron", x, 0, s * 0.05f, 0f, top - s * 0.1f, top + s * 0.04f, 8); // a point
                }
                if (state < 0.85f)
                    foreach (float y in new[] { 0.3f, 0.7f })
                        if (top > s * (y + 0.12f))
                            _scratch.Box("iron", "iron", 0, 0, s * 0.37f, s * 0.03f, top - s * (1f - y) - s * 0.03f, top - s * (1f - y) + s * 0.03f);
                Parts(_scratch.Take(), world);
                break;
            }
            case BlockLook.Spikes: {
                // nine steel spikes, pushed up through their holes
                float up = s * (0.05f + 0.6f * state);
                for (int ix = 0; ix < 3; ix++)
                    for (int iz = 0; iz < 3; iz++)
                        _scratch.Cylinder("iron", "iron", (ix - 1) * s / 3f, (iz - 1) * s / 3f, s * 0.085f, 0f, 0.4f, 0.4f + up, 8);
                Parts(_scratch.Take(), world);
                break;
            }
        }

        fx.EmissiveColor = Vector3.Zero;
        gd.RasterizerState = oldRaster;
        gd.BlendState = oldBlend;
        gd.DepthStencilState = oldDepth;
        gd.SamplerStates[0] = oldSampler;
    }
}
