using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// Paints tank skins onto the unwrapped tank (<see cref="TankSkinMesh"/>). Every texel knows where on the tank it is,
/// so a skin is a rule for "what color is the paint here": camouflage is solid noise that wraps around corners, and
/// the fittings every tank has (tracks and road wheels, an engine grille, hatches, headlights, the gun's collar and
/// muzzle) are placed by position. Each skin adds its own paint scheme and marking on top.
/// </summary>
public static class TankPaint {
    /// <summary>How a tank is painted.</summary>
    public enum Scheme {
        // camouflage
        Blotch, SoftBlotch, Outlined, Whitewash, TigerStripes, Plain, Bands,
        // showpieces
        Rainbow, Neon, Gold, Chrome,
        // boss looks
        Sailor, Dots, Hazard, Cracks, Scales, Candy, Specks, Plates, Halves, Circuit, Hex, Diamonds, Rust, Flames, Rings, Wisps, Coins,
    }

    public enum Mark { None, WhiteStar, RedStar, Cross, IronCross, Sun, Flash, Chevron, Diamond, Card, Square, Skull, Crown, Bolt, Anchor, Rank, Eye }

    /// <summary>A whole paint job.</summary>
    public sealed record Design(Scheme Scheme, Color A, Color B, Color C, Color D, Mark Mark = Mark.None, Color? MarkColor = null,
        float Scale = 1f, Color? Barrel = null, Color? Wheels = null, Color? Trim = null, Color? Glow = null) {
        /// <summary>True if the paint moves (redrawn as time passes).</summary>
        public bool Animated => Scheme is Scheme.Rainbow or Scheme.Neon or Scheme.Gold or Scheme.Chrome or Scheme.Cracks or Scheme.Circuit or Scheme.Wisps or Scheme.Flames or Scheme.Coins;
    }

    private enum Region : byte { None, Under, Track, FenderTop, Skirt, Deck, Front, Rear, TurretTop, TurretSide, Barrel, Muzzle }

    // ------------------------------------------------------------------ noise

