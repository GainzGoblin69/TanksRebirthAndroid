using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Graphics;

namespace TanksRebirth.GameContent.Systems.Troves;

/// <summary>
/// The shapes of Tank Troves' enemies that aren't tanks: the Wall Turret (a steel gun on a striped base, its head
/// turning to aim), the Mortar Bunker (concrete and sandbags around a mortar tube), the Crawler Nest (a pulsing mound
/// with glowing burrows) and the Mine Crawler (a striped bug with scurrying legs and a blinking light).
/// They're built here from simple solids instead of being squashed, repainted tanks.
/// </summary>
public static class TroveEnemyModels {
    private enum Part { Body, Head, LegsA, LegsB, Glow }

    private sealed class Model {
        public readonly List<(Part Part, VertexPositionNormalTexture[] Verts)> Parts = [];
        /// <summary>How wide the soft shadow under it is.</summary>
        public float Shadow = 16f;
    }

    // ------------------------------------------------------------------ colors (one texel each)

    private static readonly List<Color> _palette = [];
    private static int Col(int r, int g, int b) {
        var c = new Color(r, g, b);
        int i = _palette.IndexOf(c);
        if (i < 0) {
            _palette.Add(c);
            i = _palette.Count - 1;
        }
        return i;
    }
    private const int PaletteSize = 64;

    // ------------------------------------------------------------------ building solids

    private sealed class Builder {
        public readonly List<VertexPositionNormalTexture> V = [];
        public Matrix M = Matrix.Identity;

        private static Vector2 Uv(int col) => new((col + 0.5f) / PaletteSize, 0.5f);

