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

public enum FloorTheme { Normal, Forest, Desert, IceCave, Volcano, Haunted, Factory, Swamp, Crystal, Blizzard, NeonCity, Carnival, ToxicLab, SpaceStation }

/// <summary>
/// Special versions of Tank Troves floors: about 1 in 3 runs, a floor shows up themed (the same on every phone, from the
/// seed). A theme recolours the floor and blocks, scatters flat details on the ground, and adds one small twist:
/// Forest bushes hide you from enemies, Desert quicksand slows tanks, Ice Cave floors are slippery, Volcano lava cracks
/// burn, and the Haunted Vault is foggy.
/// </summary>
public static class TroveThemes {
    public const double THEME_CHANCE = 0.45;

    public sealed record ThemeDef(FloorTheme Theme, int Floor, string Name, Vector3 FloorTint, Vector3 BlockTint, Color Banner, string Twist);

    public static readonly ThemeDef[] All = [
        new(FloorTheme.Forest, 1, "The Forest", new(0.62f, 0.95f, 0.55f), new(0.75f, 0.95f, 0.65f), new Color(40, 110, 50), "Hide in bushes: enemies can't see you there"),
        new(FloorTheme.Desert, 2, "The Dunes", new(1.08f, 0.92f, 0.62f), new(1.05f, 0.9f, 0.65f), new Color(190, 140, 60), "Quicksand slows any tank in it"),
        new(FloorTheme.IceCave, 3, "The Ice Cave", new(0.78f, 0.92f, 1.08f), new(0.82f, 0.95f, 1.1f), new Color(80, 150, 210), "The floor is slippery"),
        new(FloorTheme.Volcano, 4, "The Volcano", new(0.6f, 0.35f, 0.3f), new(0.7f, 0.45f, 0.4f), new Color(160, 40, 20), "Glowing lava cracks burn"),
        new(FloorTheme.Haunted, 5, "The Haunted Vault", new(0.48f, 0.38f, 0.62f), new(0.55f, 0.45f, 0.7f), new Color(70, 40, 100), "Thick fog: you can only see close by"),
        new(FloorTheme.Blizzard, 1, "The Blizzard", new(0.8f, 0.86f, 0.96f), new(0.84f, 0.89f, 0.97f), new Color(150, 180, 210), "Snow drifts slow you down, and the snow hides what's far away"),
        new(FloorTheme.Swamp, 2, "The Swamp", new(0.62f, 0.72f, 0.48f), new(0.6f, 0.68f, 0.5f), new Color(70, 95, 50), "Sticky mud pools slow tanks down"),
        new(FloorTheme.Factory, 3, "The Factory", new(0.62f, 0.66f, 0.72f), new(0.7f, 0.72f, 0.78f), new Color(150, 30, 30), "Red alert! Steam vents burst every few seconds"),
        new(FloorTheme.Crystal, 4, "The Crystal Caves", new(0.42f, 0.42f, 0.62f), new(0.5f, 0.5f, 0.72f), new Color(60, 120, 170), "A dark cave lit by glowing crystals"),
        new(FloorTheme.NeonCity, 2, "Neon City", new(0.55f, 0.55f, 0.72f), new(0.6f, 0.6f, 0.78f), new Color(200, 40, 160), "Rainy neon streets: puddles are slippery"),
        new(FloorTheme.Carnival, 3, "The Haunted Carnival", new(0.62f, 0.46f, 0.55f), new(0.7f, 0.55f, 0.62f), new Color(180, 70, 30), "Spinning carnival lights and grinning jack-o'-lanterns"),
        new(FloorTheme.ToxicLab, 4, "The Toxic Lab", new(0.55f, 0.7f, 0.55f), new(0.6f, 0.72f, 0.62f), new Color(60, 160, 40), "Glowing acid puddles burn"),
        new(FloorTheme.SpaceStation, 5, "The Space Station", new(0.6f, 0.66f, 0.82f), new(0.66f, 0.72f, 0.86f), new Color(40, 70, 150), "Low gravity: tanks glide"),
        new(FloorTheme.Factory, 4, "The Foundry", new(0.6f, 0.55f, 0.55f), new(0.68f, 0.62f, 0.6f), new Color(150, 30, 30), "Red alert! Steam vents burst every few seconds"),
    ];
    /// <summary>The theme's look for a floor (a theme can show up on more than one floor, under another name).</summary>
    public static ThemeDef? Def(FloorTheme t, int floor) => All.FirstOrDefault(d => d.Theme == t && d.Floor == floor) ?? Def(t);