    private static float Hash(int x, int y, int z) {
        uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483629);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
    }

    /// <summary>Smooth solid noise, 0..1.</summary>
    private static float Noise(Vector3 p) {
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
        float fx = p.X - x, fy = p.Y - y, fz = p.Z - z;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float a = MathHelper.Lerp(MathHelper.Lerp(Hash(x, y, z), Hash(x + 1, y, z), fx), MathHelper.Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), fx), fy);
        float b = MathHelper.Lerp(MathHelper.Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), fx), MathHelper.Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), fx), fy);
        return MathHelper.Lerp(a, b, fz);
    }

    private static float Fbm(Vector3 p) => Noise(p) * 0.65f + Noise(p * 2.13f + new Vector3(17.1f, 3.7f, 9.2f)) * 0.35f;

    private static float Step(float edge0, float edge1, float x) => MathHelper.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
    private static float Frac(float x) => x - MathF.Floor(x);
    private static Vector3 V(Color c) => c.ToVector3();
    private static Vector3 Mix(Vector3 a, Vector3 b, float t) => a + (b - a) * MathHelper.Clamp(t, 0f, 1f);

    // ------------------------------------------------------------------ where on the tank

    private static Region RegionOf(Vector3 p, Vector3 n, int part, bool player) {
        if (part == TankSkinMesh.CannonPart) {
            if (p.Z > 6.0f)
                return p.Z > (player ? 15.4f : 16.4f) ? Region.Muzzle : Region.Barrel;
            return n.Y > 0.6f ? Region.TurretTop : Region.TurretSide;
        }
        if (n.Y < -0.93f)
            return Region.Under;
        if (MathF.Abs(p.X) > 6.75f) {
            if (n.Y > 0.6f)
                return Region.FenderTop;
            // (the enemies' tank has plain slanted sides: the lower slant is where its tracks are painted)
            return p.Y < (player ? 5.7f : 4.05f) ? Region.Track : Region.Skirt;
        }
        if (n.Y < -0.6f)
            return Region.Under;
        if (n.Y > 0.5f)
            return Region.Deck;
        return n.Z < -0.3f ? Region.Rear : Region.Front;
    }

    // ------------------------------------------------------------------ the paint itself

    private static Vector3 Hsv(float h, float s, float v) => PlayerColors.HsvToColor(h, s, v).ToVector3();

    /// <summary>The body paint at a point (before fittings and markings).</summary>
    private static Vector3 Body(Design d, Vector3 p, Vector3 n, Region r, float t) {
        Vector3 a = V(d.A), b = V(d.B), c = V(d.C), dd = V(d.D);
        float s = d.Scale;
        switch (d.Scheme) {
            case Scheme.Plain:
                return a;
            case Scheme.Blotch: {
                // hard-edged patches of up to four colors
                float f = Fbm(p * 0.17f * s + new Vector3(3.1f, 7.7f, 1.3f));
                float g = Fbm(p * 0.21f * s + new Vector3(40.5f, 12.2f, 8.8f));
                var col = f > 0.56f ? b : a;
                if (g > 0.6f) col = c;
                if (d.D != d.A && Fbm(p * 0.3f * s + new Vector3(9.9f, 31f, 4f)) > 0.7f) col = dd;
                return col;
            }
            case Scheme.SoftBlotch: {
                // sprayed on: the patches fade into each other
                float f = Fbm(p * 0.16f * s + new Vector3(5.5f, 2.2f, 8.1f));
                float g = Fbm(p * 0.19f * s + new Vector3(21.5f, 18.2f, 3.3f));
                var col = Mix(a, b, Step(0.5f, 0.62f, f));
                return Mix(col, c, Step(0.56f, 0.68f, g));
            }
            case Scheme.Outlined: {
                // brushed patches with a dark line between them
                float f = Fbm(p * 0.15f * s + new Vector3(1.1f, 5.3f, 2.9f));
                float g = Fbm(p * 0.15f * s + new Vector3(31.4f, 15.9f, 26.5f));
                Vector3 col;
                float edge;
                if (f > 0.55f) { col = b; edge = f - 0.55f; }
                else if (g > 0.55f) { col = c; edge = MathF.Min(g - 0.55f, 0.55f - f); }
                else { col = a; edge = MathF.Min(0.55f - f, 0.55f - g); }
                return edge < 0.018f ? dd : col;
            }
            case Scheme.Whitewash: {
                // white brushed over the factory color, worn away low down and in patches
                float wear = Fbm(p * 0.33f + new Vector3(2f, 9f, 4f)) + Noise(new Vector3(p.X * 1.5f, p.Y * 0.25f, p.Z * 1.5f)) * 0.25f;
                float low = p.Y < 6f ? (6f - p.Y) / 14f : 0f;
                return Mix(a, b, Step(0.62f, 0.76f, wear + low)) * (0.96f + 0.04f * Noise(p * 2.4f));
            }
            case Scheme.TigerStripes: {
                // dark stripes wandering across an orange coat, pale underneath
                float wob = Noise(new Vector3(p.X * 0.35f, p.Y * 0.35f, p.Z * 0.12f)) * 5f;
                float stripe = MathF.Abs(Frac((p.Z + wob) / 4.2f) - 0.5f) * 2f; // 0 at a stripe's middle
                float width = 0.3f + 0.25f * Noise(new Vector3(p.X * 0.3f, p.Y * 0.4f, 7f));
                var coat = Mix(a, c, Step(4.5f, 1.5f, p.Y) * 0.6f);
                return Mix(b, coat, Step(width - 0.07f, width + 0.07f, stripe));
            }
            case Scheme.Bands: {
                // long diagonal bands of three colors
                float k = Frac((p.Z + p.X * 0.7f + p.Y * 0.5f + Noise(p * 0.2f) * 4f) / (16f / s));
                return k < 0.42f ? a : k < 0.72f ? b : c;
            }
            case Scheme.Rainbow:
                return Hsv(t * 0.22f + p.Z * 0.022f + p.X * 0.015f + p.Y * 0.02f, 0.72f, 1f) * (0.86f + 0.14f * n.Y);
            case Scheme.Neon: {
                // a dark hull with lines of light running along it
                var hull = a * (0.85f + 0.3f * Noise(p * 0.6f));
                float lineZ = MathF.Abs(Frac(p.Z / 4.6f) - 0.5f), lineX = MathF.Abs(Frac((p.X + 0.4f) / 4.6f) - 0.5f), lineY = MathF.Abs(Frac((p.Y + 1f) / 3.2f) - 0.5f);
                float line = MathF.Min(MathF.Abs(n.Z) > 0.6f ? 1f : lineZ, MathF.Min(MathF.Abs(n.X) > 0.6f ? 1f : lineX, MathF.Abs(n.Y) > 0.6f ? 1f : lineY));
                float pulse = 0.55f + 0.45f * MathF.Sin(p.Z * 0.45f - t * 5f);
                var light = Mix(b, c, 0.5f + 0.5f * MathF.Sin(p.Z * 0.2f + p.X * 0.2f + t * 1.4f));
                return hull + light * (Step(0.07f, 0.02f, line) * (0.45f + 0.55f * pulse) + Step(0.2f, 0.0f, line) * 0.12f);
            }
            case Scheme.Gold:
            case Scheme.Chrome: {
                // polished metal: it mirrors a made-up sky, horizon and ground, with a glint sweeping across
                var m = Vector3.Normalize(n + new Vector3(Noise(p * 0.5f) - 0.5f, 0, Noise(p * 0.5f + new Vector3(9f)) - 0.5f) * 0.12f);
                float up = m.Y, side = MathF.Atan2(m.X, m.Z) + t * 0.25f;
                var sky = Mix(new Vector3(0.62f, 0.72f, 0.88f), new Vector3(1f, 1f, 1f), Step(0.5f, 1f, up));
                var ground = Mix(new Vector3(0.2f, 0.17f, 0.15f), new Vector3(0.48f, 0.42f, 0.36f), 0.5f + 0.5f * MathF.Sin(side * 3f));
                var env = up > 0.12f ? sky : up > -0.02f ? new Vector3(0.1f, 0.1f, 0.12f) : ground;
                float glint = MathF.Pow(MathF.Max(0f, MathF.Sin((p.Z + p.X * 0.6f + p.Y) * 0.16f - t * 1.6f)), 24f);
                if (d.Scheme == Scheme.Chrome)
                    return env * 0.78f + new Vector3(glint * 0.5f);
                // gold tints the reflection
                return env * a * 0.95f + a * 0.16f + new Vector3(1f, 0.92f, 0.7f) * glint * 0.6f;
            }

            // ---- boss looks
            case Scheme.Sailor: {
                // navy with white hoops around the turret and along the skirts
                bool hoop = (r is Region.TurretSide or Region.Skirt or Region.Barrel) && Frac((r == Region.Barrel ? p.Z : p.Y) / 1.9f) < 0.36f;
                return hoop ? b : a;
            }
            case Scheme.Dots: {
                var cell = new Vector3(Frac(p.X / 4f) - 0.5f, Frac(p.Y / 4f) - 0.5f, Frac(p.Z / 4f) - 0.5f) * 4f;
                float dist = MathF.Abs(n.Y) > 0.6f ? MathF.Sqrt(cell.X * cell.X + cell.Z * cell.Z) : MathF.Abs(n.X) > 0.6f ? MathF.Sqrt(cell.Z * cell.Z + cell.Y * cell.Y) : MathF.Sqrt(cell.X * cell.X + cell.Y * cell.Y);
                return dist < 1.05f ? b : a;
            }
            case Scheme.Hazard: {
                // work-site yellow and black chevrons on the skirts and turret, dirt-brown elsewhere
                if (r is Region.Skirt or Region.TurretSide or Region.Front or Region.Rear)
                    return Frac((p.Z + p.X + p.Y * 1.2f) / 3.4f) < 0.5f ? b : c;
                return a * (0.88f + 0.2f * Noise(p * 0.5f));
            }
            case Scheme.Cracks: {
                // a dark shell split by glowing cracks
                float f = Fbm(p * 0.26f * s + new Vector3(4.4f, 1.2f, 6.6f));
                float crack = MathF.Abs(f - 0.5f);
                float glow = Step(0.035f, 0.008f, crack) * (0.7f + 0.3f * MathF.Sin(t * 3f + p.Z));
                return a * (0.8f + 0.4f * Noise(p * 0.9f)) + b * glow + b * Step(0.09f, 0.0f, crack) * 0.18f;
            }
            case Scheme.Scales: {
                // rows of overlapping scales, lighter on the belly
                float u = MathF.Abs(n.Y) > 0.6f ? p.X : MathF.Abs(n.X) > 0.6f ? p.Y * 1.3f : p.X, v = MathF.Abs(n.Z) > 0.6f ? p.Y * 1.3f : p.Z;
                float row = MathF.Floor(v / 1.7f);
                float cu = Frac(u / 2.2f + (row % 2 == 0 ? 0f : 0.5f)) - 0.5f, cv = Frac(v / 1.7f);
                float rim = cu * cu * 4f + (1f - cv) * (1f - cv);
                var scale = Mix(a, b, cv * 0.7f);
                var col = rim > 0.92f ? a * 0.45f : scale;
                return Mix(col, c, Step(3.2f, 1.2f, p.Y) * (r is Region.Skirt or Region.Front or Region.Rear ? 0.8f : 0f));
            }
            case Scheme.Candy: {
                // glossy swirls
                float k = Frac((p.Z * 0.9f + MathF.Sin(p.X * 0.5f + p.Y * 0.4f) * 2.2f + p.Y) / 5.5f);
                var col = k < 0.5f ? a : b;
                float gloss = MathF.Pow(MathF.Max(0f, n.Y * 0.7f + 0.3f), 6f) * 0.25f + MathF.Pow(MathF.Max(0f, MathF.Sin(p.X * 0.6f + p.Z * 0.3f + 1f)), 30f) * 0.5f;
                return col + new Vector3(gloss);
            }
            case Scheme.Specks: {
                float sp = Noise(p * 1.9f);
                var col = r is Region.TurretTop or Region.TurretSide && p.Y > 12.2f ? c : a;
                return sp > 0.78f ? b : col;
            }
            case Scheme.Plates: {
                // thick armor plates with seams and rivets
                float u = MathF.Abs(n.X) > 0.6f ? p.Z : p.X, v = MathF.Abs(n.Y) > 0.6f ? p.Z : p.Y;
                float pu = Frac(u / 5.2f), pv = Frac(v / (MathF.Abs(n.Y) > 0.6f ? 5.2f : 3.4f));
                float seam = MathF.Min(MathF.Min(pu, 1 - pu) * 5.2f, MathF.Min(pv, 1 - pv) * 3.4f);
                var col = a * (0.9f + 0.16f * Hash((int)MathF.Floor(u / 5.2f), (int)MathF.Floor(v / 3.4f), (int)(n.X * 2 + n.Y * 5)));
                if (seam < 0.16f)
                    return a * 0.5f;
                // a rivet in each corner
                float ru = MathF.Min(pu, 1 - pu) * 5.2f - 0.6f, rv = MathF.Min(pv, 1 - pv) * 3.4f - 0.6f;
                if (ru * ru + rv * rv < 0.085f)
                    return b;
                return col;
            }
            case Scheme.Halves:
                // one end one color, the other end the other (a magnet), bare metal between
                return MathF.Abs(p.Z) < 1.1f ? c : p.Z > 0 ? a : b;
            case Scheme.Circuit: {
                // dark metal with glowing traces that turn corners
                float u = MathF.Abs(n.X) > 0.6f ? p.Z : p.X, v = MathF.Abs(n.Y) > 0.6f ? p.Z : p.Y;
                int cx = (int)MathF.Floor(u / 2.6f), cy = (int)MathF.Floor(v / 2.6f);
                float fu = Frac(u / 2.6f), fv = Frac(v / 2.6f);
                bool horiz = Hash(cx, cy, 5) > 0.5f;
                float line = horiz ? MathF.Abs(fv - 0.5f) : MathF.Abs(fu - 0.5f);
                bool node = (fu - 0.5f) * (fu - 0.5f) + (fv - 0.5f) * (fv - 0.5f) < 0.02f && Hash(cx, cy, 9) > 0.6f;
                float pulse = 0.6f + 0.4f * MathF.Sin(t * 4f + cx * 1.3f + cy * 0.7f);
                var hull = a * (0.85f + 0.25f * Noise(p * 0.7f));
                if (node) return b * (0.7f + 0.3f * pulse) + hull * 0.3f;
                return hull + b * Step(0.07f, 0.03f, line) * pulse * 0.9f;
            }
            case Scheme.Hex: {
                // honeycomb cells
                float u = (MathF.Abs(n.X) > 0.6f ? p.Z : p.X) / (2.4f * s), v = (MathF.Abs(n.Y) > 0.6f ? p.Z : p.Y) / (2.08f * s);
                float row = MathF.Floor(v);
                float fu = Frac(u + (row % 2 == 0 ? 0f : 0.5f)) - 0.5f, fv = Frac(v) - 0.5f;
                float hex = MathF.Max(MathF.Abs(fu), MathF.Abs(fu) * 0.5f + MathF.Abs(fv) * 0.87f);
                float fill = Hash((int)MathF.Floor(u + (row % 2 == 0 ? 0f : 0.5f)), (int)row, 3);
                return hex > 0.43f ? b : Mix(a, c, fill > 0.72f ? 0.7f : fill * 0.25f);
            }
            case Scheme.Diamonds: {
                // a jester's diamonds
                float u = (MathF.Abs(n.X) > 0.6f ? p.Z : p.X) / 3.2f, v = (MathF.Abs(n.Y) > 0.6f ? p.Z : p.Y) / 3.2f;
                int k = (int)MathF.Floor(u + v) + (int)MathF.Floor(u - v);
                return (k & 1) == 0 ? a : b;
            }
            case Scheme.Rust: {
                // battleship grey, streaked with rust running down from the seams
                float streak = Noise(new Vector3(p.X * 1.3f, p.Y * 0.18f, p.Z * 1.3f));
                float patch = Fbm(p * 0.3f);
                var col = a * (0.9f + 0.14f * Noise(p * 0.8f));
                col = Mix(col, b, Step(0.6f, 0.82f, streak) * Step(13f, 3f, p.Y) * 0.85f);
                return Mix(col, c, Step(0.66f, 0.74f, patch) * 0.7f);
            }
            case Scheme.Flames: {
                // flames licking up from the bottom and back along the sides
                float h = (p.Y - (r is Region.TurretSide or Region.TurretTop ? 7.6f : 0f)) / 6.5f + (r is Region.Deck or Region.FenderTop or Region.TurretTop ? (11.8f - p.Z) / 26f : 0f);
                float f = h + (Fbm(new Vector3(p.X * 0.5f, p.Y * 0.22f - t * 0.25f, p.Z * 0.5f)) - 0.5f) * 1.5f;
                return f < 0.32f ? dd : f < 0.52f ? c : f < 0.72f ? b : a;
            }
            case Scheme.Rings: {
                // clock faces: rings and ticks
                float u = MathF.Abs(n.X) > 0.6f ? p.Z : p.X, v = MathF.Abs(n.Y) > 0.6f ? p.Z : p.Y - 5f;
                float rad = MathF.Sqrt(u * u + v * v), ang = MathF.Atan2(u, v);
                bool ring = Frac(rad / 3.3f) < 0.14f;
                bool tick = Frac(ang / MathHelper.TwoPi * 12f) < 0.1f && Frac(rad / 3.3f) > 0.62f;
                var col = a * (0.9f + 0.2f * Noise(p * 0.4f));
                return ring ? b : tick ? c : col;
            }
            case Scheme.Wisps: {
                // pale, with slow smoke drifting through it
                float f = Fbm(new Vector3(p.X * 0.2f + t * 0.12f, p.Y * 0.25f, p.Z * 0.2f - t * 0.08f));
                return Mix(a, b, Step(0.35f, 0.7f, f)) + c * Step(0.74f, 0.9f, f) * 0.5f;
            }
            case Scheme.Coins: {
                // gold, studded with coins
                var cell = new Vector3(Frac(p.X / 3.3f) - 0.5f, Frac(p.Y / 3.3f) - 0.5f, Frac(p.Z / 3.3f) - 0.5f) * 3.3f;
                float dist = MathF.Abs(n.Y) > 0.6f ? MathF.Sqrt(cell.X * cell.X + cell.Z * cell.Z) : MathF.Abs(n.X) > 0.6f ? MathF.Sqrt(cell.Z * cell.Z + cell.Y * cell.Y) : MathF.Sqrt(cell.X * cell.X + cell.Y * cell.Y);
                var gold = a * (0.85f + 0.3f * MathF.Max(0f, n.Y)) + new Vector3(MathF.Pow(MathF.Max(0f, MathF.Sin((p.Z + p.X) * 0.2f - t * 1.5f)), 20f) * 0.4f);
                return dist < 1.15f ? (dist > 0.9f ? b * 0.7f : dist < 0.35f ? b * 0.8f : b) : gold;
            }
        }
        return a;
    }

    /// <summary>The fittings every tank has, drawn over the body paint.</summary>
    private static Vector3 Fittings(Design d, Vector3 col, Vector3 body, Vector3 p, Vector3 n, Region r, bool player, float t) {
        var wheelCol = V(d.Wheels ?? new Color(92, 94, 98));
        var trim = d.Trim is { } tc ? V(tc) : body * 0.55f;
        float ax = MathF.Abs(p.X);
        switch (r) {
            case Region.Under:
                return new Vector3(0.09f, 0.085f, 0.085f);
            case Region.Track: {
                var rubber = new Vector3(0.2f, 0.195f, 0.2f);
                // (the enemies' tracks are painted on a slant that faces the floor and gets little light: paint them brighter)
                float lit = player ? 1f : 1.2f;
                wheelCol *= lit;
                rubber *= lit;
                float topRun = player ? 4.75f : 3.45f, bottomRun = player ? 1.0f : 0.6f;
                if (MathF.Abs(n.X) > 0.3f) {
                    // seen from the side: three road wheels, a sprocket and an idler, inside the track's loop
                    float best = 99f, bestR = 1f;
                    foreach (var (z, y, rad) in player ? _wheels : _wheelsEnemy) {
                        float dz = p.Z - z, dy = p.Y - y;
                        float dist = MathF.Sqrt(dz * dz + dy * dy) / rad;
                        if (dist < best) { best = dist; bestR = rad; }
                    }
                    if (best < 1f) {
                        float rr = best * bestR;
                        if (rr < bestR * 0.24f) return wheelCol * 2.1f;                          // hub cap
                        if (rr > bestR * 0.8f) return new Vector3(0.16f, 0.16f, 0.17f) * lit;            // tire
                        // the wheel's disc, with a ring of bolts
                        float ang = MathF.Atan2(p.Y - (player ? 2.85f : 2f), (Frac(p.Z / 7.3f + 0.5f) - 0.5f) * 7.3f);
                        bool bolt = rr > bestR * 0.46f && rr < bestR * 0.62f && Frac(ang / MathHelper.TwoPi * 6f) < 0.3f;
                        return bolt ? wheelCol * 0.7f : wheelCol * (1.25f + 0.4f * (1f - best));
                    }
                    // the track's links, top and bottom runs
                    if (p.Y > topRun || p.Y < bottomRun)
                        return Frac(p.Z / 1.05f) < 0.28f ? rubber * 0.55f : rubber * 1.55f;
                    return new Vector3(0.11f, 0.105f, 0.105f) * lit; // the shadow behind the wheels
                }
                // the track seen end-on or from above: its links
                return Frac((p.Y + p.Z) / 1.05f) < 0.28f ? rubber * 0.55f : rubber * 1.5f;
            }
            case Region.FenderTop: {
                // the mudguard: a seam where it meets the hull, a lip at the outside, the track showing at the ends
                if (ax < 6.98f) return col * 0.5f;
                if (MathF.Abs(p.Z) > 10.2f)
                    return Frac(p.Z / 0.9f) < 0.3f ? new Vector3(0.08f) : new Vector3(0.2f, 0.195f, 0.2f);
                if (ax > 9.55f) return col * 0.72f;
                float seam = MathF.Abs(Frac(p.Z / 5.6f + 0.5f) - 0.5f) * 5.6f;
                return seam < 0.1f ? col * 0.62f : col * 0.94f;
            }
            case Region.Skirt: {
                // side armor in panels, a rivet at each panel's corners
                float pz = Frac(p.Z / 4.7f + 0.5f);
                float seam = MathF.Min(pz, 1 - pz) * 4.7f;
                if (seam < 0.11f) return col * 0.55f;
                float rz = seam - 0.55f, ry = MathF.Min(MathF.Abs(p.Y - 6.15f), MathF.Abs(p.Y - 7.35f));
                if (rz * rz + ry * ry < 0.035f) return col * 0.6f + new Vector3(0.12f);
                return col * 0.9f;
            }
            case Region.Deck: {
                // rear: the engine grille; front: the driver's hatch; a darker ring where the turret sits
                if (p.Z < -6.6f && p.Z > -10.9f && ax < 4.7f) {
                    bool frame = p.Z > -6.85f || p.Z < -10.65f || ax > 4.45f || ax < 0.18f;
                    if (frame) return trim;
                    return Frac(p.Z / 0.62f) < 0.42f ? new Vector3(0.06f) : trim * 1.25f;
                }
                if (p.Z > 6.9f && p.Z < 10.1f && ax < 2.5f) {
                    bool edge = p.Z < 7.12f || p.Z > 9.88f || ax > 2.28f;
                    if (edge) return col * 0.5f;
                    float hd = MathF.Sqrt(p.X * p.X + (p.Z - 8.5f) * (p.Z - 8.5f));
                    return hd < 0.42f ? col * 0.5f : col * 1.07f;
                }
                // fuel caps beside the grille
                foreach (float fx in _capX) {
                    float cd = MathF.Sqrt((p.X - fx) * (p.X - fx) + (p.Z + 8.7f) * (p.Z + 8.7f));
                    if (cd < 0.62f) return cd > 0.45f ? col * 0.5f : new Vector3(0.62f, 0.2f, 0.16f);
                }
                float ring = MathF.Max(ax, MathF.Abs(p.Z));
                if (ring < 6.1f) return col * 0.78f;
                return col;
            }
            case Region.Front: {
                // headlights, and a tow hook in the middle
                foreach (float hx in _lampX) {
                    float hd = MathF.Sqrt((p.X - hx) * (p.X - hx) + (p.Y - 5.9f) * (p.Y - 5.9f));
                    if (hd < 0.95f) return hd > 0.7f ? new Vector3(0.12f) : Mix(new Vector3(1f, 0.97f, 0.8f), new Vector3(1f, 0.85f, 0.45f), hd / 0.7f);
                }
                if (ax < 0.5f && p.Y > 2.6f && p.Y < 3.9f) return new Vector3(0.13f);
                return col * (p.Y < 2.4f ? 0.8f : 0.97f);
            }
            case Region.Rear: {
                // exhausts and tail lights
                foreach (float ex in _exhaustX) {
                    float ed = MathF.Sqrt((p.X - ex) * (p.X - ex) + (p.Y - 5.6f) * (p.Y - 5.6f));
                    if (ed < 0.9f) return ed > 0.62f ? new Vector3(0.32f, 0.3f, 0.3f) : new Vector3(0.04f);
                }
                foreach (float lx in _lampX) {
                    if (MathF.Abs(p.X - lx) < 0.6f && MathF.Abs(p.Y - 6.7f) < 0.36f) return new Vector3(0.85f, 0.12f, 0.1f);
                }
                return col * (p.Y < 2.4f ? 0.78f : 0.93f);
            }
            case Region.TurretTop: {
                // the commander's hatch, a periscope, an aerial's base
                float hd = MathF.Sqrt((p.X + 1.8f) * (p.X + 1.8f) + (p.Z + 1.3f) * (p.Z + 1.3f));
                if (hd < 2.05f) {
                    if (hd > 1.72f) return col * 0.5f;
                    if (hd < 0.3f) return col * 0.55f;
                    return MathF.Abs(p.X + 1.8f) < 0.09f ? col * 0.7f : col * 1.08f;
                }
                if (MathF.Abs(p.X - 2.6f) < 0.95f && MathF.Abs(p.Z - 2.4f) < 0.5f) return MathF.Abs(p.Z - 2.4f) < 0.25f ? new Vector3(0.25f, 0.4f, 0.5f) : new Vector3(0.14f);
                float ad = MathF.Sqrt((p.X - 3.4f) * (p.X - 3.4f) + (p.Z + 3.6f) * (p.Z + 3.6f));
                if (ad < 0.5f) return ad < 0.2f ? new Vector3(0.75f) : new Vector3(0.12f);
                return col * 1.04f;
            }
            case Region.TurretSide: {
                // the gun's shield at the front, a weld line low down, lifting eyes
                if (n.Z > 0.6f && MathF.Abs(p.X) < 2.6f && p.Y > 9.3f && p.Y < 12.9f)
                    return MathF.Abs(p.X) > 2.35f || p.Y < 9.5f || p.Y > 12.7f ? col * 0.5f : col * 0.8f;
                if (MathF.Abs(p.Y - 8.75f) < 0.09f) return col * 0.6f;
                return col;
            }
            case Region.Barrel: {
                var b = d.Barrel is { } bc ? V(bc) : col * 0.82f;
                if (p.Z < 7.2f) return b * 0.55f;                                 // the collar it slides in
                if (MathF.Abs(p.Z - 10.1f) < 0.42f) return b * 1.25f + new Vector3(0.04f);  // the fume extractor's band
                return b * (0.9f + 0.18f * MathF.Max(0f, n.Y));
            }
            case Region.Muzzle: {
                var steel = new Vector3(0.2f, 0.2f, 0.215f);
                if (n.Z > 0.7f) return new Vector3(0.03f); // looking down the bore
                return Frac(p.Z / 0.95f) < 0.34f ? steel * 0.45f : steel * (1f + 0.5f * MathF.Max(0f, n.Y));
            }
        }
        return col;
    }
    private static readonly (float Z, float Y, float R)[] _wheels = [(-7.3f, 2.85f, 2.55f), (0f, 2.85f, 2.55f), (7.3f, 2.85f, 2.55f), (-10.3f, 3.55f, 1.55f), (10.3f, 3.55f, 1.55f)];
    private static readonly (float Z, float Y, float R)[] _wheelsEnemy = [(-7.3f, 2f, 1.5f), (0f, 2f, 1.5f), (7.3f, 2f, 1.5f), (-10.3f, 2.4f, 1.0f), (10.3f, 2.4f, 1.0f), (-3.65f, 2f, 1.5f), (3.65f, 2f, 1.5f)];
    private static readonly float[] _capX = [-5.6f, 5.6f], _lampX = [-4.9f, 4.9f], _exhaustX = [-3.2f, 3.2f];

    // ------------------------------------------------------------------ markings

    /// <summary>How far inside a five-pointed star (radius 1) a point is; above 0 = inside.</summary>
    private static float Star(float x, float y) {
        float ang = MathF.Atan2(x, y), rad = MathF.Sqrt(x * x + y * y);
        float seg = MathHelper.TwoPi / 5f;
        float a = MathF.Abs(Frac(ang / seg + 0.5f) - 0.5f) * seg; // 0 at a point's tip direction
        // the edge runs from the tip (radius 1) to the notch (radius 0.42) half a segment round
        float edge = 0.42f / MathF.Cos(MathF.Max(0f, seg / 2f - a) - 0.0f) ;
        float tip = MathHelper.Lerp(1f, 0.42f, a / (seg / 2f));
        return MathF.Min(tip, MathF.Max(edge, tip)) - rad;
    }

    /// <summary>A marking on each side of the turret (u along the tank, v up; both about -1..1 across the marking).</summary>
    private static Vector3? Marking(Mark mark, Vector3 markCol, float u, float v, float t) {
        var white = new Vector3(0.94f, 0.94f, 0.9f);
        var black = new Vector3(0.07f);
        float au = MathF.Abs(u), av = MathF.Abs(v), rad = MathF.Sqrt(u * u + v * v);
        switch (mark) {
            case Mark.WhiteStar:
            case Mark.RedStar: {
                float s = Star(u, v);
                if (s > 0f) return mark == Mark.RedStar ? new Vector3(0.8f, 0.1f, 0.09f) : white;
                if (mark == Mark.RedStar && s > -0.13f) return white * 0.95f;
                return null;
            }
            case Mark.Cross: {
                // a black cross edged in white
                bool arm = au < 0.3f && av < 1f || av < 0.3f && au < 1f;
                bool outline = au < 0.46f && av < 1.12f || av < 0.46f && au < 1.12f;
                return arm ? black : outline ? white : null;
            }
            case Mark.IronCross: {
                // arms that flare toward their ends
                float w = 0.16f + 0.34f * MathF.Max(au, av);
                bool arm = (au < w && av < 1f) || (av < w && au < 1f);
                bool outline = (au < w + 0.14f && av < 1.12f) || (av < w + 0.14f && au < 1.12f);
                return arm ? black : outline ? white : null;
            }
            case Mark.Sun: {
                // a red disc on a white flag
                if (au > 1.25f || av > 0.85f) return null;
                return rad < 0.52f ? new Vector3(0.78f, 0.1f, 0.12f) : white;
            }
            case Mark.Flash: {
                // three upright bars: white, red, white
                if (au > 0.95f || av > 0.72f) return null;
                return au < 0.32f ? new Vector3(0.78f, 0.12f, 0.1f) : white;
            }
            case Mark.Chevron: {
                float arm = MathF.Abs(v - (0.55f - au * 1.1f));
                return arm < 0.2f && au < 1f ? markCol : null;
            }
            case Mark.Diamond: {
                float dd = au + av * 0.75f;
                return dd < 0.72f ? null : dd < 0.92f ? markCol : null;
            }
            case Mark.Card: {
                // a white disc with a red heart
                if (rad > 1f) return null;
                float hx = au, hy = v + 0.1f;
                bool heart = (hx - 0.24f) * (hx - 0.24f) + (hy - 0.2f) * (hy - 0.2f) < 0.075f || (hy < 0.24f && hy > -0.52f && hx < (hy + 0.52f) * 0.62f);
                return heart ? new Vector3(0.8f, 0.1f, 0.1f) : white;
            }
            case Mark.Square: {
                if (au > 0.8f || av > 0.8f) return null;
                return au > 0.56f || av > 0.56f ? white : markCol;
            }
            case Mark.Skull: {
                // a skull: a round head, two eyes, a jaw with gaps
                bool head = u * u + (v - 0.18f) * (v - 0.18f) < 0.62f;
                bool jaw = au < 0.42f && v < -0.3f && v > -0.92f;
                if (!head && !jaw) return null;
                bool eye = (au - 0.3f) * (au - 0.3f) + (v - 0.22f) * (v - 0.22f) < 0.045f;
                bool nose = au < 0.08f && v < -0.02f && v > -0.26f;
                bool gap = jaw && v < -0.5f && Frac(u / 0.28f + 0.5f) < 0.26f;
                return eye || nose || gap ? black : markCol;
            }
            case Mark.Crown: {
                // a band with three points
                if (au > 1f) return null;
                float top = 0.05f + 0.75f * MathF.Abs(Frac(au / 0.67f + 0.5f) - 0.5f) * 2f;
                if (v < -0.55f || v > top) return null;
                return v < -0.25f && Frac(u / 0.5f + 0.25f) < 0.3f ? new Vector3(0.8f, 0.12f, 0.2f) : markCol;
            }
            case Mark.Bolt: {
                // a lightning bolt
                float x = u + v * 0.35f;
                bool upper = v > -0.1f && v < 1f && x > -0.32f && x < 0.12f;
                bool lower = v < 0.18f && v > -1f && x > -0.12f + (v + 1f) * 0.0f && x < 0.34f - (0.18f - v) * 0.25f && x > -0.1f + (0.18f - v) * 0.12f;
                return upper || lower ? markCol : null;
            }
            case Mark.Anchor: {
                bool shaft = au < 0.11f && v < 0.62f && v > -0.8f;
                bool bar = av - 0.42f < 0.1f && v > 0.32f && v < 0.52f && au < 0.42f;
                bool ring = MathF.Abs(MathF.Sqrt(u * u + (v - 0.8f) * (v - 0.8f)) - 0.2f) < 0.08f;
                float arc = MathF.Sqrt(u * u + (v + 0.25f) * (v + 0.25f));
                bool hook = MathF.Abs(arc - 0.68f) < 0.11f && v < -0.3f;
                return shaft || bar || ring || hook ? markCol : null;
            }
            case Mark.Rank: {
                // three sergeant's stripes
                for (int i = 0; i < 3; i++) {
                    float arm = MathF.Abs((v + 0.35f - i * 0.48f) - (0.45f - au * 0.75f));
                    if (arm < 0.13f && au < 0.9f) return markCol;
                }
                return null;
            }
            case Mark.Eye: {
                // a watching eye that glows
                float lid = au * au * 0.9f + v * v * 3.2f;
                if (lid > 1f) return null;
                float pupil = MathF.Sqrt(u * u + v * v);
                if (pupil < 0.16f) return black;
                if (pupil < 0.4f) return markCol * (0.8f + 0.4f * MathF.Sin(t * 3f));
                return white;
            }
        }
        return null;
    }

    // ------------------------------------------------------------------ painting a sheet

    private static readonly Dictionary<TankSkinMesh.Sheet, byte[]> _regions = [];

    /// <summary>Paints a design onto a sheet's worth of pixels.</summary>
    public static void Paint(TankSkinMesh mesh, TankSkinMesh.Sheet sheet, Color[] into, Design d, float time) {
        if (!_regions.TryGetValue(sheet, out var regions)) {
            regions = new byte[sheet.Part.Length];
            for (int i = 0; i < regions.Length; i++)
                if (sheet.Part[i] != 0)
                    regions[i] = (byte)RegionOf(sheet.Pos[i], sheet.Normal[i], sheet.Part[i], mesh.IsPlayerModel);
            _regions[sheet] = regions;
        }
        bool player = mesh.IsPlayerModel;
        var markCol = V(d.MarkColor ?? Color.White);
        var glow = d.Glow is { } gc ? V(gc) : Vector3.Zero;
        for (int i = 0; i < into.Length; i++) {
            var r = (Region)regions[i];
            if (r == Region.None) {
                into[i] = new Color(d.A.R, d.A.G, d.A.B, (byte)255);
                continue;
            }
            var p = sheet.Pos[i];
            var n = sheet.Normal[i];
            var body = Body(d, p, n, r, time);
            var col = body;
            // a marking on each side of the turret (and on the skirts for the big ones)
            if (d.Mark != Mark.None && r == Region.TurretSide && MathF.Abs(n.X) > 0.6f && Marking(d.Mark, markCol, (p.Z + 0.2f) * (n.X > 0 ? 1f : -1f) / 2.3f, (p.Y - 11f) / 2.3f, time) is { } m)
                col = m;
            bool shiny = d.Scheme is Scheme.Gold or Scheme.Chrome or Scheme.Rainbow;
            col = Fittings(d, col, body, p, n, r, player, time);
            // light from above, grime low down, and a little grain so flat paint isn't dead flat
            if (r is not (Region.Track or Region.Under or Region.Muzzle)) {
                float grain = shiny ? 1f : 0.955f + 0.09f * Noise(p * 2.7f);
                float lift = 0.93f + 0.09f * MathF.Max(0f, n.Y);
                col *= grain * lift;
                if (!shiny && sheet.Part[i] == TankSkinMesh.ChassisPart && p.Y < 3.4f)
                    col = Mix(col, new Vector3(0.36f, 0.31f, 0.25f), (3.4f - p.Y) / 3.4f * 0.42f);
                // paint chipped down to the metal along the lower edges
                if (!shiny && p.Y < 6.5f && Noise(p * 4.2f) > 0.83f && Noise(p * 0.9f) > 0.52f)
                    col *= 0.62f;
            }
            into[i] = new Color(MathHelper.Clamp(col.X, 0f, 1f), MathHelper.Clamp(col.Y, 0f, 1f), MathHelper.Clamp(col.Z, 0f, 1f), 1f);
        }
    }

    // ------------------------------------------------------------------ textures

    private static readonly Dictionary<Texture2D, (bool PlayerModel, Design Design)> _painted = [];
    private static readonly Dictionary<int, Color[]> _buffers = [];

    /// <summary>Is this a painted-skin texture (drawn with the unwrapped model)?</summary>
    public static bool IsPainted(Texture2D? tex) => tex is not null && _painted.ContainsKey(tex);

    /// <summary>The unwrapped model a painted texture was made for, or null if it isn't one.</summary>
    public static TankSkinMesh? MeshFor(Texture2D? tex) =>
        tex is not null && _painted.TryGetValue(tex, out var info) ? (info.PlayerModel ? TankSkinMesh.Player : TankSkinMesh.Enemy) : null;

    private static int _tick;
    /// <summary>Once a frame: lets moving designs know time has passed.</summary>
    public static void NextFrame() => _tick++;

    /// <summary>Repaints a moving design now and then (every few frames; they move slowly).</summary>
    public static void Tick(Texture2D tex) {
        if (_painted.TryGetValue(tex, out var info) && info.Design.Animated && (_tick + tex.GetHashCode()) % 4 == 0)
            Repaint(tex, info.PlayerModel, info.Design, Time);
    }

    /// <summary>Makes a painted skin for the player's tank or the enemies'. Animated designs get a smaller sheet
    /// (they're repainted as they move).</summary>
    public static Texture2D Make(GraphicsDevice gd, bool playerModel, Design d, bool small = false) {
        int size = d.Animated || small ? 256 : 512;
        var tex = new Texture2D(gd, size, size, !d.Animated, SurfaceFormat.Color);
        _painted[tex] = (playerModel, d);
        tex.Disposing += (_, _) => _painted.Remove(tex);
        Repaint(tex, playerModel, d, Time);
        return tex;
    }

    public static float Time => (float)(DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600.0);

    /// <summary>Paints (or repaints, for moving designs) a skin texture.</summary>
    public static void Repaint(Texture2D tex, bool playerModel, Design d, float time) {
        if (tex.IsDisposed)
            return;
        var mesh = playerModel ? TankSkinMesh.Player : TankSkinMesh.Enemy;
        int size = tex.Width;
        lock (_buffers) {
            if (!_buffers.TryGetValue(size, out var buf))
                _buffers[size] = buf = new Color[size * size];
            Paint(mesh, mesh.GetSheet(size), buf, d, time);
            tex.SetData(0, null, buf, 0, buf.Length);
            if (tex.LevelCount > 1) {
                // the smaller sizes, each the average of the one before
                var cur = buf;
                int w = size;
                for (int level = 1; level < tex.LevelCount; level++) {
                    int nw = Math.Max(1, w / 2);
                    var next = new Color[nw * nw];
                    for (int y = 0; y < nw; y++)
                        for (int x = 0; x < nw; x++) {
                            Color a = cur[y * 2 * w + x * 2], b = cur[y * 2 * w + Math.Min(w - 1, x * 2 + 1)],
                                c = cur[Math.Min(w - 1, y * 2 + 1) * w + x * 2], e = cur[Math.Min(w - 1, y * 2 + 1) * w + Math.Min(w - 1, x * 2 + 1)];
                            next[y * nw + x] = new Color((a.R + b.R + c.R + e.R) / 4, (a.G + b.G + c.G + e.G) / 4, (a.B + b.B + c.B + e.B) / 4, 255);
                        }
                    tex.SetData(level, null, next, 0, next.Length);
                    cur = next;
                    w = nw;
                }
            }
        }
    }
}
