using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.Graphics;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// The tank models unwrapped for painting. The models that ship with the game point almost every face at a single
/// texel, so a texture can only give each part one flat color. Here the same shapes (read from the model files, see
/// <see cref="TankSkinGeometry"/>) get texture coordinates that spread every face out over a sheet, and for each texel
/// of that sheet we remember where on the tank it is. A skin is then painted by a function of the place on the tank
/// (camouflage that wraps around corners, wheels, hatches, markings...), and while a painted tank is drawn its model's
/// vertex buffers are switched for these ones.
/// </summary>
public sealed class TankSkinMesh {
    public const int ChassisPart = 1, CannonPart = 2;

    /// <summary>The player's tank (true) or the enemies' (false).</summary>
    public readonly bool IsPlayerModel;

    // per triangle corner
    private readonly Vector3[] _pos, _nrm;
    private readonly byte[] _part;
    private readonly Vector2[] _uv;

    private VertexBuffer? _chassisVb, _cannonVb;
    private IndexBuffer? _chassisIb, _cannonIb;
    private int _chassisVerts, _cannonVerts;

    /// <summary>What's at each texel of a sheet of a given size.</summary>
    public sealed class Sheet {
        public int Size;
        /// <summary>Where on the tank (model units; x across, y up, z forward).</summary>
        public Vector3[] Pos = [];
        public Vector3[] Normal = [];
        /// <summary>0 = nothing, <see cref="ChassisPart"/> or <see cref="CannonPart"/>.</summary>
        public byte[] Part = [];
    }
    private readonly Dictionary<int, Sheet> _sheets = [];

    private static TankSkinMesh? _player, _enemy;
    public static TankSkinMesh Player => _player ??= new TankSkinMesh(true);
    public static TankSkinMesh Enemy => _enemy ??= new TankSkinMesh(false);

    private TankSkinMesh(bool player) {
        IsPlayerModel = player;
        var chassis = Decode(player ? TankSkinGeometry.PlayerChassis : TankSkinGeometry.EnemyChassis);
        var cannon = Decode(player ? TankSkinGeometry.PlayerCannon : TankSkinGeometry.EnemyCannon);
        int n = chassis.Length / 6 + cannon.Length / 6;
        _pos = new Vector3[n];
        _nrm = new Vector3[n];
        _part = new byte[n];
        _uv = new Vector2[n];
        int k = 0;
        foreach (var (data, part) in new[] { (chassis, (byte)ChassisPart), (cannon, (byte)CannonPart) })
            for (int i = 0; i < data.Length; i += 6, k++) {
                _pos[k] = new Vector3(data[i], data[i + 1], data[i + 2]);
                _nrm[k] = Vector3.Normalize(new Vector3(data[i + 3], data[i + 4], data[i + 5]));
                _part[k] = part;
            }
        // the game draws clockwise faces (the model files wind them the other way)
        for (int t = 0; t + 2 < n; t += 3) {
            (_pos[t + 1], _pos[t + 2]) = (_pos[t + 2], _pos[t + 1]);
            (_nrm[t + 1], _nrm[t + 2]) = (_nrm[t + 2], _nrm[t + 1]);
        }
        _chassisVerts = chassis.Length / 6;
        _cannonVerts = cannon.Length / 6;
        Unwrap();
    }

