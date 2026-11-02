using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.Coordinates;
using TanksRebirth.GameContent.Systems.ParticleSystem;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.Troves;

public enum FloorTheme { Normal, Forest, Desert, IceCave, Volcano, Haunted }

/// <summary>
/// Special versions of Tank Troves floors: about 1 in 3 runs, a floor shows up themed (the same on every phone, from the
/// seed). A theme recolours the floor and blocks, scatters flat details on the ground, and adds one small twist:
/// Forest bushes hide you from enemies, Desert quicksand slows tanks, Ice Cave floors are slippery, Volcano lava cracks
/// burn, and the Haunted Vault is foggy.
/// </summary>
public static class TroveThemes {
    public const double THEME_CHANCE = 1.0 / 3.0;

    public sealed record ThemeDef(FloorTheme Theme, int Floor, string Name, Vector3 FloorTint, Vector3 BlockTint, Color Banner, string Twist);

    public static readonly ThemeDef[] All = [
        new(FloorTheme.Forest, 1, "The Forest", new(0.62f, 0.95f, 0.55f), new(0.75f, 0.95f, 0.65f), new Color(40, 110, 50), "Hide in bushes: enemies can't see you there"),
        new(FloorTheme.Desert, 2, "The Dunes", new(1.08f, 0.92f, 0.62f), new(1.05f, 0.9f, 0.65f), new Color(190, 140, 60), "Quicksand slows any tank in it"),
        new(FloorTheme.IceCave, 3, "The Ice Cave", new(0.78f, 0.92f, 1.08f), new(0.82f, 0.95f, 1.1f), new Color(80, 150, 210), "The floor is slippery"),
        new(FloorTheme.Volcano, 4, "The Volcano", new(0.6f, 0.35f, 0.3f), new(0.7f, 0.45f, 0.4f), new Color(160, 40, 20), "Glowing lava cracks burn"),
        new(FloorTheme.Haunted, 5, "The Haunted Vault", new(0.48f, 0.38f, 0.62f), new(0.55f, 0.45f, 0.7f), new Color(70, 40, 100), "Thick fog: you can only see close by"),
    ];

    /// <summary>This floor's theme for this run (Normal most of the time).</summary>
    public static FloorTheme PickFor(int floor, int seed) {
        if (TestTheme is { } tt && Def(tt)?.Floor == floor)
            return tt;
        var rng = new Random(unchecked(seed * 9277 + floor * 613));
        return rng.NextDouble() < THEME_CHANCE ? All.FirstOrDefault(t => t.Floor == floor)?.Theme ?? FloorTheme.Normal : FloorTheme.Normal;
    }
    public static ThemeDef? Def(FloorTheme t) => All.FirstOrDefault(d => d.Theme == t);
    /// <summary>For automated testing: this theme always shows up on its floor.</summary>
    public static FloorTheme? TestTheme;

    public static FloorTheme Current => TroveRun.Active && TroveRun.Floor is { } f ? f.Theme : FloorTheme.Normal;

    // ------------------------------------------------------------------ this room's details and hazards

    private enum Deco { Grass, Flower, Leaf, Bush, Pebble, Cactus, Quicksand, Snow, Crack, Lava, Scorch, Cobweb, Candle }

    /// <summary>Areas with an effect (bushes, quicksand, lava): where, how big.</summary>
    public static readonly List<(Vector2 At, float Radius)> Zones = [];
    private static readonly List<Particle> _decor = [];
    private static readonly Dictionary<Tank, Vector2> _slide = [];
    private static readonly Dictionary<Tank, double> _onLavaSince = [], _burnedAt = [];
    private static readonly Dictionary<Deco, Texture2D> _tex = [];

    private static double Now => TroveRun.PlayTime;