        private void Add(Vector3 p, Vector3 n, int col) =>
            V.Add(new VertexPositionNormalTexture(Vector3.Transform(p, M), Vector3.Normalize(Vector3.TransformNormal(n, M)), Uv(col)));

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int col) {
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() < 1e-9f)
                n = Vector3.Cross(c - a, d - a);
            n.Normalize();
            Add(a, n, col); Add(b, n, col); Add(c, n, col);
            Add(a, n, col); Add(c, n, col); Add(d, n, col);
        }

        /// <summary>A box around <paramref name="c"/> (half sizes), optionally turned around Y.</summary>
        public void Box(Vector3 c, Vector3 h, int col, int? topCol = null) {
            Vector3 P(float x, float y, float z) => c + new Vector3(x * h.X, y * h.Y, z * h.Z);
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), topCol ?? col);    // top
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), col);              // +z
            Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), col);          // -z
            Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), col);              // +x
            Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), col);          // -x
        }

        /// <summary>A round (or many-sided) pillar from radius <paramref name="r0"/> at the bottom to <paramref name="r1"/>
        /// at the top. <paramref name="stripe"/> gives every other side a second color.</summary>
        public void Pillar(Vector3 baseCenter, float r0, float r1, float height, int sides, int col, int? topCol = null, int? stripe = null,
            bool smooth = true, float turn = 0f, bool cap = true) {
            float slope = (r0 - r1) / Math.Max(0.001f, height);
            for (int i = 0; i < sides; i++) {
                float a0 = turn + i * MathHelper.TwoPi / sides, a1 = turn + (i + 1) * MathHelper.TwoPi / sides;
                Vector3 d0 = new(MathF.Sin(a0), 0, MathF.Cos(a0)), d1 = new(MathF.Sin(a1), 0, MathF.Cos(a1));
                Vector3 b0 = baseCenter + d0 * r0, b1 = baseCenter + d1 * r0;
                Vector3 t0 = baseCenter + d0 * r1 + Vector3.Up * height, t1 = baseCenter + d1 * r1 + Vector3.Up * height;
                int c = stripe is { } s && i % 2 == 1 ? s : col;
                if (smooth) {
                    Vector3 n0 = d0 + Vector3.Up * slope, n1 = d1 + Vector3.Up * slope;
                    Add(b0, n0, c); Add(b1, n1, c); Add(t1, n1, c);
                    Add(b0, n0, c); Add(t1, n1, c); Add(t0, n0, c);
                }
                else
                    Quad(b0, b1, t1, t0, c);
                if (cap && r1 > 0.01f) {
                    var top = baseCenter + Vector3.Up * height;
                    int tc = topCol ?? col;
                    Add(top, Vector3.Up, tc); Add(t0, Vector3.Up, tc); Add(t1, Vector3.Up, tc);
                }
            }
        }

        /// <summary>The top half of a ball (stretched by the three radii), sitting on <paramref name="c"/>.</summary>
        public void Dome(Vector3 c, float rx, float ry, float rz, int col, int rings = 5, int sides = 14) {
            Vector3 P(int ring, int side, out Vector3 n) {
                float e = ring / (float)rings * MathHelper.PiOver2, a = side * MathHelper.TwoPi / sides;
                var u = new Vector3(MathF.Cos(e) * MathF.Sin(a), MathF.Sin(e), MathF.Cos(e) * MathF.Cos(a));
                n = new Vector3(u.X / rx, u.Y / ry, u.Z / rz);
                return c + new Vector3(u.X * rx, u.Y * ry, u.Z * rz);
            }
            for (int ring = 0; ring < rings; ring++)
                for (int side = 0; side < sides; side++) {
                    var a = P(ring, side, out var na);
                    var b = P(ring, side + 1, out var nb);
                    var d = P(ring + 1, side, out var nd);
                    var e = P(ring + 1, side + 1, out var ne);
                    Add(a, na, col); Add(b, nb, col); Add(e, ne, col);
                    if (ring < rings - 1) {
                        Add(a, na, col); Add(e, ne, col); Add(d, nd, col);
                    }
                }
        }

        public VertexPositionNormalTexture[] Take() {
            var r = V.ToArray();
            V.Clear();
            M = Matrix.Identity;
            return r;
        }
    }

    // ------------------------------------------------------------------ the four enemies

    private static Model BuildTurret() {
        var m = new Model { Shadow = 19f };
        var b = new Builder();
        int steelDark = Col(74, 80, 92), steel = Col(150, 157, 168), steelLight = Col(196, 202, 212), black = Col(34, 36, 42),
            yellow = Col(240, 196, 40), red = Col(230, 60, 50), gunmetal = Col(92, 98, 110);
        // a wide base with warning stripes, and a collar
        b.Pillar(Vector3.Zero, 14f, 12.4f, 3.2f, 16, yellow, steelDark, black, smooth: false);
        b.Pillar(new Vector3(0, 3.2f, 0), 11.2f, 10.4f, 2.6f, 16, steelDark, gunmetal);
        b.Pillar(new Vector3(0, 5.8f, 0), 8.2f, 8.2f, 1.2f, 16, black, black);
        m.Parts.Add((Part.Body, b.Take()));
        // the head: an armored drum with a sloped roof, a hatch, twin barrels and a sensor
        b.Pillar(new Vector3(0, 7f, 0), 9.2f, 8.6f, 5.4f, 8, steel, steelLight, smooth: false, turn: MathHelper.Pi / 8);
        b.Pillar(new Vector3(0, 12.4f, 0), 8.6f, 5.2f, 2.2f, 8, steelLight, steel, smooth: false, turn: MathHelper.Pi / 8);
        b.Pillar(new Vector3(-1.5f, 14.6f, -1.5f), 2.6f, 2.4f, 0.9f, 10, gunmetal, steelDark);
        // gun shield and barrels (forward is +z)
        b.Box(new Vector3(0, 9.9f, 8.6f), new Vector3(6.2f, 2.9f, 1.3f), gunmetal, steelDark);
        foreach (float x in new[] { -2.7f, 2.7f }) {
            b.M = Matrix.CreateRotationX(MathHelper.PiOver2) * Matrix.CreateTranslation(x, 9.9f, 9.5f);
            b.Pillar(Vector3.Zero, 1.55f, 1.55f, 12f, 10, steelDark, black);
            b.Pillar(new Vector3(0, 12f, 0), 2.1f, 2.1f, 2.4f, 10, black, black);
            b.M = Matrix.Identity;
        }
        m.Parts.Add((Part.Head, b.Take()));
        b.Box(new Vector3(4.6f, 13.6f, 3.8f), new Vector3(1.1f, 1.1f, 1.1f), red);
        m.Parts.Add((Part.Glow, b.Take()));
        return m;
    }

    private static Model BuildBunker() {
        var m = new Model { Shadow = 22f };
        var b = new Builder();
        int concrete = Col(156, 152, 138), concreteDark = Col(112, 108, 98), concreteLight = Col(188, 184, 170), black = Col(30, 30, 34),
            sand = Col(196, 170, 120), sandDark = Col(160, 134, 90), metal = Col(70, 76, 84), metalLight = Col(130, 138, 148), olive = Col(92, 104, 66);
        // a low concrete block with sloped sides and a darker footing
        b.Pillar(Vector3.Zero, 17.5f, 17f, 1.6f, 4, concreteDark, concreteDark, smooth: false, turn: MathHelper.PiOver4);
        b.Pillar(new Vector3(0, 1.6f, 0), 16.4f, 12.6f, 6.6f, 4, concrete, concreteLight, smooth: false, turn: MathHelper.PiOver4);
        // the firing pit on top
        b.Pillar(new Vector3(0, 8.2f, 0), 7.4f, 7.4f, 0.9f, 14, concreteDark, black);
        // viewing slits on the four sides
        for (int i = 0; i < 4; i++) {
            b.M = Matrix.CreateRotationX(-0.52f) * Matrix.CreateTranslation(0, 4.7f, 10.6f) * Matrix.CreateRotationY(i * MathHelper.PiOver2);
            b.Box(Vector3.Zero, new Vector3(4.6f, 0.9f, 0.5f), black);
        }
        b.M = Matrix.Identity;
        // sandbags around the corners
        for (int i = 0; i < 4; i++) {
            float a = MathHelper.PiOver4 + i * MathHelper.PiOver2;
            var at = new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * 15.4f;
            for (int k = -1; k <= 1; k++) {
                var side = new Vector3(MathF.Cos(a), 0, -MathF.Sin(a)) * (k * 4.6f);
                b.M = Matrix.CreateRotationY(a + MathHelper.PiOver2) * Matrix.CreateTranslation(at + side - new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * Math.Abs(k) * 1.4f);
                b.Dome(Vector3.Zero, 2.9f, 2.5f, 1.9f, k == 0 ? sand : sandDark, 3, 8);
            }
            b.M = Matrix.CreateRotationY(a + MathHelper.PiOver2) * Matrix.CreateTranslation(at + new Vector3(0, 2.2f, 0) - new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * 0.9f);
            b.Dome(Vector3.Zero, 2.9f, 2.3f, 1.9f, sand, 3, 8);
        }
        b.M = Matrix.Identity;
        // the mortar: a base plate and a tube tilted forward
        b.Pillar(new Vector3(0, 9.1f, 0), 4.2f, 3.8f, 0.9f, 12, metal, metalLight);
        b.M = Matrix.CreateRotationX(0.42f) * Matrix.CreateTranslation(0, 9.6f, -1.2f);
        b.Pillar(Vector3.Zero, 2.5f, 2.3f, 10.5f, 12, olive, black);
        b.Pillar(new Vector3(0, 9.2f, 0), 2.95f, 2.95f, 1.5f, 12, metal, black);
        m.Parts.Add((Part.Body, b.Take()));
        return m;
    }

    private static Model BuildNest() {
        var m = new Model { Shadow = 21f };
        var b = new Builder();
        int flesh = Col(128, 54, 84), fleshDark = Col(88, 34, 60), fleshLight = Col(170, 84, 112), pit = Col(26, 10, 20), glow = Col(255, 214, 70),
            ridge = Col(150, 68, 98);
        // a lumpy mound: one big dome with smaller swellings around it
        b.Dome(Vector3.Zero, 15.5f, 8.6f, 15.5f, flesh, 6, 18);
        for (int i = 0; i < 5; i++) {
            float a = i * MathHelper.TwoPi / 5f + 0.5f;
            var at = new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * 11.4f;
            b.Dome(at, 5.6f, 4.6f + i % 2, 5.6f, i % 2 == 0 ? fleshDark : ridge, 4, 10);
        }
        m.Parts.Add((Part.Body, b.Take()));
        // burrows: a ring of raised holes, and a bigger one on top, each with a glowing rim and a dark throat
        var glowB = new Builder();
        void Burrow(Vector3 at, float r, float tilt, float yaw) {
            var mm = Matrix.CreateRotationX(tilt) * Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(at);
            b.M = mm;
            b.Pillar(Vector3.Zero, r * 1.35f, r * 1.05f, r * 0.9f, 10, fleshLight, pit);
            glowB.M = mm;
            glowB.Pillar(new Vector3(0, r * 0.9f, 0), r * 1.05f, r * 0.78f, 0.25f, 10, glow, pit);
        }
        Burrow(new Vector3(0, 7.6f, 0), 3.6f, 0f, 0f);
        for (int i = 0; i < 4; i++) {
            float a = i * MathHelper.PiOver2 + 0.25f;
            Burrow(new Vector3(MathF.Sin(a) * 8.6f, 5.3f, MathF.Cos(a) * 8.6f), 2.3f, 0.75f, a);
        }
        m.Parts.Add((Part.Body, b.Take()));
        m.Parts.Add((Part.Glow, glowB.Take()));
        return m;
    }

    private static Model BuildCrawler() {
        var m = new Model { Shadow = 9.5f };
        var b = new Builder();
        int yellow = Col(244, 200, 44), black = Col(30, 28, 30), dark = Col(58, 52, 50), red = Col(240, 50, 40), eye = Col(255, 255, 255);
        // a striped shell (forward is +z), a dark head with two eyes, and feelers
        b.Dome(new Vector3(0, 1.6f, -0.6f), 4.6f, 4.4f, 6f, yellow, 5, 14);
        foreach (float z in new[] { -3f, 0.3f }) {
            b.M = Matrix.CreateTranslation(0, 1.6f, z);
            // a band hugging the shell
            for (int i = 0; i < 8; i++) {
                float a0 = -MathHelper.PiOver2 + i * MathHelper.Pi / 8f, a1 = a0 + MathHelper.Pi / 8f;
                float k = MathF.Sqrt(Math.Max(0f, 1f - (z + 0.6f) * (z + 0.6f) / 36f)) * 1.03f;
                Vector3 P(float a, float dz) => new(MathF.Sin(a) * 4.6f * k, MathF.Cos(a) * 4.4f * k, dz);
                b.Quad(P(a0, -0.75f), P(a0, 0.75f), P(a1, 0.75f), P(a1, -0.75f), black);
            }
        }
        b.M = Matrix.Identity;
        b.Dome(new Vector3(0, 1.4f, 5.2f), 3f, 2.9f, 2.8f, dark, 4, 10);
        foreach (float x in new[] { -1.3f, 1.3f }) {
            b.Box(new Vector3(x, 3.3f, 7.1f), new Vector3(0.55f, 0.55f, 0.4f), eye);
            b.M = Matrix.CreateRotationX(0.9f) * Matrix.CreateRotationY(x * 0.25f) * Matrix.CreateTranslation(x, 3.6f, 6.6f);
            b.Box(new Vector3(0, 1.6f, 0), new Vector3(0.22f, 1.6f, 0.22f), black);
            b.M = Matrix.Identity;
        }
        b.Box(new Vector3(0, 0.9f, -0.4f), new Vector3(3.6f, 0.8f, 5f), dark); // belly
        m.Parts.Add((Part.Body, b.Take()));
        // six legs in two sets that swing against each other
        for (int set = 0; set < 2; set++) {
            for (int i = 0; i < 3; i++)
                foreach (int side in new[] { -1, 1 }) {
                    if ((i + (side > 0 ? 1 : 0)) % 2 != set)
                        continue;
                    float z = 3.2f - i * 3.3f;
                    b.M = Matrix.CreateRotationZ(side * -0.5f) * Matrix.CreateRotationY(side * (i - 1) * -0.35f) * Matrix.CreateTranslation(side * 3.4f, 1.9f, z);
                    b.Box(new Vector3(side * 2.2f, 0, 0), new Vector3(2.4f, 0.42f, 0.5f), black);
                    b.M = Matrix.CreateRotationY(side * (i - 1) * -0.35f) * Matrix.CreateTranslation(side * 3.4f, 1.9f, z);
                    b.Box(new Vector3(side * 4.3f, -0.6f, 0), new Vector3(0.42f, 1.5f, 0.45f), black);
                    b.M = Matrix.Identity;
                }
            m.Parts.Add((set == 0 ? Part.LegsA : Part.LegsB, b.Take()));
        }
        // the light on its back
        b.Dome(new Vector3(0, 5.6f, -1.2f), 1.5f, 1.5f, 1.5f, red, 3, 8);
        m.Parts.Add((Part.Glow, b.Take()));
        return m;
    }

    // ------------------------------------------------------------------ drawing

    private static Model? _turret, _bunker, _nest, _crawler;
    private static BasicEffect? _fx, _shadowFx;
    private static Texture2D? _paletteTex, _shadowTex;
    private static VertexPositionTexture[] _shadowQuad = new VertexPositionTexture[6];
    private static bool _failed;

    private static void Ensure(GraphicsDevice gd) {
        if (_fx is { IsDisposed: false } && _paletteTex is { IsDisposed: false } && _shadowTex is { IsDisposed: false })
            return;
        _palette.Clear();
        _turret = BuildTurret();
        _bunker = BuildBunker();
        _nest = BuildNest();
        _crawler = BuildCrawler();
        var px = new Color[PaletteSize];
        for (int i = 0; i < _palette.Count && i < PaletteSize; i++)
            px[i] = _palette[i];
        _paletteTex = new Texture2D(gd, PaletteSize, 1);
        _paletteTex.SetData(px);
        // a soft round shadow
        const int S = 48;
        var sh = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++) {
                float d = MathF.Sqrt((x - S / 2f + 0.5f) * (x - S / 2f + 0.5f) + (y - S / 2f + 0.5f) * (y - S / 2f + 0.5f)) / (S / 2f);
                float a = MathHelper.Clamp((1f - d) / 0.45f, 0f, 1f);
                a = a * a * (3 - 2 * a) * 0.5f;
                sh[y * S + x] = new Color(0f, 0f, 0f, a);
            }
        _shadowTex = new Texture2D(gd, S, S);
        _shadowTex.SetData(sh);
        _fx = new BasicEffect(gd) { TextureEnabled = true, Texture = _paletteTex };
        _shadowFx = new BasicEffect(gd) { TextureEnabled = true, Texture = _shadowTex, LightingEnabled = false };
    }

    /// <summary>Draws a non-tank enemy in place of the tank model; false if this tank isn't one (or drawing failed).</summary>
    public static bool Render(Tank t) {
        if (_failed)
            return false;
        var variant = TroveEnemies.VariantOf(t);
        if (variant is not (EnemyVariant.Turret or EnemyVariant.Bunker or EnemyVariant.Nest or EnemyVariant.Crawler))
            return false;
        try {
            float time = (float)TroveRun.PlayTime;
            Draw(TankGame.Instance.GraphicsDevice, variant, t.Position3D, t.ChassisRotation, t.TurretRotation, time, t.WorldId * 1.7f,
                CameraGlobals.GameView, CameraGlobals.GameProjection, TroveEnemies.IsFused(t));
            return true;
        }
        catch (Exception e) {
            _failed = true;
            TankGame.ClientLog?.Write($"Enemy models failed (they're drawn as tanks instead): {e.Message}", Internals.LogType.Warn);
            return false;
        }
    }

    /// <summary>Draws one enemy. <paramref name="phase"/> keeps different enemies' animations out of step.</summary>
    public static void Draw(GraphicsDevice gd, EnemyVariant variant, Vector3 pos, float chassisRotation, float turretRotation, float time, float phase,
        Matrix view, Matrix projection, bool alarmed = false) {
        Ensure(gd);
        var model = variant switch {
            EnemyVariant.Turret => _turret!,
            EnemyVariant.Bunker => _bunker!,
            EnemyVariant.Nest => _nest!,
            _ => _crawler!,
        };
        var oldRaster = gd.RasterizerState;
        var oldBlend = gd.BlendState;
        var oldDepth = gd.DepthStencilState;
        var oldSampler = gd.SamplerStates[0];
        gd.RasterizerState = RasterizerState.CullNone;

        // the shadow first, flat on the floor
        float sw = model.Shadow;
        var sp = new Vector3(pos.X + sw * 0.08f, 0.25f, pos.Z + sw * 0.1f);
        _shadowQuad[0] = new(sp + new Vector3(-sw, 0, -sw), new Vector2(0, 0));
        _shadowQuad[1] = new(sp + new Vector3(sw, 0, -sw), new Vector2(1, 0));
        _shadowQuad[2] = new(sp + new Vector3(sw, 0, sw), new Vector2(1, 1));
        _shadowQuad[3] = _shadowQuad[0];
        _shadowQuad[4] = _shadowQuad[2];
        _shadowQuad[5] = new(sp + new Vector3(-sw, 0, sw), new Vector2(0, 1));
        gd.BlendState = BlendState.NonPremultiplied;
        gd.DepthStencilState = DepthStencilState.DepthRead;
        gd.SamplerStates[0] = SamplerState.LinearClamp;
        var sfx = _shadowFx!;
        sfx.World = Matrix.Identity;
        sfx.View = view;
        sfx.Projection = projection;
        foreach (var pass in sfx.CurrentTechnique.Passes) {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleList, _shadowQuad, 0, 2);
        }

        gd.BlendState = BlendState.Opaque;
        gd.DepthStencilState = DepthStencilState.Default;
        gd.SamplerStates[0] = SamplerState.PointClamp;
        var fx = _fx!;
        fx.View = view;
        fx.Projection = projection;
        fx.Alpha = 1f;
        fx.SetDefaultGameLighting_IngameEntities(0.9f);
        // a second, slanted light so the sides have some shape
        fx.DirectionalLight1.Enabled = true;
        fx.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(-0.5f, -0.35f, -0.8f));
        fx.DirectionalLight1.DiffuseColor = new Vector3(0.42f);
        fx.DirectionalLight1.SpecularColor = Vector3.Zero;

        var place = Matrix.CreateTranslation(pos);
        var chassis = Matrix.CreateRotationY(-chassisRotation);
        float t = time + phase;
        foreach (var (part, verts) in model.Parts) {
            Matrix world;
            var emissive = Vector3.Zero;
            switch (variant) {
                case EnemyVariant.Turret:
                    world = part == Part.Body ? place : Matrix.CreateRotationY(turretRotation) * place;
                    if (part == Part.Glow)
                        emissive = new Vector3(0.55f + 0.45f * MathF.Sin(t * 5f), 0, 0);
                    break;
                case EnemyVariant.Nest: {
                    // it breathes
                    float s = 1f + 0.035f * MathF.Sin(t * 2.6f);
                    world = Matrix.CreateScale(2f - s, s, 2f - s) * place;
                    if (part == Part.Glow)
                        emissive = new Vector3(1f, 0.8f, 0.2f) * (0.45f + 0.4f * MathF.Sin(t * 2.6f));
                    break;
                }
                case EnemyVariant.Crawler: {
                    float swing = MathF.Sin(t * 22f) * 0.32f;
                    world = part switch {
                        Part.LegsA => Matrix.CreateRotationY(swing),
                        Part.LegsB => Matrix.CreateRotationY(-swing),
                        _ => Matrix.CreateTranslation(0, MathF.Abs(MathF.Sin(t * 22f)) * 0.5f, 0),
                    } * chassis * place;
                    if (part == Part.Glow) {
                        bool on = alarmed ? (int)(t * 14f) % 2 == 0 : (int)(t * 2.2f) % 2 == 0;
                        emissive = on ? new Vector3(1f, 0.15f, 0.1f) : Vector3.Zero;
                    }
                    break;
                }
                default:
                    world = place;
                    break;
            }
            fx.World = world;
            fx.EmissiveColor = emissive;
            foreach (var pass in fx.CurrentTechnique.Passes) {
                pass.Apply();
                gd.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, verts.Length / 3);
            }
        }
        fx.EmissiveColor = Vector3.Zero;

        gd.RasterizerState = oldRaster;
        gd.BlendState = oldBlend;
        gd.DepthStencilState = oldDepth;
        gd.SamplerStates[0] = oldSampler;
    }
}