    private static float[] Decode(string base64) {
        var bytes = Convert.FromBase64String(base64);
        var f = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, f, 0, f.Length * 4);
        return f;
    }

    // ------------------------------------------------------------------ unwrapping

    private const int BaseSize = 256, Pad = 3;

    private sealed class Chart {
        public int Part, Axis, Sign, Layer;
        public float UMin, VMax;
        public int W, H, X, Y;
        public float[] Depth = [];
    }

    private static (float U, float V, float D) Project(Vector3 p, int axis) => axis switch {
        0 => (p.Z, p.Y, p.X),
        1 => (p.X, p.Z, p.Y),
        _ => (p.X, p.Y, p.Z),
    };

    /// <summary>Gives every triangle a place on the sheet: each face is laid flat looking along the axis it faces most
    /// (top, sides, front, back...), and faces hidden behind others from that side go on a sheet of their own.</summary>
    private void Unwrap() {
        int tris = _pos.Length / 3;
        for (float tpu = 7.4f; tpu > 1.5f; tpu -= 0.2f) {
            var charts = new List<Chart>();
            var triChart = new Chart[tris];
            // outermost faces first, so they take the first layer
            var order = new int[tris];
            var key = new float[tris];
            var triAxis = new int[tris];
            var triSign = new int[tris];
            for (int t = 0; t < tris; t++) {
                var fn = Vector3.Cross(_pos[t * 3 + 1] - _pos[t * 3], _pos[t * 3 + 2] - _pos[t * 3]);
                if (fn.LengthSquared() < 1e-10f)
                    fn = _nrm[t * 3];
                // (the model's own normal decides which way a face looks; the winding only breaks ties)
                var n = _nrm[t * 3] + _nrm[t * 3 + 1] + _nrm[t * 3 + 2];
                if (n.LengthSquared() < 1e-6f)
                    n = fn;
                float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
                int axis = ay >= ax && ay >= az ? 1 : ax >= az ? 0 : 2;
                float comp = axis == 0 ? n.X : axis == 1 ? n.Y : n.Z;
                triAxis[t] = axis;
                triSign[t] = comp >= 0 ? 1 : -1;
                var c = (_pos[t * 3] + _pos[t * 3 + 1] + _pos[t * 3 + 2]) / 3f;
                key[t] = -Project(c, axis).D * triSign[t];
                order[t] = t;
            }
            Array.Sort(key, order);

            foreach (int t in order) {
                int part = _part[t * 3], axis = triAxis[t], sign = triSign[t];
                // the underside is never seen: a small patch will do
                float scale = axis == 1 && sign < 0 ? tpu * 0.25f : tpu;
                Chart? home = null;
                for (int layer = 0; home is null; layer++) {
                    var ch = charts.Find(c => c.Part == part && c.Axis == axis && c.Sign == sign && c.Layer == layer);
                    if (ch is null) {
                        // sized to the whole part, seen from this side
                        float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
                        for (int i = 0; i < _pos.Length; i++) {
                            if (_part[i] != part)
                                continue;
                            var (u, v, _) = Project(_pos[i], axis);
                            uMin = MathF.Min(uMin, u); uMax = MathF.Max(uMax, u);
                            vMin = MathF.Min(vMin, v); vMax = MathF.Max(vMax, v);
                        }
                        ch = new Chart { Part = part, Axis = axis, Sign = sign, Layer = layer, UMin = uMin, VMax = vMax,
                            W = (int)MathF.Ceiling((uMax - uMin) * scale) + Pad * 2, H = (int)MathF.Ceiling((vMax - vMin) * scale) + Pad * 2 };
                        ch.Depth = new float[ch.W * ch.H];
                        Array.Fill(ch.Depth, float.NaN);
                        charts.Add(ch);
                    }
                    if (TryPlace(ch, t, axis, scale))
                        home = ch;
                }
                triChart[t] = home;
            }

            // pack the charts in rows, tallest first
            charts.Sort((a, b) => b.H != a.H ? b.H.CompareTo(a.H) : b.W.CompareTo(a.W));
            int x = 0, y = 0, rowH = 0;
            bool fits = true;
            foreach (var ch in charts) {
                if (x + ch.W > BaseSize) {
                    x = 0;
                    y += rowH;
                    rowH = 0;
                }
                if (ch.W > BaseSize || y + ch.H > BaseSize) {
                    fits = false;
                    break;
                }
                ch.X = x;
                ch.Y = y;
                x += ch.W;
                rowH = Math.Max(rowH, ch.H);
            }
            if (!fits)
                continue;
            for (int t = 0; t < tris; t++) {
                var ch = triChart[t];
                float scale = ch.Axis == 1 && ch.Sign < 0 ? tpu * 0.25f : tpu;
                for (int j = 0; j < 3; j++) {
                    var (u, v, _) = Project(_pos[t * 3 + j], ch.Axis);
                    _uv[t * 3 + j] = new Vector2((ch.X + Pad + (u - ch.UMin) * scale) / BaseSize, (ch.Y + Pad + (ch.VMax - v) * scale) / BaseSize);
                }
            }
            return;
        }
        throw new InvalidOperationException("The tank doesn't fit on its skin sheet.");
    }

    /// <summary>Puts a triangle on a chart layer unless something at a different depth is already there.</summary>
    private bool TryPlace(Chart ch, int t, int axis, float scale) {
        Span<float> us = stackalloc float[3], vs = stackalloc float[3], ds = stackalloc float[3];
        for (int j = 0; j < 3; j++) {
            var (u, v, d) = Project(_pos[t * 3 + j], axis);
            us[j] = Pad + (u - ch.UMin) * scale;
            vs[j] = Pad + (ch.VMax - v) * scale;
            ds[j] = d;
        }
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(us[0], MathF.Min(us[1], us[2])))), x1 = Math.Min(ch.W - 1, (int)MathF.Ceiling(MathF.Max(us[0], MathF.Max(us[1], us[2]))));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(vs[0], MathF.Min(vs[1], vs[2])))), y1 = Math.Min(ch.H - 1, (int)MathF.Ceiling(MathF.Max(vs[0], MathF.Max(vs[1], vs[2]))));
        float det = (vs[1] - vs[2]) * (us[0] - us[2]) + (us[2] - us[1]) * (vs[0] - vs[2]);
        if (MathF.Abs(det) < 1e-6f)
            return true; // a sliver seen edge-on: it covers nothing
        for (int pass = 0; pass < 2; pass++)
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) {
                    float px = x + 0.5f, py = y + 0.5f;
                    float a = ((vs[1] - vs[2]) * (px - us[2]) + (us[2] - us[1]) * (py - vs[2])) / det;
                    float b = ((vs[2] - vs[0]) * (px - us[2]) + (us[0] - us[2]) * (py - vs[2])) / det;
                    float c = 1 - a - b;
                    if (a < -0.02f || b < -0.02f || c < -0.02f)
                        continue;
                    float d = a * ds[0] + b * ds[1] + c * ds[2];
                    ref float cell = ref ch.Depth[y * ch.W + x];
                    if (pass == 0) {
                        if (!float.IsNaN(cell) && MathF.Abs(cell - d) > 0.35f)
                            return false;
                    }
                    else
                        cell = d;
                }
        return true;
    }

    // ------------------------------------------------------------------ sheets

    /// <summary>What's at each texel, for a sheet <paramref name="size"/> texels across (256 or 512).</summary>
    public Sheet GetSheet(int size) {
        if (_sheets.TryGetValue(size, out var sheet))
            return sheet;
        sheet = new Sheet { Size = size, Pos = new Vector3[size * size], Normal = new Vector3[size * size], Part = new byte[size * size] };
        int tris = _pos.Length / 3;
        for (int t = 0; t < tris; t++) {
            Vector2 a = _uv[t * 3] * size, b = _uv[t * 3 + 1] * size, c = _uv[t * 3 + 2] * size;
            int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)))), x1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
            int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)))), y1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
            float det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (MathF.Abs(det) < 1e-6f)
                continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) {
                    float px = x + 0.5f, py = y + 0.5f;
                    float wa = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / det;
                    float wb = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / det;
                    float wc = 1 - wa - wb;
                    // (a little outside the triangle too, so its edge texels are filled)
                    if (wa < -0.35f || wb < -0.35f || wc < -0.35f)
                        continue;
                    bool inside = wa >= 0 && wb >= 0 && wc >= 0;
                    int i = y * size + x;
                    if (sheet.Part[i] != 0 && !inside)
                        continue;
                    if (sheet.Part[i] == 0 || inside) {
                        sheet.Pos[i] = _pos[t * 3] * wa + _pos[t * 3 + 1] * wb + _pos[t * 3 + 2] * wc;
                        var n = _nrm[t * 3] * wa + _nrm[t * 3 + 1] * wb + _nrm[t * 3 + 2] * wc;
                        sheet.Normal[i] = n.LengthSquared() > 1e-6f ? Vector3.Normalize(n) : _nrm[t * 3];
                        sheet.Part[i] = _part[t * 3];
                    }
                }
        }
        // spread the edges outward a little, so smoothing the picture never pulls in empty texels
        for (int pass = 0; pass < (size >= 512 ? 3 : 2); pass++) {
            var from = (byte[])sheet.Part.Clone();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    int i = y * size + x;
                    if (from[i] != 0)
                        continue;
                    for (int k = 0; k < 4; k++) {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size || from[ny * size + nx] == 0)
                            continue;
                        int j = ny * size + nx;
                        sheet.Pos[i] = sheet.Pos[j];
                        sheet.Normal[i] = sheet.Normal[j];
                        sheet.Part[i] = from[j];
                        break;
                    }
                }
        }
        _sheets[size] = sheet;
        return sheet;
    }

    // ------------------------------------------------------------------ drawing with the unwrapped shapes

    private void EnsureBuffers(GraphicsDevice gd) {
        if (_chassisVb is { IsDisposed: false } && _cannonVb is { IsDisposed: false })
            return;
        (VertexBuffer, IndexBuffer) Make(int start, int count) {
            var verts = new SkinVertex[count];
            var idx = new short[count];
            for (int i = 0; i < count; i++) {
                verts[i] = new SkinVertex { Position = _pos[start + i], Normal = _nrm[start + i], Color = Color.White, Uv = _uv[start + i] };
                idx[i] = (short)i;
            }
            var vb = new VertexBuffer(gd, SkinVertex.Declaration, count, BufferUsage.WriteOnly);
            vb.SetData(verts);
            var ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, count, BufferUsage.WriteOnly);
            ib.SetData(idx);
            return (vb, ib);
        }
        (_chassisVb, _chassisIb) = Make(0, _chassisVerts);
        (_cannonVb, _cannonIb) = Make(_chassisVerts, _cannonVerts);
    }

    /// <summary>A corner of the unwrapped tank. It carries a (white) color because the game's tank models do, and
    /// their effect multiplies by it: without one, phones read it as black.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    private struct SkinVertex : IVertexType {
        public Vector3 Position;
        public Vector3 Normal;
        public Color Color;
        public Vector2 Uv;
        public static readonly VertexDeclaration Declaration = new(36,
            new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
            new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0));
        readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
    }

    private readonly struct Saved(ModelMeshPart part) {
        public readonly ModelMeshPart Part = part;
        public readonly VertexBuffer Vb = part.VertexBuffer;
        public readonly IndexBuffer Ib = part.IndexBuffer;
        public readonly int NumVertices = part.NumVertices, PrimitiveCount = part.PrimitiveCount, StartIndex = part.StartIndex, VertexOffset = part.VertexOffset;
    }
    private readonly List<Saved> _saved = [];
    private SamplerState? _savedSampler;
    private bool? _matches;

    /// <summary>Do these shapes sit where the loaded model's do? (Checked once; if not, painted skins are off.)</summary>
    private bool Matches(Model model) {
        if (_matches is { } known)
            return known;
        bool ok = false;
        try {
            foreach (var mesh in model.Meshes) {
                if (mesh.Name != "Cannon")
                    continue;
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                for (int i = _chassisVerts; i < _pos.Length; i++) {
                    min = Vector3.Min(min, _pos[i]);
                    max = Vector3.Max(max, _pos[i]);
                }
                var center = (min + max) / 2f;
                float radius = (max - min).Length() / 2f;
                var bs = mesh.BoundingSphere;
                // the model may have been saved facing the other way: turn ours to match
                if (Vector3.Distance(bs.Center, new Vector3(-center.X, center.Y, -center.Z)) + 1f < Vector3.Distance(bs.Center, center)) {
                    for (int i = 0; i < _pos.Length; i++) {
                        _pos[i] = new Vector3(-_pos[i].X, _pos[i].Y, -_pos[i].Z);
                        _nrm[i] = new Vector3(-_nrm[i].X, _nrm[i].Y, -_nrm[i].Z);
                    }
                    center = new Vector3(-center.X, center.Y, -center.Z);
                    _chassisVb?.Dispose();
                    _cannonVb?.Dispose();
                    _chassisVb = _cannonVb = null;
                    TankGame.ClientLog?.Write($"Tank skins: the {(IsPlayerModel ? "player" : "enemy")} model faces the other way; turned to match.", Internals.LogType.Info);
                }
                ok = Vector3.Distance(bs.Center, center) < 3.5f && bs.Radius > radius * 0.7f && bs.Radius < radius * 1.45f;
                if (!ok)
                    TankGame.ClientLog?.Write($"Tank skins off for the {(IsPlayerModel ? "player" : "enemy")} model: its cannon is at {bs.Center} r={bs.Radius:0.0}, " +
                        $"expected {center} r={radius:0.0}.", Internals.LogType.Warn);
            }
        }
        catch (Exception e) {
            TankGame.ClientLog?.Write($"Tank skins off: {e.Message}", Internals.LogType.Warn);
        }
        _matches = ok;
        return ok;
    }

    /// <summary>Can painted skins be used with this model?</summary>
    public bool Usable(Model? model) => model is not null && Matches(model);

    /// <summary>Switches the model's chassis and cannon to the unwrapped shapes (call <see cref="End"/> after drawing).
    /// False if they can't be used, and nothing was changed.</summary>
    public bool Begin(Model model) {
        if (_saved.Count > 0 || !Matches(model))
            return false;
        var gd = TankGame.Instance.GraphicsDevice;
        EnsureBuffers(gd);
        foreach (var mesh in model.Meshes) {
            bool chassis = mesh.Name == "Chassis", cannon = mesh.Name == "Cannon";
            if (!chassis && !cannon)
                continue;
            if (mesh.MeshParts.Count != 1) {
                End();
                _matches = false;
                return false;
            }
            var part = mesh.MeshParts[0];
            _saved.Add(new Saved(part));
            part.VertexBuffer = chassis ? _chassisVb : _cannonVb;
            part.IndexBuffer = chassis ? _chassisIb : _cannonIb;
            part.NumVertices = chassis ? _chassisVerts : _cannonVerts;
            part.PrimitiveCount = (chassis ? _chassisVerts : _cannonVerts) / 3;
            part.StartIndex = 0;
            part.VertexOffset = 0;
        }
        // painted skins are detailed: smooth them (the stock 32x32 textures are drawn blocky)
        _savedSampler = gd.SamplerStates[0];
        gd.SamplerStates[0] = SamplerState.LinearClamp;
        return true;
    }

    public void End() {
        foreach (var s in _saved) {
            s.Part.VertexBuffer = s.Vb;
            s.Part.IndexBuffer = s.Ib;
            s.Part.NumVertices = s.NumVertices;
            s.Part.PrimitiveCount = s.PrimitiveCount;
            s.Part.StartIndex = s.StartIndex;
            s.Part.VertexOffset = s.VertexOffset;
        }
        if (_saved.Count > 0 && _savedSampler is not null)
            TankGame.Instance.GraphicsDevice.SamplerStates[0] = _savedSampler;
        _saved.Clear();
        _savedSampler = null;
    }

    /// <summary>(tests) the room's light, when no room is loaded.</summary>
    public static Vector3? PreviewAmbient;
    private static BasicEffect? _previewFx;
    /// <summary>Draws the shapes directly (tests, previews): the chassis with <paramref name="world"/>, the cannon
    /// turned by <paramref name="turretYaw"/> on top.</summary>
    public void DrawDirect(GraphicsDevice gd, Texture2D texture, Matrix world, float turretYaw, Matrix view, Matrix projection) {
        EnsureBuffers(gd);
        _previewFx ??= new BasicEffect(gd);
        var fx = _previewFx;
        fx.TextureEnabled = true;
        fx.VertexColorEnabled = true; // (as the game's tank models have it)
        fx.Texture = texture;
        fx.View = view;
        fx.Projection = projection;
        fx.SetDefaultGameLighting_IngameEntities(specular: IsPlayerModel, ambientMultiplier: IsPlayerModel ? 2f : 0.9f);
        if (PreviewAmbient is { } amb)
            fx.AmbientLightColor = amb;
        gd.SamplerStates[0] = SamplerState.LinearClamp;
        gd.RasterizerState = RasterizerState.CullCounterClockwise; // (as the game draws tanks: this shows the faces are wound the right way)
        gd.DepthStencilState = DepthStencilState.Default;
        gd.BlendState = BlendState.Opaque;
        for (int i = 0; i < 2; i++) {
            fx.World = i == 0 ? world : Matrix.CreateRotationY(turretYaw) * world;
            gd.SetVertexBuffer(i == 0 ? _chassisVb : _cannonVb);
            gd.Indices = i == 0 ? _chassisIb : _cannonIb;
            foreach (var pass in fx.CurrentTechnique.Passes) {
                pass.Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, (i == 0 ? _chassisVerts : _cannonVerts) / 3);
            }
        }
    }
}