    /// <summary>A room just loaded: lay out its theme details (the same on every phone).</summary>
    public static void SetupRoom(TroveFloor floor, TroveRoom room, int seed) {
        ClearRoom();
        if (floor.Theme == FloorTheme.Normal)
            return;
        var rng = new Random(unchecked(seed * 41 + floor.Number * 1009 + room.Cell.X * 73 + room.Cell.Y * 29));
        var solid = new bool[TroveGen.GW, TroveGen.GH];
        foreach (var b in room.Blocks)
            solid[b.X, b.Y] = true;
        bool Free(int x, int y) => x >= 0 && y >= 0 && x < TroveGen.GW && y < TroveGen.GH && !solid[x, y]
            && !room.Teleporters.Any(t => t.X == x && t.Y == y);
        // hazards stay away from the middle (spawns and pickups) and the doorways
        bool HazardOk(int x, int y) => Free(x, y) && !(x is >= 7 and <= 14 && y is >= 5 and <= 11)
            && !(x is >= 9 and <= 12 && (y <= 2 || y >= TroveGen.GH - 3)) && !(y is >= 7 and <= 9 && (x <= 2 || x >= TroveGen.GW - 3));
        var cells = new List<Point>();
        for (int x = 0; x < TroveGen.GW; x++)
            for (int y = 0; y < TroveGen.GH; y++)
                if (Free(x, y))
                    cells.Add(new Point(x, y));
        var hazardCells = cells.Where(c => HazardOk(c.X, c.Y)).ToList();
        Vector2 W(Point c) => new BlockMapPosition(c.X, c.Y);
        Vector2 Jitter(Vector2 v) => v + new Vector2(rng.NextSingle() * 14 - 7, rng.NextSingle() * 14 - 7);

        void Scatter(Deco d, int n, float scale, float alpha = 1f) {
            for (int i = 0; i < n && cells.Count > 0; i++)
                Place(d, Jitter(W(cells[rng.Next(cells.Count)])), scale * (0.8f + rng.NextSingle() * 0.4f), rng.NextSingle() * MathHelper.TwoPi, alpha);
        }
        void Hazards(Deco d, int n, float radius, float scale) {
            for (int i = 0; i < n && hazardCells.Count > 0; i++) {
                var c = hazardCells[rng.Next(hazardCells.Count)];
                hazardCells.RemoveAll(h => Math.Abs(h.X - c.X) + Math.Abs(h.Y - c.Y) < 4);
                var at = W(c);
                Zones.Add((at, radius));
                Place(d, at, scale, rng.NextSingle() * MathHelper.TwoPi, 1f);
            }
        }

        switch (floor.Theme) {
            case FloorTheme.Forest:
                Scatter(Deco.Grass, 26, 0.55f, 0.9f);
                Scatter(Deco.Flower, 10, 0.4f);
                Scatter(Deco.Leaf, 16, 0.35f, 0.85f);
                Hazards(Deco.Bush, 3 + rng.Next(3), 24f, 1.1f);
                break;
            case FloorTheme.Desert:
                Scatter(Deco.Pebble, 22, 0.35f);
                Scatter(Deco.Cactus, 5, 0.6f);
                Hazards(Deco.Quicksand, 3 + rng.Next(2), 28f, 1.25f);
                break;
            case FloorTheme.IceCave:
                Scatter(Deco.Snow, 14, 0.9f, 0.75f);
                Scatter(Deco.Crack, 10, 0.6f, 0.6f);
                break;
            case FloorTheme.Volcano:
                Scatter(Deco.Scorch, 12, 0.8f, 0.6f);
                Hazards(Deco.Lava, 3 + rng.Next(3), 16f, 0.95f);
                break;
            case FloorTheme.Haunted:
                Scatter(Deco.Cobweb, 8, 0.8f, 0.7f);
                Scatter(Deco.Candle, 8, 0.4f);
                break;
        }
    }

    public static void ClearRoom() {
        foreach (var p in _decor)
            p.Destroy();
        _decor.Clear();
        Zones.Clear();
        _slide.Clear();
        _onLavaSince.Clear();
        _burnedAt.Clear();
    }

    private static void Place(Deco d, Vector2 at, float scale, float yaw, float alpha) {
        var tex = Texture(d);
        if (tex is null)
            return;
        var p = GameHandler.Particles.MakeParticle(new Vector3(at.X, 0.15f + _decor.Count * 0.001f, at.Y), tex);
        p.HasAdditiveBlending = d is Deco.Lava or Deco.Candle;
        p.Pitch = MathHelper.PiOver2;
        p.Yaw = yaw;
        p.Scale = new Vector3(scale);
        p.Alpha = alpha;
        p.Color = Color.White;
        if (d is Deco.Lava or Deco.Candle)
            p.UniqueBehavior = q => q.Alpha = 0.75f + 0.25f * MathF.Sin((float)Now * (d == Deco.Lava ? 2.5f : 7f) + q.Position.X);
        _decor.Add(p);
    }

    // ------------------------------------------------------------------ the twists

    public static bool InZone(Vector2 p) => Zones.Any(z => Vector2.Distance(z.At, p) < z.Radius);

    /// <summary>Forest: a player in a bush can't be seen by enemies.</summary>
    public static bool HiddenInBush(Tank t) => Current == FloorTheme.Forest && t is PlayerTank && InZone(t.Position);

