using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.RebirthUtils;
using TanksRebirth.Graphics;

namespace TanksRebirth.GameContent.Systems.ParticleSystem;

/// <summary>
/// Phones: draws the flat 3D particles (tracks, smoke, sparks, glows...) many at a time instead of one draw each.
/// Every particle used to restart the sprite batch twice and make its own draw call, so a busy battle meant thousands
/// of draws a frame. Each particle comes out exactly as before (same corners, texture crop, color and blending);
/// the corners are just worked out here and sent together.
/// Tracks lying on the floor are drawn first, grouped by picture; everything else keeps its order, with neighbours
/// that share a picture and blending sent together. Text, screen-space and custom-drawn particles still draw the old way.
/// </summary>
internal static class ParticleBatch {
    private const int MaxQuads = 4096;
    private static readonly VertexPositionColorTexture[] _verts = new VertexPositionColorTexture[MaxQuads * 4];
    private static readonly short[] _indices = MakeIndices();
    private static int _quads;
    private static Texture2D? _tex;
    private static bool _additive;
    private static BasicEffect? _fx;

    // floor tracks, grouped by picture
    private static readonly Dictionary<Texture2D, List<Particle>> _floor = [];
    private static readonly List<Texture2D> _floorOrder = [];

    private static short[] MakeIndices() {
        var idx = new short[MaxQuads * 6];
        for (int q = 0, v = 0, i = 0; q < MaxQuads; q++, v += 4) {
            idx[i++] = (short)v; idx[i++] = (short)(v + 1); idx[i++] = (short)(v + 2);
            idx[i++] = (short)(v + 1); idx[i++] = (short)(v + 3); idx[i++] = (short)(v + 2);
        }
        return idx;
    }

    /// <summary>True for particles this can draw (the rest go through <see cref="Particle.Render"/>).</summary>
    private static bool CanBatch(Particle p) =>
        p.Model is null && !p.IsIn2DSpace && !p.IsText && p.UniqueDraw is null && p.Texture is { IsDisposed: false };

    private static bool IsFloorTrack(Particle p) =>
        !p.HasAdditiveBlending && !p.FaceTowardsMe && p.Position.Y < 1f && MathF.Abs(p.Pitch - MathHelper.PiOver2) < 0.01f && p.Roll == 0f;

    public static void Render(ParticleManager system, bool reverse) {
        var gd = TankGame.Instance.GraphicsDevice;
        _fx ??= new BasicEffect(gd) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false, FogEnabled = false };
        _fx.View = system.SystemView;
        _fx.Projection = system.SystemProjection;
        _fx.World = Matrix.Identity;
        _fx.DiffuseColor = Vector3.One;
        _fx.Alpha = 1f;
        // what every particle's color starts from (the lights' color), as the one-at-a-time drawing did
        var baseLight = Lighting.LightColor.ToVector3() * Lighting.ColorBrightness;
        float sceneBright = SceneManager.GameLight.Brightness;
        var view = CameraGlobals.RebirthFreecam;

        var list = system.CurrentParticles;
        int n = list.Count;

        // 1) floor tracks, by picture
        foreach (var l in _floor.Values)
            l.Clear();
        _floorOrder.Clear();
        for (int k = 0; k < n; k++) {
            var p = list[reverse ? n - 1 - k : k];
            if (p is null || !CanBatch(p) || !IsFloorTrack(p))
                continue;
            if (!_floor.TryGetValue(p.Texture, out var bucket))
                _floor[p.Texture] = bucket = [];
            if (bucket.Count == 0)
                _floorOrder.Add(p.Texture);
            bucket.Add(p);
        }
        foreach (var tex in _floorOrder)
            foreach (var p in _floor[tex])
                Add(gd, p, baseLight, sceneBright, view);
        Flush(gd);