    /// <summary>This floor's theme for this run (Normal most of the time).</summary>
    public static FloorTheme PickFor(int floor, int seed) {
        if (TestTheme is { } tt && Def(tt)?.Floor == floor)
            return tt;
        var rng = new Random(unchecked(seed * 9277 + floor * 613));
        if (rng.NextDouble() >= THEME_CHANCE)
            return FloorTheme.Normal;
        var options = All.Where(t => t.Floor == floor).ToArray();
        return options.Length == 0 ? FloorTheme.Normal : options[rng.Next(options.Length)].Theme;
    }
    public static ThemeDef? Def(FloorTheme t) => All.FirstOrDefault(d => d.Theme == t);
    /// <summary>For automated testing: this theme always shows up on its floor.</summary>
    public static FloorTheme? TestTheme;

    public static FloorTheme Current => TroveRun.Active && TroveRun.Floor is { } f ? f.Theme : FloorTheme.Normal;

    // ------------------------------------------------------------------ this room's details and hazards

    private enum Deco { Grass, Flower, Leaf, Bush, Pebble, Cactus, Quicksand, Snow, Crack, Lava, Scorch, Cobweb, Candle, Grate, Stripes, Mud, LilyPad, Reed, Crystal, Drift, Neon, Puddle, Pumpkin, Acid, Vat, Panel, Console }

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
            case FloorTheme.Factory:
                Scatter(Deco.Scorch, 8, 0.7f, 0.5f); // oil stains
                Scatter(Deco.Stripes, 6, 0.7f, 0.85f);
                Hazards(Deco.Grate, 3 + rng.Next(3), 18f, 0.8f);
                // spinning red alarm lights in the corners of the room
                foreach (var (ax, ay) in new[] { (1, 1), (TroveGen.GW - 2, 1), (1, TroveGen.GH - 2), (TroveGen.GW - 2, TroveGen.GH - 2) })
                    Beacons.Add(new BlockMapPosition(ax, ay));
                break;
            case FloorTheme.Swamp:
                Scatter(Deco.LilyPad, 12, 0.5f, 0.9f);
                Scatter(Deco.Reed, 14, 0.55f);
                Hazards(Deco.Mud, 3 + rng.Next(3), 30f, 1.3f);
                break;
            case FloorTheme.Crystal:
                Scatter(Deco.Pebble, 12, 0.3f, 0.6f);
                for (int i = 0; i < 9 && cells.Count > 0; i++) {
                    var at = Jitter(W(cells[rng.Next(cells.Count)]));
                    var col = CrystalColors[rng.Next(CrystalColors.Length)];
                    Place(Deco.Crystal, at, 0.55f + rng.NextSingle() * 0.3f, rng.NextSingle() * MathHelper.TwoPi, 1f, col);
                    Glows.Add((at, col));
                }
                break;
            case FloorTheme.Blizzard:
                Scatter(Deco.Snow, 14, 1.0f, 0.5f);
                Hazards(Deco.Drift, 3 + rng.Next(3), 28f, 1.3f);
                break;
            case FloorTheme.NeonCity: {
                Hazards(Deco.Puddle, 3 + rng.Next(3), 26f, 1.25f);
                // neon signs in pink, blue and green, and warm streetlights that flicker
                for (int i = 0; i < 7 && cells.Count > 0; i++) {
                    var at = Jitter(W(cells[rng.Next(cells.Count)]));
                    var col = NeonColors[rng.Next(NeonColors.Length)];
                    Place(Deco.Neon, at, 0.7f, rng.NextSingle() * MathHelper.TwoPi, 1f, col);
                    Glows.Add((at, col));
                }
                foreach (var (sx, sy) in new[] { (3, 3), (TroveGen.GW - 4, 3), (3, TroveGen.GH - 4), (TroveGen.GW - 4, TroveGen.GH - 4) })
                    Flickers.Add(new BlockMapPosition(sx, sy));
                break;
            }
            case FloorTheme.Carnival:
                for (int i = 0; i < 6 && cells.Count > 0; i++) {
                    var at = Jitter(W(cells[rng.Next(cells.Count)]));
                    Place(Deco.Pumpkin, at, 0.55f, rng.NextSingle() * MathHelper.TwoPi, 1f);
                    Glows.Add((at, new Color(255, 140, 30)));
                }
                Scatter(Deco.Leaf, 10, 0.35f, 0.8f);
                Beacons.Add(Vector2.Zero); // the carousel light in the middle
                break;
            case FloorTheme.ToxicLab:
                Hazards(Deco.Acid, 3 + rng.Next(3), 18f, 1.0f);
                for (int i = 0; i < 4 && cells.Count > 0; i++) {
                    var at = Jitter(W(cells[rng.Next(cells.Count)]));
                    Place(Deco.Vat, at, 0.6f, 0f, 1f);
                    Glows.Add((at, new Color(90, 255, 80)));
                }
                Scatter(Deco.Scorch, 6, 0.6f, 0.4f);
                break;
            case FloorTheme.SpaceStation:
                Scatter(Deco.Panel, 14, 0.9f, 0.7f);
                for (int i = 0; i < 6 && cells.Count > 0; i++) {
                    var at = Jitter(W(cells[rng.Next(cells.Count)]));
                    Place(Deco.Console, at, 0.55f, rng.Next(4) * MathHelper.PiOver2, 1f);
                    Glows.Add((at, new Color(80, 160, 255)));
                }
                // warning strobes in two corners
                Beacons.Add(new BlockMapPosition(1, 1));
                Beacons.Add(new BlockMapPosition(TroveGen.GW - 2, TroveGen.GH - 2));
                break;
        }
    }

    public static void ClearRoom() {
        foreach (var p in _decor)
            p.Destroy();
        _decor.Clear();
        Zones.Clear();
        Beacons.Clear();
        Glows.Clear();
        Flickers.Clear();
        _slide.Clear();
        _onLavaSince.Clear();
        _burnedAt.Clear();
    }

    /// <summary>Factory: the red alarm lights; Crystal Caves: the crystals (where, what colour) for the dynamic lighting.</summary>
    public static readonly List<Vector2> Beacons = [];
    public static readonly List<(Vector2 At, Color Color)> Glows = [];
    /// <summary>Neon City's streetlights (they flicker).</summary>
    public static readonly List<Vector2> Flickers = [];
    public static readonly Color[] NeonColors = [new(255, 60, 200), new(60, 200, 255), new(80, 255, 140), new(255, 220, 60)];
    /// <summary>A streetlight is on (now and then it flickers off for a moment; every phone agrees).</summary>
    public static bool FlickerOn(int i) { double k = (Now * 3.1 + i * 7.3) % 11.0; return !(k > 10.2 && (int)(Now * 20) % 2 == 0); }
    /// <summary>The carnival's colours, cycling.</summary>
    public static Color CarnivalColor(float offset) {
        var cols = new[] { new Color(255, 70, 70), new Color(255, 200, 60), new Color(80, 220, 120), new Color(80, 160, 255), new Color(220, 90, 255) };
        int k = (int)(Now * 2 + offset) % cols.Length;
        return cols[(k + cols.Length) % cols.Length];
    }
    private static readonly Color[] CrystalColors = [new(90, 220, 255), new(220, 90, 255), new(120, 255, 170), new(255, 120, 200)];

    /// <summary>Lights for the dynamic lighting: the factory's spinning alarm lights and bursting vents, glowing crystals, lava.</summary>
    public static void AddLights() {
        if (!TroveRun.Active || !CampaignGlobals.InMission)
            return;
        float t = (float)Now;
        switch (Current) {
            case FloorTheme.Factory:
                for (int i = 0; i < Beacons.Count; i++) {
                    var b = Beacons[i];
                    Graphics.DynamicLighting.GameplayLights.AddBeacon(new Vector3(b.X, 45, b.Y), new Color(255, 30, 20), t * 3f + i * MathHelper.PiOver2);
                    Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(b.X, 30, b.Y), new Color(255, 40, 30), 0.8f + 0.6f * MathF.Sin(t * 6f), 90f);
                }
                foreach (var z in Zones)
                    if (Venting(z.At))
                        Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(z.At.X, 12, z.At.Y), new Color(255, 230, 220), 1.2f, 70f);
                break;
            case FloorTheme.Crystal:
                foreach (var (at, col) in Glows)
                    Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(at.X, 12, at.Y), col, 1.4f + 0.3f * MathF.Sin(t * 1.5f + at.X), 95f);
                break;
            case FloorTheme.Volcano:
                foreach (var z in Zones)
                    Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(z.At.X, 8, z.At.Y), new Color(255, 110, 30), 1.1f + 0.3f * MathF.Sin(t * 2.5f + z.At.X), 80f);
                break;
            case FloorTheme.NeonCity:
                foreach (var (at, col) in Glows)
                    Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(at.X, 10, at.Y), col, 1.5f + 0.4f * MathF.Sin(t * 9f + at.X), 85f);
                for (int i = 0; i < Flickers.Count; i++)
                    if (FlickerOn(i))
                        Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(Flickers[i].X, 40, Flickers[i].Y), new Color(255, 205, 140), 1.8f, 150f);
                break;
            case FloorTheme.Carnival:
                for (int i = 0; i < Glows.Count; i++) {
                    var (at, col) = Glows[i];
                    Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(at.X, 8, at.Y), col, 1.2f + 0.4f * MathF.Sin(t * 7f + i), 70f);
                }
                // the carousel: a spinning beam that changes colour
                Graphics.DynamicLighting.GameplayLights.AddBeacon(new Vector3(0, 50, 0), CarnivalColor(0), t * 2f);
                Graphics.DynamicLighting.GameplayLights.AddBeacon(new Vector3(0, 50, 0), CarnivalColor(2.5f), t * 2f + MathHelper.Pi);
                Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(0, 20, 0), CarnivalColor(1), 1.4f, 110f);
                break;
            case FloorTheme.ToxicLab:
                foreach (var z in Zones)
                    Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(z.At.X, 8, z.At.Y), new Color(100, 255, 70), 1.2f + 0.3f * MathF.Sin(t * 3f + z.At.X), 75f);
                foreach (var (at, col) in Glows)
                    Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(at.X, 25, at.Y), col, 1.3f, 100f);
                break;
            case FloorTheme.SpaceStation:
                // consoles blink; the strobes flash red and blue in turn
                for (int i = 0; i < Glows.Count; i++)
                    if ((int)(t * 2 + i * 0.7f) % 3 != 0)
                        Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(Glows[i].At.X, 10, Glows[i].At.Y), Glows[i].Color, 1.3f, 70f);
                for (int i = 0; i < Beacons.Count; i++) {
                    bool red = (int)(t * 1.5f) % 2 == i % 2;
                    float flash = MathF.Max(0f, MathF.Sin(t * 9.4f));
                    Graphics.DynamicLighting.GameplayLights.AddGlow(new Vector3(Beacons[i].X, 40, Beacons[i].Y), red ? new Color(255, 40, 40) : new Color(60, 120, 255), 0.5f + 2.2f * flash, 200f);
                }
                break;
        }
    }

    /// <summary>Factory steam vents: each one bursts for a second every 5 seconds (worked out from the clock, so every phone agrees).</summary>
    public const float VENT_EVERY = 5f, VENT_BURST = 1.1f;
    public static float VentPhase(Vector2 at) => (Math.Abs(at.X * 0.37f + at.Y * 0.61f) % VENT_EVERY);
    public static bool Venting(Vector2 at) => (Now + VentPhase(at)) % VENT_EVERY > VENT_EVERY - VENT_BURST;
    public static bool VentWarning(Vector2 at) { var k = (Now + VentPhase(at)) % VENT_EVERY; return k > VENT_EVERY - VENT_BURST - 0.8 && k <= VENT_EVERY - VENT_BURST; }

    private static void Place(Deco d, Vector2 at, float scale, float yaw, float alpha, Color? color = null) {
        var tex = Texture(d);
        if (tex is null)
            return;
        var p = GameHandler.Particles.MakeParticle(new Vector3(at.X, 0.15f + _decor.Count * 0.001f, at.Y), tex);
        p.HasAdditiveBlending = d is Deco.Lava or Deco.Candle or Deco.Crystal;
        p.Pitch = MathHelper.PiOver2;
        p.Yaw = yaw;
        p.Scale = new Vector3(scale);
        p.Alpha = alpha;
        p.Color = color ?? Color.White;
        if (d is Deco.Neon or Deco.Acid or Deco.Pumpkin or Deco.Console or Deco.Vat)
            p.HasAdditiveBlending = d is Deco.Neon or Deco.Acid;
        if (d == Deco.Neon)
            p.UniqueBehavior = q => q.Alpha = 0.8f + 0.2f * MathF.Sin((float)Now * 9f + q.Position.X) * ((int)(Now * 13 + q.Position.Z) % 17 == 0 ? 3 : 0.3f);
        else if (d == Deco.Acid)
            p.UniqueBehavior = q => q.Alpha = 0.75f + 0.25f * MathF.Sin((float)Now * 3f + q.Position.X);
        else if (d is Deco.Lava or Deco.Candle or Deco.Crystal)
            p.UniqueBehavior = q => q.Alpha = 0.75f + 0.25f * MathF.Sin((float)Now * (d == Deco.Lava ? 2.5f : d == Deco.Crystal ? 1.5f : 7f) + q.Position.X);
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
            case FloorTheme.Swamp:
                return InZone(t.Position) ? v * 0.55f : null;
            case FloorTheme.Blizzard:
                return InZone(t.Position) ? v * 0.6f : null;
            case FloorTheme.NeonCity:
            case FloorTheme.SpaceStation: {
                // wet puddles (Neon City) or low gravity everywhere (Space Station): tanks keep sliding a little
                if (Current == FloorTheme.NeonCity && !InZone(t.Position)) {
                    _slide.Remove(t);
                    return null;
                }
                var last = _slide.TryGetValue(t, out var l) ? l : v;
                var now = Vector2.Lerp(last, v, MathF.Min(1f, (Current == FloorTheme.SpaceStation ? 0.12f : 0.07f) * TanksRebirth.RuntimeData.DeltaTime));
                _slide[t] = now;
                return now;
            }
            case FloorTheme.IceCave: {
                // keep most of the last movement: turning and stopping take a moment
                var last = _slide.TryGetValue(t, out var l) ? l : v;
                var now = Vector2.Lerp(last, v, MathF.Min(1f, 0.06f * TanksRebirth.RuntimeData.DeltaTime));
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
        if (Current == FloorTheme.Factory) {
            bool host = !Client.IsConnected() || Client.IsHost();
            foreach (var z in Zones) {
                // a hiss of steam before it bursts, then a column of steam
                if ((Venting(z.At) || VentWarning(z.At)) && Random.Shared.NextDouble() < (Venting(z.At) ? 0.9 : 0.25) * TanksRebirth.RuntimeData.DeltaTime)
                    Puff(z.At, Venting(z.At));
                if (!Venting(z.At))
                    continue;
                foreach (var t in GameHandler.AllTanks) {
                    if (t is null || t.IsDestroyed || t.Properties.Immortal || Vector2.Distance(z.At, t.Position) > z.Radius)
                        continue;
                    bool decides = t is PlayerTank pt ? TroveItems.IsLocal(pt.PlayerId) : host;
                    if (!decides || _burnedAt.TryGetValue(t, out var last) && Now - last < VENT_EVERY - 1)
                        continue;
                    _burnedAt[t] = Now;
                    t.Damage(new TankHurtContextOther(null, TankHurtContextOther.HurtContext.FromIngame, "Steam"), true);
                }
            }
        }
        if (Current is FloorTheme.Volcano or FloorTheme.ToxicLab) {
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
                    t.Damage(new TankHurtContextOther(null, TankHurtContextOther.HurtContext.FromIngame, Current == FloorTheme.ToxicLab ? "Acid" : "Lava"), true);
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
                ember.Color = Current == FloorTheme.ToxicLab ? new Color(110, 255, 90) : new Color(255, 140, 40);
                ember.UniqueBehavior = q => {
                    q.Position += new Vector3(0, 0.35f, 0) * TanksRebirth.RuntimeData.DeltaTime;
                    q.Alpha -= 0.02f * TanksRebirth.RuntimeData.DeltaTime;
                    if (q.Alpha <= 0)
                        q.Destroy();
                };
            }
        }
    }

    private static void Puff(Vector2 at, bool big) {
        var tex = Texture(Deco.Snow);
        if (tex is null)
            return;
        var p = GameHandler.Particles.MakeParticle(new Vector3(at.X + Random.Shared.NextSingle() * 10 - 5, 3, at.Y + Random.Shared.NextSingle() * 10 - 5), tex);
        p.FaceTowardsMe = true;
        p.Scale = new Vector3(big ? 0.35f : 0.18f);
        p.Alpha = big ? 0.8f : 0.5f;
        p.Color = new Color(235, 235, 240);
        p.UniqueBehavior = q => {
            q.Position += new Vector3(0, big ? 1.4f : 0.5f, 0) * TanksRebirth.RuntimeData.DeltaTime;
            q.Scale += new Vector3(0.012f) * TanksRebirth.RuntimeData.DeltaTime;
            q.Alpha -= 0.02f * TanksRebirth.RuntimeData.DeltaTime;
            if (q.Alpha <= 0)
                q.Destroy();
        };
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
            case Deco.Grate:
                // a round steam vent with bars
                Dot(32, 32, 28, new Color(60, 62, 68), 2f);
                Dot(32, 32, 24, new Color(25, 25, 30), 2f);
                for (int i = 0; i < 5; i++)
                    Line(14, 18 + i * 7, 50, 18 + i * 7, 1.6f, new Color(110, 112, 120));
                break;
            case Deco.Stripes:
                // yellow and black hazard stripes on a floor plate
                for (int y = 20; y < 44; y++)
                    for (int x = 4; x < 60; x++)
                        px[y * N + x] = ((x + y) / 7) % 2 == 0 ? new Color(230, 190, 40, 230) : new Color(30, 30, 30, 230);
                break;
            case Deco.Mud:
                for (int r = 30; r > 4; r -= 5)
                    Dot(32 + (r % 3), 32 - (r % 4), r, r % 10 == 0 ? new Color(85, 70, 40, 225) : new Color(70, 58, 32, 225), 3f);
                Dot(24, 26, 3, new Color(120, 105, 70, 200));
                Dot(40, 38, 2, new Color(120, 105, 70, 200));
                break;
            case Deco.LilyPad:
                Dot(32, 32, 20, new Color(60, 140, 60));
                Line(32, 32, 50, 22, 3f, new Color(0, 0, 0, 0)); // the notch
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                        if (x > 32 && Math.Abs((y - 32) - (x - 32) * -0.5f) < 4)
                            px[y * N + x] = default;
                if (rng.Next(2) == 0)
                    Dot(26, 34, 5, new Color(250, 200, 220));
                break;
            case Deco.Reed:
                for (int i = 0; i < 4; i++) {
                    float x0 = 24 + i * 5;
                    Line(x0, 58, x0 + (i - 1.5f) * 4, 14 + i * 3, 1.3f, new Color(90, 120, 50));
                    Dot(x0 + (i - 1.5f) * 4, 16 + i * 3, 2.5f, new Color(110, 70, 35));
                }
                break;
            case Deco.Crystal:
                // a cluster of glowing shards (white, tinted by the particle colour)
                Dot(32, 32, 28, new Color(255, 255, 255, 60), 20f);
                for (int i = 0; i < 5; i++) {
                    float a = i * 1.256f + 0.3f;
                    Line(32, 32, 32 + MathF.Cos(a) * (14 + i * 2), 32 + MathF.Sin(a) * (14 + i * 2), 3.5f - i * 0.3f, new Color(230, 240, 255));
                }
                Dot(32, 32, 6, Color.White, 2f);
                break;
            case Deco.Drift:
                for (int i = 0; i < 7; i++)
                    Dot(32 + MathF.Cos(i * 0.9f) * (i % 3) * 8, 32 + MathF.Sin(i * 0.9f) * (i % 3) * 6, 16 - (i % 3) * 2, new Color(232, 240, 252, 190), 6f);
                break;
            case Deco.Neon:
                // a glowing neon tube sign (tinted by the particle colour)
                Dot(32, 32, 30, new Color(255, 255, 255, 50), 22f);
                Line(12, 22, 52, 22, 2.6f, Color.White);
                Line(12, 42, 52, 42, 2.6f, Color.White);
                Line(12, 22, 12, 42, 2.6f, Color.White);
                Line(52, 22, 52, 42, 2.6f, Color.White);
                Line(22, 28, 30, 36, 2f, Color.White);
                Line(30, 36, 42, 28, 2f, Color.White);
                break;
            case Deco.Puddle:
                Dot(32, 32, 28, new Color(60, 80, 120, 150), 6f);
                Dot(26, 28, 9, new Color(180, 200, 255, 90), 6f);
                Dot(40, 38, 5, new Color(255, 120, 220, 70), 4f);
                break;
            case Deco.Pumpkin:
                Dot(32, 34, 22, new Color(230, 110, 20));
                Dot(22, 34, 14, new Color(245, 130, 30));
                Dot(42, 34, 14, new Color(245, 130, 30));
                Line(32, 12, 34, 6, 2.5f, new Color(70, 110, 40));
                // the face, glowing
                Dot(24, 30, 4, new Color(255, 240, 120));
                Dot(40, 30, 4, new Color(255, 240, 120));
                Line(22, 40, 42, 40, 2.4f, new Color(255, 240, 120));
                break;
            case Deco.Acid:
                for (int i = 0; i < 6; i++)
                    Dot(32 + MathF.Cos(i * 1.1f) * (i % 3) * 7, 32 + MathF.Sin(i * 1.1f) * (i % 3) * 6, 15 - (i % 3) * 2, new Color(110, 255, 80, 210), 5f);
                Dot(28, 30, 3, new Color(220, 255, 200));
                Dot(38, 36, 2, new Color(220, 255, 200));
                break;
            case Deco.Vat:
                Dot(32, 32, 26, new Color(80, 85, 95), 2f);
                Dot(32, 32, 21, new Color(60, 220, 60), 2f);
                Dot(26, 28, 4, new Color(200, 255, 190));
                Dot(38, 36, 3, new Color(200, 255, 190));
                break;
            case Deco.Panel:
                for (int y = 8; y < 56; y++)
                    for (int x = 8; x < 56; x++)
                        px[y * N + x] = (x == 8 || y == 8 || x == 55 || y == 55) ? new Color(60, 70, 90, 200) : new Color(120, 130, 150, 120);
                Dot(12, 12, 1.6f, new Color(40, 45, 55));
                Dot(51, 12, 1.6f, new Color(40, 45, 55));
                Dot(12, 51, 1.6f, new Color(40, 45, 55));
                Dot(51, 51, 1.6f, new Color(40, 45, 55));
                break;
            case Deco.Console:
                for (int y = 16; y < 48; y++)
                    for (int x = 6; x < 58; x++)
                        px[y * N + x] = new Color(30, 34, 44);
                for (int i = 0; i < 5; i++)
                    Dot(14 + i * 9, 26, 2.5f, i % 2 == 0 ? new Color(90, 180, 255) : new Color(255, 80, 80));
                Line(12, 38, 52, 38, 1.5f, new Color(90, 180, 255));
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