    /// <summary>Desert quicksand slows; Ice Cave floors slide.</summary>
    public static Vector2? FilterVelocity(Tank t, Vector2 v) {
        switch (Current) {
            case FloorTheme.Desert:
                return InZone(t.Position) ? v * 0.5f : null;
            case FloorTheme.IceCave: {
                // keep most of the last movement: turning and stopping take a moment
                var last = _slide.TryGetValue(t, out var l) ? l : v;
                var now = Vector2.Lerp(last, v, 0.06f * TanksRebirth.RuntimeData.DeltaTime);
                _slide[t] = now;
                return now;
            }
        }
        return null;
    }

    /// <summary>Every update: lava burns, volcano embers.</summary>
    public static void Update() {
        if (!TroveRun.Active || !CampaignGlobals.InMission)
            return;
        if (Current == FloorTheme.Volcano) {
            bool host = !Client.IsConnected() || Client.IsHost();
            foreach (var t in GameHandler.AllTanks) {
                if (t is null || t.IsDestroyed || t.Properties.Immortal)
                    continue;
                // each phone burns its own players; the host burns the enemies
                bool decides = t is PlayerTank pt ? TroveItems.IsLocal(pt.PlayerId) : host;
                if (!decides || !Zones.Any(z => Vector2.Distance(z.At, t.Position) < z.Radius)) {
                    _onLavaSince.Remove(t);
                    continue;
                }
                if (!_onLavaSince.TryGetValue(t, out var since))
                    _onLavaSince[t] = since = Now;
                if (Now - since > 0.6 && (!_burnedAt.TryGetValue(t, out var last) || Now - last > 1.5)) {
                    _burnedAt[t] = Now;
                    t.Damage(new TankHurtContextOther(null, TankHurtContextOther.HurtContext.FromIngame, "Lava"), true);
                    GameHandler.Particles.MakeSmallExplosion(t.Position3D, 4, 6, 0.8f, 4);
                }
            }
            // embers drifting up from the lava
            if (Zones.Count > 0 && Random.Shared.NextDouble() < 0.25 * TanksRebirth.RuntimeData.DeltaTime) {
                var z = Zones[Random.Shared.Next(Zones.Count)];
                var ember = GameHandler.Particles.MakeParticle(new Vector3(z.At.X + Random.Shared.NextSingle() * 20 - 10, 2, z.At.Y + Random.Shared.NextSingle() * 20 - 10), Texture(Deco.Candle)!);
                ember.HasAdditiveBlending = true;
                ember.FaceTowardsMe = false;
                ember.Pitch = MathHelper.PiOver2;
                ember.Scale = new Vector3(0.15f);
                ember.Color = new Color(255, 140, 40);
                ember.UniqueBehavior = q => {
                    q.Position += new Vector3(0, 0.35f, 0) * TanksRebirth.RuntimeData.DeltaTime;
                    q.Alpha -= 0.02f * TanksRebirth.RuntimeData.DeltaTime;
                    if (q.Alpha <= 0)
                        q.Destroy();
                };
            }
        }
    }

    // ------------------------------------------------------------------ the pictures (drawn once, in code)