        // 2) everything else, in order
        for (int k = 0; k < n; k++) {
            var p = list[reverse ? n - 1 - k : k];
            if (p is null || p.Model is not null)
                continue;
            if (!CanBatch(p)) {
                Flush(gd);
                p.Render();
                continue;
            }
            if (IsFloorTrack(p))
                continue;
            Add(gd, p, baseLight, sceneBright, view);
        }
        Flush(gd);
        // (drop the floor lists' references, so finished particles can be collected)
        foreach (var l in _floor.Values)
            l.Clear();
    }

    private static void Add(GraphicsDevice gd, Particle p, Vector3 baseLight, float sceneBright, Graphics.Cameras.Freecam view) {
        var tex = p.Texture;
        if (_quads > 0 && (tex != _tex || p.HasAdditiveBlending != _additive || _quads >= MaxQuads))
            Flush(gd);
        _tex = tex;
        _additive = p.HasAdditiveBlending;

        Matrix world;
        if (p.FaceTowardsMe)
            world = Matrix.CreateScale(p.Scale) * Matrix.CreateBillboard(p.Position, view.Position, view.World.Down, view.World.Forward);
        else
            world = Matrix.CreateScale(p.Scale) * Matrix.CreateFromYawPitchRoll(p.Yaw, p.Pitch, p.Roll) * Matrix.CreateTranslation(p.Position);

        // the corners the sprite batch would have made (drawn at 0,0 with the particle's crop, origin, turn and size)
        var src = p.TextureCrop ?? new Rectangle(0, 0, tex.Width, tex.Height);
        var origin = p.Origin2D != default ? p.Origin2D : new Vector2(tex.Width / 2f, tex.Height / 2f);
        float s = p.Scale.X;
        float w = src.Width * s, h = src.Height * s;
        float dx = -origin.X * s, dy = -origin.Y * s;
        float cos = MathF.Cos(p.Rotation2D), sin = MathF.Sin(p.Rotation2D);
        float z = p.Layer;
        float u0 = src.X / (float)tex.Width, v0 = src.Y / (float)tex.Height;
        float u1 = (src.X + src.Width) / (float)tex.Width, v1 = (src.Y + src.Height) / (float)tex.Height;

        var lit = Vector3.Clamp(baseLight + p.Color.ToVector3() * sceneBright, Vector3.Zero, Vector3.One);
        var col = new Color(lit.X, lit.Y, lit.Z, MathHelper.Clamp(p.Alpha, 0f, 1f));

        int v = _quads * 4;
        Corner(v, dx, dy, cos, sin, z, u0, v0, col, ref world);
        Corner(v + 1, dx + w, dy, cos, sin, z, u1, v0, col, ref world);
        Corner(v + 2, dx, dy + h, cos, sin, z, u0, v1, col, ref world);
        Corner(v + 3, dx + w, dy + h, cos, sin, z, u1, v1, col, ref world);
        _quads++;
    }

    private static void Corner(int i, float x, float y, float cos, float sin, float z, float u, float v, Color col, ref Matrix world) {
        var local = new Vector3(x * cos - y * sin, x * sin + y * cos, z);
        _verts[i] = new VertexPositionColorTexture(Vector3.Transform(local, world), col, new Vector2(u, v));
    }

    private static void Flush(GraphicsDevice gd) {
        if (_quads == 0 || _tex is null || _fx is null) {
            _quads = 0;
            return;
        }
        var oldBlend = gd.BlendState;
        var oldDepth = gd.DepthStencilState;
        var oldRaster = gd.RasterizerState;
        var oldSampler = gd.SamplerStates[0];
        gd.BlendState = _additive ? BlendState.Additive : BlendState.NonPremultiplied;
        gd.DepthStencilState = DepthStencilState.DepthRead;
        gd.RasterizerState = RenderGlobals.DefaultRasterizer;
        gd.SamplerStates[0] = SamplerState.PointWrap;
        _fx.Texture = _tex;
        foreach (var pass in _fx.CurrentTechnique.Passes) {
            pass.Apply();
            gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _verts, 0, _quads * 4, _indices, 0, _quads * 2);
        }
        gd.BlendState = oldBlend;
        gd.DepthStencilState = oldDepth;
        gd.RasterizerState = oldRaster;
        gd.SamplerStates[0] = oldSampler;
        _quads = 0;
    }
}