    private static Texture2D? Texture(Deco d) {
        if (_tex.TryGetValue(d, out var t) && !t.IsDisposed)
            return t;
        var gd = TankGame.Instance?.GraphicsDevice;
        if (gd is null)
            return null;
        const int N = 64;
        var px = new Color[N * N];
        var rng = new Random((int)d * 7 + 3);
        void Dot(float cx, float cy, float r, Color c, float soft = 1.5f) {
            for (int y = Math.Max(0, (int)(cy - r - 2)); y < Math.Min(N, (int)(cy + r + 2)); y++)
                for (int x = Math.Max(0, (int)(cx - r - 2)); x < Math.Min(N, (int)(cx + r + 2)); x++) {
                    float dist = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Math.Clamp((r - dist) / soft, 0, 1) * c.A / 255f;
                    if (a <= 0) continue;
                    var o = px[y * N + x];
                    float oa = o.A / 255f, na = a + oa * (1 - a);
                    if (na <= 0) continue;
                    px[y * N + x] = new Color(
                        (byte)((c.R * a + o.R * oa * (1 - a)) / na), (byte)((c.G * a + o.G * oa * (1 - a)) / na),
                        (byte)((c.B * a + o.B * oa * (1 - a)) / na), (byte)(na * 255));
                }
        }
        void Line(float x0, float y0, float x1, float y1, float w, Color c) {
            int steps = (int)(MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0)) * 2) + 1;
            for (int i = 0; i <= steps; i++) {
                float k = i / (float)steps;
                Dot(x0 + (x1 - x0) * k, y0 + (y1 - y0) * k, w, c, 1f);
            }
        }
        switch (d) {
            case Deco.Grass:
                for (int i = 0; i < 7; i++) {
                    float a = -0.9f + i * 0.3f;
                    Line(32, 52, 32 + MathF.Sin(a) * 22, 52 - MathF.Cos(a) * 26, 1.6f, new Color(60 + i * 8, 150 + i * 6, 50));
                }
                break;
            case Deco.Flower: {
                var petal = rng.Next(3) switch { 0 => new Color(250, 240, 120), 1 => new Color(240, 130, 170), _ => new Color(250, 250, 250) };
                for (int i = 0; i < 5; i++)
                    Dot(32 + MathF.Cos(i * 1.256f) * 10, 32 + MathF.Sin(i * 1.256f) * 10, 8, petal);
                Dot(32, 32, 6, new Color(240, 170, 40));
                break;
            }
            case Deco.Leaf:
                Dot(26, 32, 13, new Color(170, 120, 40));
                Dot(38, 32, 13, new Color(185, 110, 35));
                Line(14, 32, 50, 32, 1, new Color(110, 70, 20));
                break;
            case Deco.Bush:
                for (int i = 0; i < 9; i++)
                    Dot(32 + MathF.Cos(i * 0.7f) * (i % 3) * 7, 32 + MathF.Sin(i * 0.7f) * (i % 3) * 7, 15 - (i % 3) * 2, new Color(40 + i * 6, 110 + i * 7, 45), 3f);
                break;
            case Deco.Pebble:
                Dot(28, 34, 9, new Color(160, 140, 115));
                Dot(38, 28, 6, new Color(185, 165, 130));
                break;
            case Deco.Cactus:
                Dot(32, 32, 16, new Color(70, 140, 70));
                Dot(32, 32, 11, new Color(90, 165, 85));
                for (int i = 0; i < 8; i++)
                    Line(32 + MathF.Cos(i * 0.785f) * 14, 32 + MathF.Sin(i * 0.785f) * 14, 32 + MathF.Cos(i * 0.785f) * 20, 32 + MathF.Sin(i * 0.785f) * 20, 0.7f, new Color(240, 240, 200));
                break;
            case Deco.Quicksand:
                for (int r = 30; r > 4; r -= 6)
                    Dot(32, 32, r, r % 12 == 0 ? new Color(170, 130, 70, 220) : new Color(140, 100, 50, 220), 3f);
                break;
            case Deco.Snow:
                for (int i = 0; i < 6; i++)
                    Dot(24 + rng.Next(16), 24 + rng.Next(16), 9 + rng.Next(8), new Color(245, 250, 255, 220), 4f);
                break;
            case Deco.Crack:
            case Deco.Lava:
            case Deco.Scorch: {
                if (d == Deco.Scorch) {
                    Dot(32, 32, 24, new Color(30, 20, 15, 170), 10f);
                    break;
                }
                var col = d == Deco.Lava ? new Color(255, 120, 30) : new Color(170, 210, 240);
                float x = 6, y = 32;
                for (int i = 0; i < 6; i++) {
                    float nx = x + 9, ny = 32 + rng.Next(-12, 13);
                    if (d == Deco.Lava)
                        Line(x, y, nx, ny, 4.5f, new Color(140, 30, 10));
                    Line(x, y, nx, ny, d == Deco.Lava ? 2.4f : 1.2f, col);
                    if (i == 2 || i == 4)
                        Line(nx, ny, nx + rng.Next(-6, 7), ny + rng.Next(-14, 15), d == Deco.Lava ? 1.8f : 0.9f, col);
                    x = nx; y = ny;
                }
                break;
            }
            case Deco.Cobweb:
                for (int i = 0; i < 6; i++)
                    Line(32, 32, 32 + MathF.Cos(i * 1.047f) * 30, 32 + MathF.Sin(i * 1.047f) * 30, 0.6f, new Color(230, 230, 240, 200));
                for (int r = 8; r < 30; r += 7)
                    for (int i = 0; i < 6; i++)
                        Line(32 + MathF.Cos(i * 1.047f) * r, 32 + MathF.Sin(i * 1.047f) * r, 32 + MathF.Cos((i + 1) * 1.047f) * r, 32 + MathF.Sin((i + 1) * 1.047f) * r, 0.5f, new Color(230, 230, 240, 170));
                break;
            case Deco.Candle:
                Dot(32, 32, 26, new Color(255, 190, 90, 70), 18f);
                Dot(32, 32, 10, new Color(255, 230, 150, 200), 6f);
                Dot(32, 32, 4, new Color(255, 255, 230), 2f);
                break;
        }
        var tex = new Texture2D(gd, N, N);
        tex.SetData(px);
        _tex[d] = tex;
        return tex;
    }
}
