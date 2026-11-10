using System;
using System.Collections.Generic;
using System.Linq;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.Systems.Troves;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.UI;

/// <summary>Tank Troves on screen: the mouse-hole doors, the minimap, the floor name, room fades and the end screen.</summary>
public static class TroveHud {
    private static Texture2D? _hole, _holeLocked, _holeBoss, _holeItem, _holeChallenge, _holeSecret, _holeMini;
    private static Vector2 _press = new(-1);
    private static bool _down, _wasDown;

    public static void EndShown() => _down = _wasDown = true;

    private static float S => WindowUtils.WindowHeight / 1080f;

    internal static Vector2 ToScreen(Vector2 world) =>
        MatrixUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(world.X, 0, world.Y), CameraGlobals.GameView, CameraGlobals.GameProjection);

    public static void Draw(SpriteBatch sb) {
        Internals.Common.Framework.Input.TouchControls.ItemSlots.Clear(); // (re-added below while there are items to tap)
        if (!TroveRun.Active || MainMenu.MainMenuUI.IsActive)
            return;
        EnsureTextures(sb.GraphicsDevice);
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        // not while the floor banner (intermission) is up
        bool banner = IntermissionSystem.Alpha > 0.05f || IntermissionSystem.BlackAlpha > 0.05f;
        if (!banner && TroveRun.Floor is { } floor && TroveRun.Fade < 1f) {
            DrawFog(sb);
            DrawShellGlows(sb);
            DrawInRoom(sb);
            DrawItemStrip(sb);
            if (TroveRun.Battle)
                BattleHud.DrawInGame(sb);
            else {
                DrawMap(sb, floor);
                DrawBossBar(sb);
            }
            DrawPickupBanner(sb);
            DrawSynergyBanner(sb);
            DrawReadyGo(sb);
            DrawBroken(sb);
        }
        if (TroveRun.Fade > 0)
            sb.Draw(TextureGlobals.Pixels[Color.White], new Rectangle(0, 0, WindowUtils.WindowWidth, WindowUtils.WindowHeight), Color.Black * TroveRun.Fade);
        sb.End();
        if (TroveRun.ShowingEnd)
            DrawEnd(sb);
    }

    // ---- doors: dark arched "mouse holes" in the middle of each wall that leads somewhere
    /// <summary>The doors, drawn inside the game's HUD batch so the mission strip at the bottom covers them.</summary>
    public static void DrawDoorsUnderHud(SpriteBatch sb) {
        if (!TroveRun.Active || MainMenu.MainMenuUI.IsActive || TroveRun.Fade >= 1f)
            return;
        if (IntermissionSystem.Alpha > 0.05f || IntermissionSystem.BlackAlpha > 0.05f)
            return;
        if (TroveRun.Floor is not { } floor || TroveRun.Room is not { } room)
            return;
        EnsureTextures(sb.GraphicsDevice);
        DrawDoors(sb, floor, room);
    }

    // ---- floor themes on screen: the Haunted Vault's fog, the Crystal Caves' dark, the blizzard's snow, the swamp haze,
    //      the factory's red alert
    private static Texture2D? _fogHole, _vignette;
    private static readonly (float X, float Y, float Speed, float Size)[] _flakes = Enumerable.Range(0, 90)
        .Select(i => { var r = new Random(i * 7919); return (r.NextSingle(), r.NextSingle(), 0.6f + r.NextSingle(), 2f + r.NextSingle() * 4f); }).ToArray();

    private static void DrawFog(SpriteBatch sb) {
        if (!CampaignGlobals.InMission)
            return;
        var theme = TroveThemes.Current;
        if (theme == FloorTheme.Normal)
            return;
        EnsureFogTextures(sb.GraphicsDevice);
        float s = S;
        float t = (float)TroveRun.PlayTime;
        int W = WindowUtils.WindowWidth, H = WindowUtils.WindowHeight;
        int me = Systems.LocalCoop.Active ? 0 : Net.NetPlay.GetMyClientId();
        var tank = GameHandler.AllPlayerTanks[Math.Clamp(me, 0, 3)] is { IsDestroyed: false } mt ? mt : null;
        switch (theme) {
            case FloorTheme.Haunted:
                if (tank is not null)
                    FogAround(sb, ToScreen(tank.Position), 420 * s * (1f + 0.03f * MathF.Sin(t * 2f)), new Color(10, 5, 20) * 0.88f);
                break;
            case FloorTheme.Crystal:
                // a dark cave: you see around you, and the crystals glow through the dark
                if (tank is not null)
                    FogAround(sb, ToScreen(tank.Position), 560 * s, new Color(4, 6, 22) * 0.72f);
                foreach (var (at, col) in TroveThemes.Glows) {
                    var c = ToScreen(at);
                    float p = 0.8f + 0.2f * MathF.Sin(t * 1.5f + at.X);
                    ToggleSwitch.DrawDisc(sb, c, 70 * s, col * (0.10f * p));
                    ToggleSwitch.DrawDisc(sb, c, 36 * s, col * (0.18f * p));
                    ToggleSwitch.DrawDisc(sb, c, 12 * s, Color.Lerp(col, Color.White, 0.6f) * (0.6f * p));
                }
                break;
            case FloorTheme.Blizzard: {
                // a white haze toward the edges and snow blowing across the screen
                sb.Draw(_vignette, new Rectangle(0, 0, W, H), new Color(235, 242, 255) * 0.55f);
                foreach (var (fx, fy, sp, sz) in _flakes) {
                    float x = (fx * W + t * 220 * s * sp) % (W + 40) - 20;
                    float y = (fy * H + t * 90 * s * sp + MathF.Sin(t * 2 + fx * 20) * 20 * s) % (H + 40) - 20;
                    ToggleSwitch.DrawDisc(sb, new Vector2(x, y), sz * s, Color.White * 0.75f);
                }
                break;
            }
            case FloorTheme.Swamp:
                sb.Draw(_vignette, new Rectangle(0, 0, W, H), new Color(40, 70, 30) * 0.35f);
                break;
            case FloorTheme.NeonCity: {
                // rain streaking down, and the signs glowing through the dark
                sb.Draw(_vignette, new Rectangle(0, 0, W, H), new Color(20, 10, 50) * 0.55f);
                foreach (var (at, col) in TroveThemes.Glows) {
                    var c = ToScreen(at);
                    ToggleSwitch.DrawDisc(sb, c, 46 * s, col * (0.12f + 0.04f * MathF.Sin(t * 9f + at.X)));
                }
                foreach (var (fx, fy, sp, sz) in _flakes) {
                    float x = (fx * W + t * 120 * s * sp) % (W + 40) - 20;
                    float y = (fy * H + t * 900 * s * sp) % (H + 60) - 30;
                    ToggleSwitch.DrawLine(sb, new Vector2(x, y), new Vector2(x - 6 * s, y + 26 * s), 1.6f * s, new Color(170, 190, 255) * 0.45f);
                }
                break;
            }
            case FloorTheme.Carnival: {
                sb.Draw(_vignette, new Rectangle(0, 0, W, H), new Color(40, 10, 40) * 0.5f);
                // strings of coloured bulbs across the room, from corner to corner
                var corners = new[] { new Vector2(-260, -170), new Vector2(260, -170), new Vector2(260, 170), new Vector2(-260, 170) };
                for (int k = 0; k < 2; k++) {
                    var a0 = ToScreen(corners[k]);
                    var a1 = ToScreen(corners[k + 2]);
                    for (int i = 0; i <= 18; i++) {
                        float u = i / 18f;
                        var pnt = Vector2.Lerp(a0, a1, u) + new Vector2(0, MathF.Sin(MathHelper.Pi * u) * 30 * s);
                        if (i < 18) {
                            var nxt = Vector2.Lerp(a0, a1, (i + 1) / 18f) + new Vector2(0, MathF.Sin(MathHelper.Pi * (i + 1) / 18f) * 30 * s);
                            ToggleSwitch.DrawLine(sb, pnt, nxt, 1.5f * s, Color.Black * 0.6f);
                        }
                        var bc = TroveThemes.CarnivalColor(i * 0.5f + k);
                        ToggleSwitch.DrawDisc(sb, pnt, 9 * s, bc * 0.3f);
                        ToggleSwitch.DrawDisc(sb, pnt, 4 * s, Color.Lerp(bc, Color.White, 0.4f));
                    }
                }
                break;
            }
            case FloorTheme.ToxicLab:
                sb.Draw(_vignette, new Rectangle(0, 0, W, H), new Color(30, 90, 20) * (0.45f + 0.08f * MathF.Sin(t * 1.5f)));
                break;
            case FloorTheme.SpaceStation: {
                sb.Draw(_vignette, new Rectangle(0, 0, W, H), new Color(5, 10, 40) * 0.6f);
                // red / blue warning flashes at the edges
                float flash = MathF.Max(0f, MathF.Sin(t * 9.4f));
                bool red = (int)(t * 1.5f) % 2 == 0;
                sb.Draw(_vignette, new Rectangle(0, 0, W, H), (red ? new Color(255, 30, 30) : new Color(40, 100, 255)) * (0.18f * flash));
                break;
            }
            case FloorTheme.Factory: {
                // red alert: the edges of the screen pulse red
                float pulse = 0.5f + 0.5f * MathF.Sin(t * 4.5f);
                sb.Draw(_vignette, new Rectangle(0, 0, W, H), new Color(200, 10, 10) * (0.25f + 0.45f * pulse));
                sb.Draw(_fogHole!, new Rectangle(0, 0, W, H), new Rectangle(0, 0, 1, 1), new Color(255, 0, 0) * (0.05f * pulse));
                // steam vents about to burst: a warning ring
                foreach (var z in TroveThemes.Zones)
                    if (TroveThemes.VentWarning(z.At)) {
                        var c = ToScreen(z.At);
                        ToggleSwitch.DrawDisc(sb, c, 26 * s, new Color(255, 230, 230) * (0.25f + 0.25f * MathF.Sin(t * 20)));
                    }
                break;
            }
        }
    }

    /// <summary>On the dark floors, shells get a small soft white glow (drawn over the fog) so they're easy to see.</summary>
    private static void DrawShellGlows(SpriteBatch sb) {
        if (!CampaignGlobals.InMission || TroveThemes.Current is not (FloorTheme.Haunted or FloorTheme.Crystal or FloorTheme.NeonCity
            or FloorTheme.Carnival or FloorTheme.SpaceStation or FloorTheme.ToxicLab or FloorTheme.Factory))
            return;
        float s = S;
        foreach (var shell in Shell.AllShells) {
            if (shell is null)
                continue;
            var c = ToScreen(shell.RenderPosition) - new Vector2(0, 8 * s);
            var col = shell.Tint ?? Color.White;
            ToggleSwitch.DrawDisc(sb, c, 9 * s, col * 0.16f);
            ToggleSwitch.DrawDisc(sb, c, 5 * s, col * 0.28f);
        }
        // boost flames glow orange behind the tank
        for (int i = 0; i < 4; i++) {
            if (!TroveItems.Players[i].Boosting || GameHandler.AllPlayerTanks[i] is not { IsDestroyed: false } bt)
                continue;
            var back = bt.Velocity.LengthSquared() > 0.01f ? -Vector2.Normalize(bt.Velocity) : Vector2.Zero;
            var bc = ToScreen(bt.Position + back * 16f);
            ToggleSwitch.DrawDisc(sb, bc, 30 * s, new Color(255, 140, 40) * 0.12f);
            ToggleSwitch.DrawDisc(sb, bc, 16 * s, new Color(255, 180, 80) * 0.18f);
        }
        // mines glow yellow (red while about to go off), explosions light up the dark while they burn
        foreach (var mine in Mine.AllMines) {
            if (mine is null)
                continue;
            bool red = mine.DetonateTime < Mine.TICKS_OF_FLASHING && (int)(mine.DetonateTime / 8f) % 2 == 0;
            var col = red ? new Color(255, 60, 40) : new Color(255, 214, 70);
            var c = ToScreen(mine.Position);
            ToggleSwitch.DrawDisc(sb, c, 22 * s, col * 0.10f);
            ToggleSwitch.DrawDisc(sb, c, 13 * s, col * 0.16f);
        }
        foreach (var ex in Explosion.Explosions) {
            if (ex is null)
                continue;
            float t = ex.LifeTime / MathF.Max(ex.LingerDuration, 1f);
            float a = t < 0.7f ? 1f : MathHelper.Lerp(1f, 0.4f, (t - 0.7f) / 0.3f);
            var c = ToScreen(ex.Position);
            float r = ex.DamageRadiusScale * Explosion.MAGIC_EXPLOSION_NUMBER * s * 1.6f;
            ToggleSwitch.DrawDisc(sb, c, r * 1.6f, new Color(255, 150, 60) * (0.10f * a));
            ToggleSwitch.DrawDisc(sb, c, r, new Color(255, 190, 90) * (0.14f * a));
        }
    }

    private static void EnsureFogTextures(GraphicsDevice gd) {
        if (_fogHole is null || _fogHole.IsDisposed) {
            const int N = 256;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++) {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N / 2f)) / (N / 2f);
                    // a soft, natural falloff (smoothstep) that reaches full dark well inside the square
                    float a = Math.Clamp((d - 0.25f) / 0.65f, 0, 1);
                    a = a * a * (3 - 2 * a);
                    px[y * N + x] = new Color(0, 0, 0, (int)(a * 255));
                }
            _fogHole = new Texture2D(gd, N, N);
            _fogHole.SetData(px);
        }
        if (_vignette is null || _vignette.IsDisposed) {
            // white, see-through in the middle and solid at the edges (tinted when drawn)
            const int N = 128;
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++) {
                    float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1;
                    float d = MathF.Sqrt(dx * dx * 0.8f + dy * dy * 1.1f);
                    float a = Math.Clamp((d - 0.55f) / 0.75f, 0, 1);
                    a = a * a * (3 - 2 * a);
                    px[y * N + x] = new Color(255, 255, 255, (int)(a * 255));
                }
            _vignette = new Texture2D(gd, N, N);
            _vignette.SetData(px);
        }
    }

    /// <summary>Dark everywhere except a soft circle (the whole screen is drawn from one texture, so there are no seams).</summary>
    private static void FogAround(SpriteBatch sb, Vector2 c, float r, Color dark) {
        int W = WindowUtils.WindowWidth, H = WindowUtils.WindowHeight;
        var hole = new Rectangle((int)(c.X - r), (int)(c.Y - r), (int)(r * 2), (int)(r * 2));
        sb.Draw(_fogHole!, hole, dark);
        // the rest of the screen uses the same texture's fully dark corner texel, so it matches the edge of the hole exactly
        // (a plain white pixel came out a different shade on some phones, leaving a lighter square around the tank)
        var corner = new Rectangle(0, 0, 1, 1);
        sb.Draw(_fogHole!, new Rectangle(0, 0, W, Math.Max(0, hole.Top)), corner, dark);
        sb.Draw(_fogHole!, new Rectangle(0, hole.Bottom, W, Math.Max(0, H - hole.Bottom)), corner, dark);
        sb.Draw(_fogHole!, new Rectangle(0, hole.Top, Math.Max(0, hole.Left), hole.Height), corner, dark);
        sb.Draw(_fogHole!, new Rectangle(hole.Right, hole.Top, Math.Max(0, W - hole.Right), hole.Height), corner, dark);
    }

    // ---- things in the room: the item on the floor, orbiters, buddy/decoy tags, the Stopwatch freeze
    private static void DrawInRoom(SpriteBatch sb) {
        if (TroveRun.Room is not { } room || !CampaignGlobals.InMission)
            return;
        float s = S;
        if (TroveItems.Frozen) {
            sb.Draw(TextureGlobals.Pixels[Color.White], new Rectangle(0, 0, WindowUtils.WindowWidth, WindowUtils.WindowHeight), new Color(120, 200, 255) * 0.14f);
            Text(sb, $"FROZEN  {Math.Ceiling(TroveItems.FrozenUntil - TroveRun.PlayTime)}", new Vector2(WindowUtils.WindowWidth / 2f, 120 * s), 0.8f * s, new Color(190, 235, 255), border: true);
        }
        for (int i = 0; i < 4; i++)
            foreach (var (pos, r) in TroveItems.Orbiters(i)) {
                var c = ToScreen(pos);
                float rr = Math.Max(4f, Vector2.Distance(c, ToScreen(pos + new Vector2(r, 0))) * 0.45f);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, rr * 0.3f), rr * 1.05f, Color.Black * 0.3f);
                ToggleSwitch.DrawDisc(sb, c, rr, new Color(200, 210, 230));
                ToggleSwitch.DrawDisc(sb, c - new Vector2(rr * 0.25f), rr * 0.4f, Color.White);
            }
        // tanks on parachutes: a warning ring and a growing shadow where each will land, the tank swaying down under its chute
        foreach (var drop in TroveDrops.Pending) {
            if (TroveRun.PlayTime < drop.Start)
                continue;
            float p = TroveDrops.Progress(drop);
            var g = ToScreen(drop.At);
            float ring = 22 * s * (1f + 0.15f * MathF.Sin((float)TroveRun.PlayTime * 10f));
            for (int i = 0; i < 24; i++) {
                float a0 = i * MathHelper.TwoPi / 24, a1 = a0 + MathHelper.TwoPi / 24 * 0.6f;
                ToggleSwitch.DrawLine(sb, g + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * ring, g + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * ring, 3 * s, new Color(255, 60, 40) * 0.8f);
            }
            ToggleSwitch.DrawDisc(sb, g, (5 + 13 * p) * s, Color.Black * (0.15f + 0.35f * p));
            float sway = MathF.Sin((float)TroveRun.PlayTime * 2.5f + drop.At.X) * 10 * s * (1 - p);
            var body = g - new Vector2(-sway, (1 - p) * 330 * s + 6 * s);
            var canopy = body - new Vector2(0, 46 * s);
            float cr = 30 * s;
            // the chute: red and white panels in a dome
            for (int i = 0; i < 8; i++) {
                float a0 = MathHelper.Pi + i * MathHelper.Pi / 8, a1 = a0 + MathHelper.Pi / 8;
                var e0 = canopy + new Vector2(MathF.Cos(a0), MathF.Sin(a0) * 0.75f) * cr;
                var e1 = canopy + new Vector2(MathF.Cos(a1), MathF.Sin(a1) * 0.75f) * cr;
                var col = i % 2 == 0 ? new Color(230, 60, 50) : new Color(245, 240, 235);
                for (float k = 0.15f; k <= 1.01f; k += 0.17f)
                    ToggleSwitch.DrawLine(sb, Vector2.Lerp(canopy, e0, k), Vector2.Lerp(canopy, e1, k), 6 * s, col);
            }
            // strings, and the tank hanging under it: a little tank in its own colours, swinging gently
            var col2 = AITank.TankDestructionColors.TryGetValue(drop.Tier, out var tc) ? tc : Color.Gray;
            float tilt = MathF.Sin((float)TroveRun.PlayTime * 2.5f + drop.At.X) * 0.18f * (1 - p);
            float ts = (1.0f + 0.35f * (1 - p)) * s; // a bit bigger while it's high up (closer to the camera)
            Vector2 R(float x, float y) => body + new Vector2(x * MathF.Cos(tilt) - y * MathF.Sin(tilt), x * MathF.Sin(tilt) + y * MathF.Cos(tilt)) * ts;
            ToggleSwitch.DrawLine(sb, canopy + new Vector2(-cr, 0), R(-13, -9), 1.5f * s, Color.White * 0.8f);
            ToggleSwitch.DrawLine(sb, canopy + new Vector2(cr, 0), R(13, -9), 1.5f * s, Color.White * 0.8f);
            ToggleSwitch.DrawLine(sb, canopy + new Vector2(-cr * 0.4f, 0), R(-5, -9), 1.2f * s, Color.White * 0.6f);
            ToggleSwitch.DrawLine(sb, canopy + new Vector2(cr * 0.4f, 0), R(5, -9), 1.2f * s, Color.White * 0.6f);
            var dark = Color.Lerp(col2, Color.Black, 0.45f);
            var light = Color.Lerp(col2, Color.White, 0.25f);
            // treads, with tread marks
            ToggleSwitch.DrawLine(sb, R(-14, -10), R(-14, 10), 7 * ts, new Color(40, 36, 32));
            ToggleSwitch.DrawLine(sb, R(14, -10), R(14, 10), 7 * ts, new Color(40, 36, 32));
            for (int k = -4; k <= 4; k++) {
                ToggleSwitch.DrawLine(sb, R(-17, k * 2.3f), R(-11, k * 2.3f), 0.9f * ts, new Color(85, 80, 72));
                ToggleSwitch.DrawLine(sb, R(11, k * 2.3f), R(17, k * 2.3f), 0.9f * ts, new Color(85, 80, 72));
            }
            // hull
            ToggleSwitch.DrawLine(sb, R(0, -11), R(0, 11), 20 * ts, dark);
            ToggleSwitch.DrawLine(sb, R(0, -10), R(0, 10), 18 * ts, col2);
            ToggleSwitch.DrawLine(sb, R(-6, -8), R(-6, 6), 3 * ts, light * 0.6f);
            // turret and barrel (pointing down at where it lands)
            ToggleSwitch.DrawLine(sb, R(0, 2), R(0, 20), 4 * ts, dark);
            ToggleSwitch.DrawDisc(sb, R(0, 0), 7.5f * ts, dark);
            ToggleSwitch.DrawDisc(sb, R(0, 0), 6.2f * ts, col2);
            ToggleSwitch.DrawDisc(sb, R(-1.5f, -1.5f), 2.2f * ts, light);
        }

        // Charge Shot: a small ring next to the tank fills up while you hold fire
        foreach (var pt in GameHandler.AllPlayerTanks) {
            if (pt is not { IsDestroyed: false } || pt.ChargeFraction < 0)
                continue;
            float f = pt.ChargeFraction;
            var cc = ToScreen(pt.Position) + new Vector2(42 * s, -34 * s);
            float rr = 15 * s;
            bool full = f >= 1f;
            float pulse = full ? 0.75f + 0.25f * MathF.Sin((float)TroveRun.PlayTime * 18f) : 1f;
            ToggleSwitch.DrawDisc(sb, cc, rr + 3 * s, Color.Black * 0.5f);
            int segs = 28;
            for (int i = 0; i < segs; i++) {
                float a0 = -MathHelper.PiOver2 + i * MathHelper.TwoPi / segs, a1 = a0 + MathHelper.TwoPi / segs * 1.15f;
                bool on = i < (int)MathF.Ceiling(f * segs);
                var col = on ? (full ? Color.White : new Color(120, 200, 255)) : new Color(60, 70, 90);
                ToggleSwitch.DrawLine(sb, cc + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * rr, cc + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * rr, 5 * s, col * pulse);
            }
            if (full)
                ToggleSwitch.DrawDisc(sb, cc, rr * 0.55f, new Color(170, 230, 255) * pulse);
        }

        // Boost: a bar under each of this phone's tanks that empties while boosting
        for (int i = 0; i < 4; i++) {
            var bp = TroveItems.Players[i];
            if (bp.BoostMax <= 0 || bp.BoostFuel <= 0 || !TroveItems.IsLocal(i) || GameHandler.AllPlayerTanks[i] is not { IsDestroyed: false } bt)
                continue;
            float f = bp.BoostFuel / bp.BoostMax;
            float w = 64 * s * MathF.Sqrt(bp.BoostMax), h = 9 * s;
            var bc = ToScreen(bt.Position) + new Vector2(0, 38 * s);
            var outer = new Rectangle((int)(bc.X - w / 2 - 2 * s), (int)(bc.Y - h / 2 - 2 * s), (int)(w + 4 * s), (int)(h + 4 * s));
            ToggleSwitch.DrawRoundedRect(sb, outer, outer.Height / 2f, Color.Black * 0.55f);
            var col = f < 0.25f ? new Color(255, 80, 60) : bp.Boosting ? new Color(255, 225, 120) : new Color(255, 160, 40);
            if (f > 0)
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(bc.X - w / 2), (int)(bc.Y - h / 2), Math.Max((int)h, (int)(w * f)), (int)h), h / 2f, col);
        }

        // Pyro Baron's fire, Chrono Tank's time bubbles, the Phantom's laser sight
        float tnow = (float)TroveRun.PlayTime;
        foreach (var (at, until) in TroveBosses.Fires) {
            var c = ToScreen(at);
            float rr = Math.Max(6f, Vector2.Distance(c, ToScreen(at + new Vector2(TroveBosses.FIRE_RADIUS, 0))));
            float fade = (float)Math.Clamp(until - TroveRun.PlayTime, 0, 1);
            float fl = 0.8f + 0.2f * MathF.Sin(tnow * 13f + at.X);
            ToggleSwitch.DrawDisc(sb, c, rr * 1.3f, new Color(255, 80, 20) * (0.25f * fade));
            ToggleSwitch.DrawDisc(sb, c, rr * fl, new Color(255, 140, 30) * (0.55f * fade));
            ToggleSwitch.DrawDisc(sb, c - new Vector2(0, rr * 0.2f), rr * 0.45f * fl, new Color(255, 230, 120) * (0.7f * fade));
        }
        foreach (var (at, until) in TroveBosses.Bubbles) {
            var c = ToScreen(at);
            float rr = Vector2.Distance(c, ToScreen(at + new Vector2(TroveBosses.BUBBLE_RADIUS, 0)));
            float fade = (float)Math.Clamp(until - TroveRun.PlayTime, 0, 1);
            ToggleSwitch.DrawDisc(sb, c, rr, new Color(90, 150, 255) * (0.18f * fade));
            for (int i = 0; i < 36; i++) {
                float a0 = i * MathHelper.TwoPi / 36, a1 = (i + 1.05f) * MathHelper.TwoPi / 36;
                ToggleSwitch.DrawLine(sb, c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * rr, c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * rr, 3 * s, new Color(150, 200, 255) * (0.7f * fade));
            }
            // a slow clock hand
            float hand = tnow * 0.8f;
            ToggleSwitch.DrawLine(sb, c, c + new Vector2(MathF.Cos(hand), MathF.Sin(hand)) * rr * 0.6f, 3 * s, new Color(200, 225, 255) * (0.6f * fade));
        }
        if (TroveBosses.PhantomSight is { } sight) {
            var a0 = ToScreen(sight.From);
            var a1 = ToScreen(sight.To);
            float len = Vector2.Distance(a0, a1);
            var dir = len > 1 ? (a1 - a0) / len : Vector2.Zero;
            for (float dd = 0; dd < len; dd += 18 * s)
                ToggleSwitch.DrawLine(sb, a0 + dir * dd, a0 + dir * Math.Min(len, dd + 9 * s), 2.5f * s, new Color(255, 40, 40) * 0.75f);
        }
        // mortar shells: a red target circle where each will land, and the shell arcing over
        foreach (var (from, to, firedAt, landAt, _) in TroveEnemies.Mortars) {
            float k = (float)Math.Clamp((TroveRun.PlayTime - firedAt) / (landAt - firedAt), 0, 1);
            var tc = ToScreen(to);
            float rr = Math.Max(10f, Vector2.Distance(tc, ToScreen(to + new Vector2(42f, 0))));
            float blink = (int)(tnow * (6 + 10 * k)) % 2 == 0 ? 1f : 0.6f;
            ToggleSwitch.DrawDisc(sb, tc, rr, new Color(255, 40, 30) * (0.12f + 0.2f * k));
            ToggleSwitch.DrawDisc(sb, tc, rr * k, new Color(255, 60, 40) * (0.3f * blink));
            for (int i = 0; i < 24; i++) {
                float a0 = i * MathHelper.TwoPi / 24f, a1 = a0 + MathHelper.TwoPi / 48f;
                ToggleSwitch.DrawLine(sb, tc + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * rr, tc + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * rr, 2.5f * s, new Color(255, 70, 50) * blink);
            }
            ToggleSwitch.DrawLine(sb, tc - new Vector2(rr * 0.3f, 0), tc + new Vector2(rr * 0.3f, 0), 2f * s, new Color(255, 70, 50) * blink);
            ToggleSwitch.DrawLine(sb, tc - new Vector2(0, rr * 0.3f), tc + new Vector2(0, rr * 0.3f), 2f * s, new Color(255, 70, 50) * blink);
            // the shell: along the line, lifted into an arc, with its shadow on the ground
            var fc = ToScreen(from);
            var ground = Vector2.Lerp(fc, tc, k);
            float lift = MathF.Sin(k * MathF.PI) * 140f * s;
            ToggleSwitch.DrawDisc(sb, ground, 6f * s * (1 - 0.4f * MathF.Sin(k * MathF.PI)), Color.Black * 0.35f);
            ToggleSwitch.DrawDisc(sb, ground - new Vector2(0, lift), 8f * s, new Color(40, 40, 40));
            ToggleSwitch.DrawDisc(sb, ground - new Vector2(0, lift) - new Vector2(2, 2) * s, 3.5f * s, new Color(150, 150, 150));
        }
        foreach (var (sn, angle, fireAt) in TroveEnemies.SniperLines) {
            var a0 = ToScreen(sn.Position);
            var a1 = ToScreen(sn.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 500f);
            float k = (float)Math.Clamp(1 - (fireAt - TroveRun.PlayTime) / 1.1, 0, 1);
            ToggleSwitch.DrawLine(sb, a0, a1, (1.5f + 3 * k) * s, new Color(255, 40, 30) * (0.3f + 0.5f * k));
        }
        // special enemies: a small tag for the ones you'd want to know about
        foreach (var (et, ev) in TroveEnemies.All) {
            if (et.Properties.Invisible)
                continue;
            (string label, Color col)? tag = ev switch {
                EnemyVariant.Medic => ("+ MEDIC", new Color(90, 230, 110)),
                EnemyVariant.Sniper => ("SNIPER", new Color(255, 90, 80)),
                EnemyVariant.Kamikaze => ((int)(tnow * 4) % 2 == 0 ? "! ! !" : "", new Color(255, 70, 40)),
                EnemyVariant.Splitter => ("SPLITTER", new Color(255, 170, 230)),
                EnemyVariant.Bunker => ("MORTAR", new Color(255, 120, 60)),
                EnemyVariant.Nest => ("NEST", new Color(230, 110, 170)),
                _ => null,
            };
            if (tag is { } tg && tg.label.Length > 0)
                Text(sb, tg.label, ToScreen(et.Position) - new Vector2(0, 44 * s * et.Scaling.X), 0.36f * s, tg.col, border: true);
        }

        // boss warnings: red laser lines, artillery targets; the Shield Commander's shield; the Vault Keeper's orbiters
        foreach (var (from, angle, fireAt) in TroveBosses.Lines) {
            var a0 = ToScreen(from);
            var a1 = ToScreen(from + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 600f);
            float k = (float)Math.Clamp(1 - (fireAt - TroveRun.PlayTime), 0, 1);
            ToggleSwitch.DrawLine(sb, a0, a1, (4 + 6 * k) * s, new Color(255, 40, 30) * (0.35f + 0.45f * k));
        }
        foreach (var (at, land) in TroveBosses.Targets) {
            var c = ToScreen(at);
            float rr = Math.Max(10f, Vector2.Distance(c, ToScreen(at + new Vector2(45f, 0))));
            float k = (float)Math.Clamp(1 - (land - TroveRun.PlayTime) / 1.3, 0, 1);
            ToggleSwitch.DrawDisc(sb, c, rr, new Color(255, 40, 30) * 0.18f);
            ToggleSwitch.DrawDisc(sb, c, rr * k, new Color(255, 60, 40) * 0.35f);
            ToggleSwitch.DrawLine(sb, c - new Vector2(rr * 0.4f, 0), c + new Vector2(rr * 0.4f, 0), 3 * s, new Color(255, 80, 60));
            ToggleSwitch.DrawLine(sb, c - new Vector2(0, rr * 0.4f), c + new Vector2(0, rr * 0.4f), 3 * s, new Color(255, 80, 60));
        }
        if (TroveBosses.Current?.Kind == BossKind.ShieldCommander && TroveBosses.Boss is { IsDestroyed: false } sc) {
            var fwd = Vector2.UnitY.Rotate(sc.ChassisRotation);
            Vector2? prev = null;
            for (int i = -6; i <= 6; i++) {
                var pt = ToScreen(sc.Position + fwd.Rotate(i * 0.17f) * 30f * sc.Scaling.X);
                if (prev is { } pv)
                    ToggleSwitch.DrawLine(sb, pv, pt, 6 * s, new Color(120, 200, 255) * 0.85f);
                prev = pt;
            }
        }
        foreach (var o in TroveBosses.BossOrbiters()) {
            var c = ToScreen(o);
            float rr = Math.Max(4f, Vector2.Distance(c, ToScreen(o + new Vector2(6f, 0))));
            ToggleSwitch.DrawDisc(sb, c, rr, new Color(190, 120, 255));
        }

        // Magnet Master: a pulsing purple field while it's ready to catch a shell (shoot when it fades)
        if (TroveBosses.MagnetCharged && TroveRun.BossTank is { IsDestroyed: false } mag) {
            var c = ToScreen(mag.Position);
            float rr = Math.Max(14f, Vector2.Distance(c, ToScreen(mag.Position + new Vector2(34f, 0))));
            float pulse = 0.5f + 0.5f * MathF.Sin((float)TroveRun.PlayTime * 7f);
            ToggleSwitch.DrawDisc(sb, c, rr * (1.05f + 0.1f * pulse), new Color(200, 80, 255) * (0.14f + 0.1f * pulse));
        }

        // oil slicks: a dark, glossy puddle
        foreach (var (oat, ountil, _) in TroveItems.Oils) {
            var c = ToScreen(oat);
            float rr = Math.Max(14f, Vector2.Distance(c, ToScreen(oat + new Vector2(TroveItems.OIL_RADIUS, 0))));
            float fade = (float)Math.Clamp(ountil - TroveRun.PlayTime, 0, 1);
            ToggleSwitch.DrawDisc(sb, c, rr, new Color(15, 15, 22) * (0.75f * fade));
            ToggleSwitch.DrawDisc(sb, c + new Vector2(-rr * 0.3f, -rr * 0.25f), rr * 0.25f, new Color(120, 90, 200) * (0.25f * fade));
            ToggleSwitch.DrawDisc(sb, c + new Vector2(rr * 0.35f, rr * 0.2f), rr * 0.15f, new Color(80, 200, 180) * (0.2f * fade));
        }

        // gravity wells, rally flags, freeze bombs, frozen enemies, shockwaves
        foreach (var (wat, _, _) in TroveItems.Wells) {
            var c = ToScreen(wat);
            float rr = Math.Max(10f, Vector2.Distance(c, ToScreen(wat + new Vector2(30f, 0))));
            for (int k = 0; k < 3; k++) {
                float ph = (float)((TroveRun.PlayTime * 1.5 + k / 3.0) % 1.0);
                ToggleSwitch.DrawDisc(sb, c, rr * (1.6f - ph * 1.4f), new Color(120, 60, 200) * (0.25f * ph));
            }
            ToggleSwitch.DrawDisc(sb, c, rr * 0.35f, new Color(30, 10, 50));
        }
        foreach (var (fat, _, reach, _) in TroveItems.Flags) {
            var c = ToScreen(fat);
            float rr = Math.Max(20f, Vector2.Distance(c, ToScreen(fat + new Vector2(reach, 0))));
            ToggleSwitch.DrawDisc(sb, c, rr, new Color(220, 60, 60) * 0.08f);
            ToggleSwitch.DrawLine(sb, c, c - new Vector2(0, 60 * s), 4 * s, new Color(90, 70, 50));
            float wave = MathF.Sin((float)TroveRun.PlayTime * 6f) * 4 * s;
            ToggleSwitch.DrawLine(sb, c - new Vector2(0, 58 * s), c + new Vector2(32 * s, -46 * s + wave), 14 * s, new Color(220, 50, 50));
        }
        foreach (var (bat, _, _) in TroveItems.FreezeBombs) {
            var c = ToScreen(bat);
            ToggleSwitch.DrawDisc(sb, c, 12 * s, new Color(30, 60, 90));
            ToggleSwitch.DrawDisc(sb, c, 8 * s, new Color(150, 230, 255) * (0.6f + 0.4f * MathF.Sin((float)TroveRun.PlayTime * 8f)));
        }
        foreach (var (iced, until) in TroveItems.IcedUntil)
            if (!iced.IsDestroyed && until > TroveRun.PlayTime)
                ToggleSwitch.DrawDisc(sb, ToScreen(iced.Position), 30 * s, new Color(160, 230, 255) * 0.35f);
        foreach (var (wat, wt) in TroveItems.Waves) {
            float k = (float)Math.Clamp((TroveRun.PlayTime - wt) / 0.5, 0, 1);
            var c = ToScreen(wat);
            float rr = Math.Max(10f, Vector2.Distance(c, ToScreen(wat + new Vector2(130f * k, 0))));
            ToggleSwitch.DrawDisc(sb, c, rr, new Color(170, 230, 255) * (0.3f * (1 - k)));
        }

        // lightning
        foreach (var (from, to, _) in TroveItems.Bolts) {
            var a = ToScreen(from);
            var b2 = ToScreen(to);
            var rnd = new Random((int)(from.X * 7 + to.Y * 13));
            var prev = a;
            for (int k = 1; k <= 6; k++) {
                var pt = Vector2.Lerp(a, b2, k / 6f);
                if (k < 6)
                    pt += new Vector2(rnd.NextSingle() * 24 - 12, rnd.NextSingle() * 24 - 12) * s;
                ToggleSwitch.DrawLine(sb, prev, pt, 7 * s, new Color(120, 180, 255) * 0.6f);
                ToggleSwitch.DrawLine(sb, prev, pt, 3 * s, Color.White);
                prev = pt;
            }
        }
        for (int i = 0; i < 4; i++)
            if (TroveItems.Buddies[i] is { IsDestroyed: false } b)
                Text(sb, "BUDDY", ToScreen(b.Position) - new Vector2(0, 46 * s), 0.6f * s, new Color(150, 210, 255), border: true);
        for (int i = 0; i < 4; i++)
            if (TroveItems.Players[i].CloneTank is { IsDestroyed: false } cl)
                Text(sb, "CLONE", ToScreen(cl.Position) - new Vector2(0, 52 * s), 0.45f * s, new Color(150, 200, 255), border: true);
        foreach (var (tt, _) in TroveItems.Turrets)
            if (!tt.IsDestroyed)
                Text(sb, "TURRET", ToScreen(tt.Position) - new Vector2(0, 46 * s), 0.4f * s, new Color(220, 220, 170), border: true);
        if (TroveItems.EmpUntil > TroveRun.PlayTime)
            Text(sb, "EMP", new Vector2(WindowUtils.WindowWidth / 2f, 170 * s), 0.7f * s, new Color(120, 240, 255), border: true);
        if (TroveItems.DecoyTank is { IsDestroyed: false } d)
            Text(sb, "DECOY", ToScreen(d.Position) - new Vector2(0, 52 * s), 0.5f * s, new Color(255, 220, 140), border: true);
        if (room.PickupVisible)
            DrawPedestal(sb, room);
        // items found in broken cork: a small bobbing icon with a gold glow and its name
        foreach (var g in room.GroundDrops) {
            if (g.Taken) continue;
            var c = ToScreen(g.At);
            float r = Math.Max(10f, Vector2.Distance(c, ToScreen(g.At + new Vector2(Block.SIDE_LENGTH * 0.5f, 0))));
            float bob = MathF.Sin((float)TroveRun.PlayTime * 3f + g.At.X) * r * 0.15f;
            ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.2f), r * 0.75f, Color.Black * 0.3f);
            var at = c - new Vector2(0, r * 0.6f + bob);
            ToggleSwitch.DrawDisc(sb, at, r * 1.1f, new Color(255, 220, 120) * 0.35f);
            DrawItemIcon(sb, g.Item, at, r * 0.8f);
            Text(sb, TroveItems.Def(g.Item).Name, at - new Vector2(0, r * 1.5f), 0.38f * s, Color.White, border: true);
        }
    }

    // ---- the ITEM-button items you carry: a row of icons under the ITEM button (the one it uses is big and gold-ringed;
    //      tap another to switch, or X on a controller), and what the selected one does, in small text under them
    private static void DrawItemStrip(SpriteBatch sb) {
        if (!CampaignGlobals.InMission || TroveRun.ShowingEnd)
            return;
        int me = Systems.LocalCoop.Active ? 0 : Net.NetPlay.GetMyClientId();
        var p = TroveItems.Players[Math.Clamp(me, 0, 3)];
        if (p.Actives.Count == 0)
            return;
        float s = S;
        var tc = Internals.Common.Framework.Input.TouchControls.ItemButtonCenter;
        float R = Internals.Common.Framework.Input.TouchControls.ItemButtonRadius;
        float ir = R * 0.36f, gap = ir * 2.7f;
        int n = p.Actives.Count;
        var rowCenter = new Vector2(tc.X, tc.Y + R * 1.55f);
        // keep it on screen
        float half = (n - 1) * gap / 2f + ir * 1.4f;
        rowCenter.X = Math.Clamp(rowCenter.X, half + 8, WindowUtils.WindowWidth - half - 8);
        for (int i = 0; i < n; i++) {
            var id = p.Actives[i];
            bool sel = i == p.Selected;
            var at = rowCenter + new Vector2((i - (n - 1) / 2f) * gap, 0);
            float r = sel ? ir * 1.2f : ir * 0.9f;
            if (sel) {
                ToggleSwitch.DrawDisc(sb, at, r * 1.32f, new Color(255, 215, 80));
                ToggleSwitch.DrawDisc(sb, at, r * 1.16f, new Color(40, 30, 20));
            }
            bool ready = p.IsReady(id);
            if (ready && p.JustReady(id)) {
                // just came back: it blinks with a bright ring for a few seconds
                bool flash = (int)((TroveRun.PlayTime - p.ReadyTimeOf(id)) * 6) % 2 == 0;
                if (flash)
                    ToggleSwitch.DrawDisc(sb, at, r * 1.45f, new Color(255, 255, 200) * 0.85f);
            }
            DrawItemIcon(sb, id, at, r);
            if (!ready) {
                // dimmed while recharging, with a ring filling up and the seconds left
                ToggleSwitch.DrawDisc(sb, at, r * 1.02f, Color.Black * 0.55f);
                float f = p.RechargeFraction(id);
                const int segs = 24;
                for (int k = 0; k < segs; k++) {
                    float a0 = -MathHelper.PiOver2 + k * MathHelper.TwoPi / segs, a1 = a0 + MathHelper.TwoPi / segs * 1.1f;
                    ToggleSwitch.DrawLine(sb, at + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r * 1.12f, at + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r * 1.12f,
                        Math.Max(2f, r * 0.13f), k < (int)(f * segs) ? new Color(255, 215, 80) : Color.Black * 0.45f);
                }
                Text(sb, $"{Math.Ceiling(p.SecondsLeft(id))}s", at, 0.38f * s, Color.White, border: true);
            }
            // uses left before it breaks (red when it's the last one)
            int uses = p.UsesOf(id);
            if (TroveItems.Def(id).MaxUses > 1)
                Text(sb, $"x{uses}", at + new Vector2(r * 0.85f, r * 0.85f), 0.36f * s, uses <= 1 ? new Color(255, 90, 80) : Color.White, border: true);
            Internals.Common.Framework.Input.TouchControls.ItemSlots.Add((
                new Rectangle((int)(at.X - gap / 2f), (int)(at.Y - gap / 2f), (int)gap, (int)gap), i));
        }
        // the selected item's name and what it does (bright for a few seconds after it changes, then softer)
        if (p.Active is not { } a)
            return;
        var d = TroveItems.Def(a);
        double since = TroveRun.PlayTime - p.SelectedAt;
        float alpha = since < 4 ? 1f : 0.7f;
        float y = rowCenter.Y + ir * 2.1f;
        float lh = FontGlobals.RebirthFont.MeasureString("Ag").Y;
        int usesLeft = p.UsesOf(a);
        string usesText = d.MaxUses > 1 ? (usesLeft <= 1 ? "  (last use)" : $"  ({usesLeft} uses left)") : "";
        Text(sb, d.Name + usesText, new Vector2(rowCenter.X, y), 0.85f * s, Color.Lerp(d.Color, Color.White, 0.5f) * alpha, border: true);
        foreach (var line in Wrap(d.Description, 0.7f * s, 760 * s)) {
            y += lh * 0.7f * s * 1.05f;
            Text(sb, line, new Vector2(rowCenter.X, y), 0.7f * s, Color.White * alpha, border: true);
        }
        if (n > 1 && since < 6)
            Text(sb, "tap an icon or press X to switch", new Vector2(rowCenter.X, y + lh * 0.6f * s * 1.2f), 0.6f * s, new Color(220, 210, 190) * alpha, border: true);
    }

    /// <summary>Splits text into lines no wider than maxWidth at this scale.</summary>
    private static List<string> Wrap(string text, float scale, float maxWidth) {
        var font = FontGlobals.RebirthFont;
        var lines = new List<string>();
        string line = "";
        foreach (var word in text.Split(' ')) {
            string tryLine = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureString(tryLine).X * scale > maxWidth) {
                lines.Add(line);
                line = word;
            }
            else
                line = tryLine;
        }
        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }

    // ---- an item in the middle of the room (on a stone pedestal in item and secret rooms), with what it does above it
    private static void DrawPedestal(SpriteBatch sb, TroveRoom room) {
        var c = ToScreen(TroveRun.PickupPoint);
        var edge = ToScreen(TroveRun.PickupPoint + new Vector2(Block.SIDE_LENGTH * 0.8f, 0));
        float r = Math.Max(14f, Vector2.Distance(c, edge));
        float bob = MathF.Sin((float)TroveRun.PlayTime * 3f) * r * 0.12f;
        if (room.Kind is TroveRoomKind.Item or TroveRoomKind.Secret) {
            ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.25f), r * 1.15f, Color.Black * 0.35f);
            ToggleSwitch.DrawDisc(sb, c, r * 1.05f, new Color(110, 104, 98));
            ToggleSwitch.DrawDisc(sb, c, r * 0.85f, new Color(150, 144, 136));
        }
        else
            ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.2f), r * 0.8f, Color.Black * 0.3f);
        var iconAt = c - new Vector2(0, r * 0.9f + bob);
        ToggleSwitch.DrawDisc(sb, iconAt, r * 1.15f, new Color(255, 220, 120) * 0.3f); // glow
        DrawItemIcon(sb, room.Pickup!.Value, iconAt, r * 0.8f);

        // the label: name, what it does, how it works, and any synergy it would switch on for you
        var def = TroveItems.Def(room.Pickup.Value);
        float s = S;
        string kind = def.Kind switch {
            TroveItemKind.Active => def.MaxUses <= 1 ? "ITEM button  -  one use"
                : def.RechargeSeconds > 0 ? $"ITEM button  -  {def.MaxUses} uses, ready again after {def.RechargeSeconds:0}s"
                : $"ITEM button  -  {def.MaxUses} uses, ready again after {def.CooldownSeconds:0}s",
            TroveItemKind.Passive => TroveItems.Stacks(def.Id) ? "Lasts the whole run  -  stacks" : "Lasts the whole run",
            _ => "Takes effect right away",
        };
        var hint = TroveItems.SynergyHint(Net.NetPlay.GetMyClientId(), def.Id);
        float nameScale = 1.15f * s, descScale = 0.82f * s, kindScale = 0.68f * s;
        float maxW = 980 * s;
        var descLines = Wrap(def.Description, descScale, maxW);
        var font = FontGlobals.RebirthFont;
        float lineH = font.MeasureString("Ag").Y;
        float w = Math.Max(font.MeasureString(def.Name).X * nameScale, descLines.Max(l => font.MeasureString(l).X) * descScale);
        w = Math.Max(w, font.MeasureString(kind).X * kindScale);
        if (hint is not null)
            w = Math.Max(w, font.MeasureString($"Synergy: {hint.Name}!").X * kindScale * 1.1f);
        w += 56 * s;
        float h = lineH * nameScale + descLines.Count * lineH * descScale * 1.05f + lineH * kindScale * 1.2f + (hint is not null ? lineH * kindScale * 1.3f : 0) + 40 * s;
        var mid = c - new Vector2(0, r * 2.6f + h / 2f + bob * 0.5f);
        mid.X = Math.Clamp(mid.X, w / 2f + 8, WindowUtils.WindowWidth - w / 2f - 8);
        mid.Y = Math.Max(mid.Y, h / 2f + 8);
        var box = new Rectangle((int)(mid.X - w / 2f), (int)(mid.Y - h / 2f), (int)w, (int)h);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(box.X - 3, box.Y - 3, box.Width + 6, box.Height + 6), 18 * s, Color.Lerp(def.Color, Color.Black, 0.25f));
        ToggleSwitch.DrawRoundedRect(sb, box, 16 * s, new Color(30, 22, 16));
        float y = box.Y + 20 * s + lineH * nameScale / 2f;
        Text(sb, def.Name, new Vector2(mid.X, y), nameScale, Color.Lerp(def.Color, Color.White, 0.45f), border: true);
        y += lineH * nameScale / 2f + lineH * descScale * 0.6f;
        foreach (var l in descLines) {
            Text(sb, l, new Vector2(mid.X, y), descScale, Color.White);
            y += lineH * descScale * 1.05f;
        }
        y += lineH * kindScale * 0.15f;
        Text(sb, kind, new Vector2(mid.X, y), kindScale, new Color(205, 195, 175));
        if (hint is not null) {
            y += lineH * kindScale * 1.3f;
            Text(sb, $"Synergy: {hint.Name}!", new Vector2(mid.X, y), kindScale * 1.1f, new Color(255, 215, 80), border: true);
        }
    }

    // discs inside the game's HUD batch (premultiplied alpha)
    private static void DiscPM(SpriteBatch sb, Vector2 c, float r, Color col) => ToggleSwitch.DrawDisc(sb, c, r, col);

    /// <summary>An item's picture: a round badge in the item's color with its symbol (from the icon sheet).</summary>
    public static void DrawItemIcon(SpriteBatch sb, TroveItemId id, Vector2 c, float r, bool premultiplied = false) {
        if (TroveIcons.Draw(sb, id, c, r))
            return;
        // (no icon sheet: drawn shape by shape)
        var def = TroveItems.Def(id);
        ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.08f), r * 1.05f, Color.Black * 0.4f);
        ToggleSwitch.DrawDisc(sb, c, r, def.Color);
        ToggleSwitch.DrawDisc(sb, c, r * 0.82f, Color.Lerp(def.Color, Color.White, 0.25f));
        DrawItemSymbol(sb, id, c, r);
    }

    /// <summary>The symbol in the middle of an item's badge.</summary>
    internal static void DrawItemSymbol(SpriteBatch sb, TroveItemId id, Vector2 c, float r) {
        var def = TroveItems.Def(id);
        var w = Color.White;
        float t = Math.Max(2f, r * 0.14f);
        switch (id) {
            case TroveItemId.Rocket: {
                var a = c + new Vector2(-r * 0.45f, r * 0.45f);
                var b = c + new Vector2(r * 0.45f, -r * 0.45f);
                ToggleSwitch.DrawLine(sb, a, b, r * 0.3f, new Color(60, 60, 70));
                ToggleSwitch.DrawDisc(sb, b, r * 0.15f, new Color(60, 60, 70));
                ToggleSwitch.DrawDisc(sb, a, r * 0.2f, new Color(255, 220, 60));
                break;
            }
            case TroveItemId.Homing:
                ToggleSwitch.DrawDisc(sb, c, r * 0.5f, w);
                ToggleSwitch.DrawDisc(sb, c, r * 0.36f, def.Color);
                ToggleSwitch.DrawDisc(sb, c, r * 0.12f, w);
                ToggleSwitch.DrawLine(sb, c - new Vector2(r * 0.7f, 0), c + new Vector2(r * 0.7f, 0), t * 0.7f, w);
                ToggleSwitch.DrawLine(sb, c - new Vector2(0, r * 0.7f), c + new Vector2(0, r * 0.7f), t * 0.7f, w);
                break;
            case TroveItemId.SliderBomb:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.1f), r * 0.45f, new Color(25, 25, 30));
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.2f, -r * 0.25f), c + new Vector2(r * 0.45f, -r * 0.55f), t * 0.6f, new Color(140, 110, 70));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.5f, -r * 0.6f), r * 0.12f, new Color(255, 200, 60));
                break;
            case TroveItemId.ExtraLife: {
                // a heart
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.22f, -r * 0.12f), r * 0.27f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.22f, -r * 0.12f), r * 0.27f, w);
                for (int i = 0; i < 6; i++) {
                    float y = -r * 0.1f + i * r * 0.09f, half = r * (0.48f - i * 0.085f);
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-half, y), c + new Vector2(half, y), r * 0.11f, w);
                }
                break;
            }
            case TroveItemId.Shield: {
                var top = c + new Vector2(0, -r * 0.55f);
                var l = c + new Vector2(-r * 0.45f, -r * 0.35f);
                var rr = c + new Vector2(r * 0.45f, -r * 0.35f);
                var bot = c + new Vector2(0, r * 0.6f);
                ToggleSwitch.DrawLine(sb, top, l, t, w);
                ToggleSwitch.DrawLine(sb, top, rr, t, w);
                ToggleSwitch.DrawLine(sb, l, bot, t, w);
                ToggleSwitch.DrawLine(sb, rr, bot, t, w);
                break;
            }
            case TroveItemId.SpeedTreads:
                foreach (float dx in new[] { -0.3f, 0.15f }) {
                    ToggleSwitch.DrawLine(sb, c + new Vector2((dx - 0.15f) * r, -r * 0.4f), c + new Vector2((dx + 0.15f) * r, 0), t, w);
                    ToggleSwitch.DrawLine(sb, c + new Vector2((dx + 0.15f) * r, 0), c + new Vector2((dx - 0.15f) * r, r * 0.4f), t, w);
                }
                break;
            case TroveItemId.BouncyShells:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, -r * 0.35f), c + new Vector2(-r * 0.15f, r * 0.4f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.15f, r * 0.4f), c + new Vector2(r * 0.2f, -r * 0.35f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.2f, -r * 0.35f), c + new Vector2(r * 0.55f, r * 0.3f), t, w);
                break;
            case TroveItemId.ExtraShell:
                foreach (float dx in new[] { -0.22f, 0.22f }) {
                    ToggleSwitch.DrawLine(sb, c + new Vector2(dx * r, r * 0.45f), c + new Vector2(dx * r, -r * 0.25f), r * 0.26f, new Color(70, 70, 80));
                    ToggleSwitch.DrawDisc(sb, c + new Vector2(dx * r, -r * 0.27f), r * 0.13f, new Color(70, 70, 80));
                }
                break;
            case TroveItemId.MinePack:
                ToggleSwitch.DrawDisc(sb, c, r * 0.5f, new Color(240, 210, 60));
                ToggleSwitch.DrawDisc(sb, c, r * 0.18f, new Color(210, 40, 30));
                break;
            case TroveItemId.Detonator: {
                // a plunger box
                var box = new Rectangle((int)(c.X - r * 0.45f), (int)(c.Y - r * 0.05f), (int)(r * 0.9f), (int)(r * 0.5f));
                ToggleSwitch.DrawRoundedRect(sb, box, r * 0.08f, new Color(60, 40, 30));
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.05f), c + new Vector2(0, -r * 0.5f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.3f, -r * 0.5f), c + new Vector2(r * 0.3f, -r * 0.5f), t * 1.2f, w);
                break;
            }
            case TroveItemId.BigMines:
                ToggleSwitch.DrawDisc(sb, c, r * 0.62f, new Color(240, 210, 60));
                ToggleSwitch.DrawDisc(sb, c, r * 0.3f, new Color(210, 40, 30));
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.45f, -r * 0.45f), c + new Vector2(r * 0.75f, -r * 0.75f), t * 0.7f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.45f, r * 0.45f), c + new Vector2(-r * 0.75f, r * 0.75f), t * 0.7f, w);
                break;
            case TroveItemId.TripleShot:
                foreach (float ang in new[] { -0.45f, 0f, 0.45f }) {
                    var dir = new Vector2(MathF.Sin(ang), -MathF.Cos(ang));
                    ToggleSwitch.DrawLine(sb, c + dir * r * 0.05f, c + dir * r * 0.6f, r * 0.18f, w);
                }
                break;
            case TroveItemId.QuickReload:
                foreach (float dx in new[] { -0.25f, 0.15f }) {
                    ToggleSwitch.DrawLine(sb, c + new Vector2((dx - 0.12f) * r, -r * 0.35f), c + new Vector2((dx + 0.18f) * r, 0), t, w);
                    ToggleSwitch.DrawLine(sb, c + new Vector2((dx + 0.18f) * r, 0), c + new Vector2((dx - 0.12f) * r, r * 0.35f), t, w);
                }
                break;
            case TroveItemId.OrbitGuard:
                ToggleSwitch.DrawDisc(sb, c, r * 0.3f, new Color(60, 70, 90));
                for (int i = 0; i < 3; i++) {
                    float a = i * MathHelper.TwoPi / 3f + 0.4f;
                    ToggleSwitch.DrawDisc(sb, c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r * 0.55f, r * 0.15f, w);
                }
                break;
            case TroveItemId.TreasureMap: {
                var box = new Rectangle((int)(c.X - r * 0.5f), (int)(c.Y - r * 0.38f), (int)(r * 1.0f), (int)(r * 0.76f));
                ToggleSwitch.DrawRoundedRect(sb, box, r * 0.06f, new Color(240, 220, 170));
                var red = new Color(200, 40, 30);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.05f, -r * 0.15f), c + new Vector2(r * 0.3f, r * 0.15f), t * 0.7f, red);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.3f, -r * 0.15f), c + new Vector2(r * 0.05f, r * 0.15f), t * 0.7f, red);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.35f, r * 0.2f), c + new Vector2(-r * 0.05f, r * 0.0f), t * 0.4f, new Color(120, 90, 50));
                break;
            }
            case TroveItemId.BuddyTank: {
                var body = new Rectangle((int)(c.X - r * 0.45f), (int)(c.Y - r * 0.1f), (int)(r * 0.9f), (int)(r * 0.45f));
                ToggleSwitch.DrawRoundedRect(sb, body, r * 0.08f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.12f), r * 0.22f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.12f), c + new Vector2(r * 0.55f, -r * 0.35f), t * 0.8f, w);
                break;
            }
            case TroveItemId.HeavyShells: {
                // a fat brass shell
                var brass = new Color(222, 176, 78);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.5f), c + new Vector2(0, -r * 0.18f), r * 0.52f, brass);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.2f), r * 0.26f, brass);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.36f), r * 0.16f, new Color(240, 205, 120));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.26f, r * 0.34f), c + new Vector2(r * 0.26f, r * 0.34f), r * 0.1f, new Color(150, 105, 40));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.12f, r * 0.2f), c + new Vector2(-r * 0.12f, -r * 0.2f), r * 0.07f, new Color(255, 235, 170));
                break;
            }
            case TroveItemId.ExplosiveShells:
                for (int i = 0; i < 8; i++) {
                    float a = i * MathHelper.TwoPi / 8f;
                    var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    ToggleSwitch.DrawLine(sb, c + dir * r * 0.25f, c + dir * r * (i % 2 == 0 ? 0.68f : 0.5f), t * 0.8f, new Color(255, 230, 120));
                }
                ToggleSwitch.DrawDisc(sb, c, r * 0.25f, w);
                break;
            case TroveItemId.GhostTreads: {
                var ghost = new Color(255, 255, 255, 170);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.12f), r * 0.38f, ghost);
                var body = new Rectangle((int)(c.X - r * 0.38f), (int)(c.Y - r * 0.12f), (int)(r * 0.76f), (int)(r * 0.5f));
                ToggleSwitch.DrawRoundedRect(sb, body, r * 0.05f, ghost);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.13f, -r * 0.15f), r * 0.07f, new Color(40, 40, 60));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.13f, -r * 0.15f), r * 0.07f, new Color(40, 40, 60));
                break;
            }
            case TroveItemId.ShellShield: {
                var top = c + new Vector2(0, -r * 0.55f);
                var l = c + new Vector2(-r * 0.45f, -r * 0.35f);
                var rr = c + new Vector2(r * 0.45f, -r * 0.35f);
                var bot = c + new Vector2(0, r * 0.6f);
                ToggleSwitch.DrawLine(sb, top, l, t, w);
                ToggleSwitch.DrawLine(sb, top, rr, t, w);
                ToggleSwitch.DrawLine(sb, l, bot, t, w);
                ToggleSwitch.DrawLine(sb, rr, bot, t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.25f), c + new Vector2(0, -r * 0.2f), r * 0.16f, w);
                break;
            }
            case TroveItemId.BossHunter: {
                var red = new Color(255, 230, 230);
                ToggleSwitch.DrawDisc(sb, c, r * 0.5f, red);
                ToggleSwitch.DrawDisc(sb, c, r * 0.38f, def.Color);
                ToggleSwitch.DrawDisc(sb, c, r * 0.1f, red);
                ToggleSwitch.DrawLine(sb, c - new Vector2(r * 0.72f, 0), c - new Vector2(r * 0.3f, 0), t * 0.7f, red);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.72f, 0), c + new Vector2(r * 0.3f, 0), t * 0.7f, red);
                ToggleSwitch.DrawLine(sb, c - new Vector2(0, r * 0.72f), c - new Vector2(0, r * 0.3f), t * 0.7f, red);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.72f), c + new Vector2(0, r * 0.3f), t * 0.7f, red);
                break;
            }
            case TroveItemId.Stopwatch:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.05f), r * 0.52f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.55f), r * 0.1f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.05f), c + new Vector2(0, -r * 0.3f), t * 0.7f, new Color(40, 50, 60));
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.05f), c + new Vector2(r * 0.25f, r * 0.12f), t * 0.7f, new Color(40, 50, 60));
                break;
            case TroveItemId.ShellBurst:
                for (int i = 0; i < 8; i++) {
                    float a = i * MathHelper.TwoPi / 8f;
                    var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    ToggleSwitch.DrawLine(sb, c + dir * r * 0.3f, c + dir * r * 0.65f, r * 0.13f, w);
                }
                break;
            case TroveItemId.Nuke: {
                var dark = new Color(40, 30, 20);
                ToggleSwitch.DrawDisc(sb, c, r * 0.16f, dark);
                for (int i = 0; i < 3; i++) {
                    float a = i * MathHelper.TwoPi / 3f - MathHelper.PiOver2;
                    var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                    ToggleSwitch.DrawLine(sb, c + dir * r * 0.28f, c + dir * r * 0.62f, r * 0.32f, dark);
                }
                break;
            }
            case TroveItemId.Decoy:
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.45f), (int)(c.Y - r * 0.05f), (int)(r * 0.9f), (int)(r * 0.42f)), r * 0.08f, new Color(240, 220, 190));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.08f), r * 0.2f, new Color(240, 220, 190));
                Text(sb, "?", c + new Vector2(0, -r * 0.48f), r * 0.02f, w, border: true);
                break;
            case TroveItemId.Invincibility:
                for (int i = 0; i < 5; i++) {
                    float a1 = i * MathHelper.TwoPi / 5f - MathHelper.PiOver2, a2 = (i + 2) * MathHelper.TwoPi / 5f - MathHelper.PiOver2;
                    ToggleSwitch.DrawLine(sb, c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r * 0.6f, c + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * r * 0.6f, t, w);
                }
                break;
            case TroveItemId.Reroll: {
                var box = new Rectangle((int)(c.X - r * 0.45f), (int)(c.Y - r * 0.45f), (int)(r * 0.9f), (int)(r * 0.9f));
                ToggleSwitch.DrawRoundedRect(sb, box, r * 0.15f, w);
                var pip = new Color(40, 40, 50);
                foreach (var o in new[] { new Vector2(-0.22f, -0.22f), new Vector2(0.22f, 0.22f), Vector2.Zero, new Vector2(0.22f, -0.22f), new Vector2(-0.22f, 0.22f) })
                    ToggleSwitch.DrawDisc(sb, c + o * r, r * 0.08f, pip);
                break;
            }
            case TroveItemId.SplitShot:
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.5f), c + new Vector2(0, 0), t, w);
                ToggleSwitch.DrawLine(sb, c, c + new Vector2(-r * 0.4f, -r * 0.45f), t, w);
                ToggleSwitch.DrawLine(sb, c, c + new Vector2(r * 0.4f, -r * 0.45f), t, w);
                break;
            case TroveItemId.SniperShells:
                ToggleSwitch.DrawDisc(sb, c, r * 0.5f, w);
                ToggleSwitch.DrawDisc(sb, c, r * 0.4f, def.Color);
                ToggleSwitch.DrawLine(sb, c - new Vector2(r * 0.6f, 0), c + new Vector2(r * 0.6f, 0), t * 0.5f, w);
                ToggleSwitch.DrawLine(sb, c - new Vector2(0, r * 0.6f), c + new Vector2(0, r * 0.6f), t * 0.5f, w);
                break;
            case TroveItemId.ChainLightning:
            case TroveItemId.LightningStorm: {
                var pts = new[] { new Vector2(0.15f, -0.6f), new Vector2(-0.2f, 0.0f), new Vector2(0.15f, 0.0f), new Vector2(-0.15f, 0.6f) };
                for (int i = 0; i < 3; i++)
                    ToggleSwitch.DrawLine(sb, c + pts[i] * r, c + pts[i + 1] * r, t * 1.1f, new Color(255, 250, 180));
                if (id == TroveItemId.LightningStorm)
                    ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.35f, -r * 0.45f), r * 0.2f, w);
                break;
            }
            case TroveItemId.RegenPlating:
            case TroveItemId.RepairKit: {
                ToggleSwitch.DrawLine(sb, c - new Vector2(r * 0.45f, 0), c + new Vector2(r * 0.45f, 0), r * 0.25f, w);
                ToggleSwitch.DrawLine(sb, c - new Vector2(0, r * 0.45f), c + new Vector2(0, r * 0.45f), r * 0.25f, w);
                break;
            }
            case TroveItemId.ClusterMines:
                foreach (var o in new[] { new Vector2(-0.3f, -0.3f), new Vector2(0.3f, -0.3f), new Vector2(-0.3f, 0.3f), new Vector2(0.3f, 0.3f) })
                    ToggleSwitch.DrawDisc(sb, c + o * r, r * 0.22f, new Color(240, 210, 60));
                ToggleSwitch.DrawDisc(sb, c, r * 0.12f, new Color(210, 40, 30));
                break;
            case TroveItemId.HomingMines:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.15f, 0), r * 0.35f, new Color(240, 210, 60));
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.25f, 0), c + new Vector2(r * 0.6f, 0), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.6f, 0), c + new Vector2(r * 0.4f, -r * 0.2f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.6f, 0), c + new Vector2(r * 0.4f, r * 0.2f), t, w);
                break;
            case TroveItemId.TinyTank:
            case TroveItemId.GiantTank: {
                float k = id == TroveItemId.TinyTank ? 0.5f : 1f;
                var body = new Rectangle((int)(c.X - r * 0.45f * k), (int)(c.Y - r * 0.1f * k), (int)(r * 0.9f * k), (int)(r * 0.45f * k));
                ToggleSwitch.DrawRoundedRect(sb, body, r * 0.06f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.12f * k), r * 0.22f * k, w);
                break;
            }
            case TroveItemId.VampireTreads:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.25f, -r * 0.2f), c + new Vector2(-r * 0.15f, r * 0.4f), r * 0.16f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.25f, -r * 0.2f), c + new Vector2(r * 0.15f, r * 0.4f), r * 0.16f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.5f, -r * 0.3f), c + new Vector2(r * 0.5f, -r * 0.3f), t, w);
                break;
            case TroveItemId.RicochetMaster:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, r * 0.4f), c + new Vector2(-r * 0.1f, -r * 0.35f), t * 0.6f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.1f, -r * 0.35f), c + new Vector2(r * 0.25f, r * 0.3f), t * 0.9f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.35f, r * 0.4f), r * 0.2f, w);
                break;
            case TroveItemId.EchoShot:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.2f, 0), r * 0.22f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.25f, 0), r * 0.16f, new Color(255, 255, 255, 150));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.55f, 0), r * 0.1f, new Color(255, 255, 255, 90));
                break;
            case TroveItemId.Airstrike:
                for (int i = 0; i < 3; i++) {
                    var top = c + new Vector2((i - 1) * r * 0.35f, -r * 0.55f);
                    ToggleSwitch.DrawLine(sb, top, top + new Vector2(0, r * 0.6f), t * 0.9f, w);
                    ToggleSwitch.DrawDisc(sb, top + new Vector2(0, r * 0.65f), r * 0.12f, new Color(255, 220, 120));
                }
                break;
            case TroveItemId.WallBuilder: {
                // three cork blocks: two below, one on top
                var cork = new Color(150, 98, 56);
                var top = new Color(205, 152, 96);
                foreach (var o in new[] { new Vector2(-0.52f, 0.02f), new Vector2(0.04f, 0.02f), new Vector2(-0.24f, -0.46f) }) {
                    ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X + o.X * r), (int)(c.Y + o.Y * r), (int)(r * 0.48f), (int)(r * 0.42f)), r * 0.06f, cork);
                    ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X + (o.X + 0.05f) * r), (int)(c.Y + (o.Y + 0.05f) * r), (int)(r * 0.38f), (int)(r * 0.13f)), r * 0.04f, top);
                }
                break;
            }
            case TroveItemId.Dash:
                foreach (float dy in new[] { -0.3f, 0f, 0.3f })
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, dy * r), c + new Vector2(r * (dy == 0 ? 0.55f : 0.25f), dy * r), t * 0.8f, w);
                break;
            case TroveItemId.MineStorm:
                for (int i = 0; i < 6; i++) {
                    var dir = Vector2.UnitX.Rotate(i * MathHelper.TwoPi / 6f);
                    ToggleSwitch.DrawDisc(sb, c + dir * r * 0.45f, r * 0.15f, new Color(240, 210, 60));
                }
                ToggleSwitch.DrawDisc(sb, c, r * 0.12f, new Color(210, 40, 30));
                break;
            case TroveItemId.Convert:
                ToggleSwitch.DrawDisc(sb, c, r * 0.48f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.1f, 0), r * 0.38f, def.Color);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.15f, -r * 0.1f), c + new Vector2(r * 0.25f, -r * 0.1f), t * 0.7f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.25f, -r * 0.1f), c + new Vector2(r * 0.12f, -r * 0.25f), t * 0.7f, w);
                break;
            case TroveItemId.ShrinkRay:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, r * 0.2f), c + new Vector2(r * 0.1f, -r * 0.1f), r * 0.22f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.35f, -r * 0.2f), r * 0.14f, new Color(200, 255, 210));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.55f, -r * 0.3f), r * 0.08f, new Color(200, 255, 210));
                break;
            case TroveItemId.GhostShells: {
                // a pale shell slipping through a wall
                var wall = new Color(84, 90, 140);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.08f, -r * 0.6f), c + new Vector2(r * 0.08f, r * 0.6f), r * 0.26f, wall);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.08f, -r * 0.6f), c + new Vector2(r * 0.08f, r * 0.6f), r * 0.08f, new Color(120, 128, 180));
                for (int i = 0; i < 3; i++)
                    ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * (0.62f - i * 0.17f), 0), r * (0.07f + i * 0.02f), new Color(255, 255, 255, 120 + i * 40));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.12f, 0), c + new Vector2(r * 0.5f, 0), r * 0.24f, wall);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.1f, 0), c + new Vector2(r * 0.48f, 0), r * 0.16f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.5f, 0), r * 0.12f, wall);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.5f, 0), r * 0.08f, w);
                break;
            }
            case TroveItemId.LastStand:
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.55f), c + new Vector2(0, r * 0.15f), r * 0.22f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.45f), r * 0.13f, w);
                break;
            case TroveItemId.MagnetShells: {
                var red = new Color(240, 240, 240);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.35f, -r * 0.4f), c + new Vector2(-r * 0.35f, r * 0.15f), r * 0.22f, red);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.35f, -r * 0.4f), c + new Vector2(r * 0.35f, r * 0.15f), r * 0.22f, red);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.35f, r * 0.2f), c + new Vector2(r * 0.35f, r * 0.2f), r * 0.22f, red);
                break;
            }
            case TroveItemId.Afterburner:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.15f), r * 0.35f, new Color(255, 220, 90));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.2f), r * 0.22f, new Color(255, 240, 160));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.2f), r * 0.16f, new Color(255, 255, 230));
                break;
            case TroveItemId.RicochetShield:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.5f, -r * 0.4f), c + new Vector2(0, r * 0.2f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.2f), c + new Vector2(r * 0.5f, -r * 0.4f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.45f, r * 0.5f), c + new Vector2(r * 0.45f, r * 0.5f), t, w);
                break;
            case TroveItemId.SecondWind: {
                // a heart with wings
                var wing = new Color(214, 160, 70);
                foreach (float k in new[] { -1f, 1f }) {
                    ToggleSwitch.DrawLine(sb, c + new Vector2(k * r * 0.14f, -r * 0.02f), c + new Vector2(k * r * 0.66f, -r * 0.36f), t * 1.1f, wing);
                    ToggleSwitch.DrawLine(sb, c + new Vector2(k * r * 0.14f, r * 0.1f), c + new Vector2(k * r * 0.6f, -r * 0.1f), t, wing);
                    ToggleSwitch.DrawLine(sb, c + new Vector2(k * r * 0.14f, r * 0.2f), c + new Vector2(k * r * 0.48f, r * 0.14f), t * 0.85f, wing);
                }
                var heart = new Color(226, 70, 76);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.1f, -r * 0.02f), r * 0.14f, heart);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.1f, -r * 0.02f), r * 0.14f, heart);
                for (int i = 0; i < 5; i++) {
                    float y = r * 0.02f + i * r * 0.055f, half = r * (0.23f - i * 0.05f);
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-half, y), c + new Vector2(half, y), r * 0.07f, heart);
                }
                break;
            }
            case TroveItemId.Clone:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.25f, 0), r * 0.25f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.25f, 0), r * 0.25f, new Color(255, 255, 255, 140));
                break;
            case TroveItemId.EMP:
                ToggleSwitch.DrawDisc(sb, c, r * 0.55f, w);
                ToggleSwitch.DrawDisc(sb, c, r * 0.43f, def.Color);
                ToggleSwitch.DrawDisc(sb, c, r * 0.25f, w);
                ToggleSwitch.DrawDisc(sb, c, r * 0.14f, def.Color);
                break;
            case TroveItemId.Teleport:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.3f, r * 0.2f), r * 0.2f, new Color(255, 255, 255, 120));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.15f, r * 0.05f), c + new Vector2(r * 0.2f, -r * 0.2f), t * 0.6f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.3f, -r * 0.3f), r * 0.2f, w);
                break;
            case TroveItemId.TurretDrop:
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.4f), (int)(c.Y + r * 0.05f), (int)(r * 0.8f), (int)(r * 0.35f)), r * 0.05f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.05f), r * 0.22f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.05f), c + new Vector2(0, -r * 0.6f), t, w);
                break;
            case TroveItemId.SupplyDrop:
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.4f), (int)(c.Y - r * 0.1f), (int)(r * 0.8f), (int)(r * 0.55f)), r * 0.05f, new Color(200, 160, 100));
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.1f), c + new Vector2(0, r * 0.45f), t * 0.6f, new Color(120, 80, 40));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.3f, -r * 0.6f), c + new Vector2(0, -r * 0.15f), t * 0.5f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.3f, -r * 0.6f), c + new Vector2(0, -r * 0.15f), t * 0.5f, w);
                break;
            case TroveItemId.MirrorShield: {
                var frame = new Color(70, 112, 168);
                ToggleSwitch.DrawDisc(sb, c, r * 0.56f, frame);
                ToggleSwitch.DrawDisc(sb, c, r * 0.44f, new Color(186, 222, 250));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.06f, r * 0.08f), r * 0.34f, new Color(150, 198, 240));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.26f, -r * 0.08f), c + new Vector2(-r * 0.02f, -r * 0.3f), t * 0.7f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.12f, r * 0.08f), c + new Vector2(r * 0.02f, -r * 0.05f), t * 0.45f, w);
                break;
            }
            case TroveItemId.BountyHunter:
                ToggleSwitch.DrawDisc(sb, c, r * 0.5f, new Color(255, 220, 90));
                Text(sb, "$", c, r * 0.022f, new Color(120, 80, 10));
                break;
            case TroveItemId.HotPotato:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.08f), r * 0.42f, new Color(190, 130, 70));
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.1f, -r * 0.35f), c + new Vector2(r * 0.25f, -r * 0.6f), t * 0.6f, new Color(255, 210, 60));
                break;
            case TroveItemId.Wingman:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, r * 0.1f), c + new Vector2(r * 0.55f, r * 0.1f), r * 0.16f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.45f), c + new Vector2(0, r * 0.45f), r * 0.16f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.25f, r * 0.42f), c + new Vector2(r * 0.25f, r * 0.42f), r * 0.12f, w);
                break;
            case TroveItemId.Momentum:
                foreach (float dy in new[] { -0.25f, 0.05f, 0.35f })
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, dy * r), c + new Vector2(-r * 0.1f, dy * r), t * 0.6f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.2f, r * 0.05f), r * 0.25f, w);
                break;
            case TroveItemId.GravityWell:
                ToggleSwitch.DrawDisc(sb, c, r * 0.55f, w);
                ToggleSwitch.DrawDisc(sb, c, r * 0.42f, def.Color);
                ToggleSwitch.DrawDisc(sb, c, r * 0.22f, new Color(20, 10, 30));
                break;
            case TroveItemId.SmokeBomb:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.2f, r * 0.05f), r * 0.3f, new Color(230, 230, 235));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.22f, -r * 0.08f), r * 0.33f, new Color(220, 220, 228));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.25f), r * 0.28f, new Color(240, 240, 245));
                break;
            case TroveItemId.RallyFlag:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.25f, r * 0.55f), c + new Vector2(-r * 0.25f, -r * 0.55f), t * 0.7f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.25f, -r * 0.35f), c + new Vector2(r * 0.45f, -r * 0.25f), r * 0.32f, w);
                break;
            case TroveItemId.Shockwave:
                ToggleSwitch.DrawDisc(sb, c, r * 0.6f, w);
                ToggleSwitch.DrawDisc(sb, c, r * 0.48f, def.Color);
                ToggleSwitch.DrawDisc(sb, c, r * 0.32f, w);
                ToggleSwitch.DrawDisc(sb, c, r * 0.2f, def.Color);
                break;
            case TroveItemId.RocketBarrage:
                for (int i = -1; i <= 1; i++) {
                    var a0 = c + new Vector2(i * r * 0.3f - r * 0.15f, r * 0.4f);
                    var a1 = a0 + new Vector2(r * 0.3f, -r * 0.75f);
                    ToggleSwitch.DrawLine(sb, a0, a1, r * 0.13f, w);
                    ToggleSwitch.DrawDisc(sb, a0, r * 0.08f, new Color(255, 220, 90));
                }
                break;
            case TroveItemId.FreezeBomb:
                ToggleSwitch.DrawDisc(sb, c, r * 0.4f, new Color(40, 70, 100));
                for (int i = 0; i < 3; i++) {
                    var dir = Vector2.UnitX.Rotate(i * MathHelper.Pi / 3f);
                    ToggleSwitch.DrawLine(sb, c - dir * r * 0.32f, c + dir * r * 0.32f, t * 0.5f, new Color(200, 245, 255));
                }
                break;
            case TroveItemId.NitroTreads:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.4f, r * 0.35f), c + new Vector2(r * 0.1f, -r * 0.1f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.1f, -r * 0.1f), c + new Vector2(-r * 0.05f, r * 0.05f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.05f, r * 0.05f), c + new Vector2(r * 0.4f, -r * 0.45f), t, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.42f, r * 0.38f), r * 0.14f, new Color(255, 140, 40));
                break;
            case TroveItemId.RapidBarrel:
                foreach (float dy in new[] { -0.3f, 0f, 0.3f }) {
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.45f, dy * r), c + new Vector2(r * 0.25f, dy * r), r * 0.14f, w);
                    ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.32f, dy * r), r * 0.09f, w);
                }
                break;
            case TroveItemId.ArmorPlating:
            case TroveItemId.HeavyArmor:
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.45f), (int)(c.Y - r * 0.45f), (int)(r * 0.9f), (int)(r * 0.9f)), r * 0.1f, w);
                ToggleSwitch.DrawLine(sb, c - new Vector2(r * 0.25f, 0), c + new Vector2(r * 0.25f, 0), r * 0.16f, def.Color);
                ToggleSwitch.DrawLine(sb, c - new Vector2(0, r * 0.25f), c + new Vector2(0, r * 0.25f), r * 0.16f, def.Color);
                if (id == TroveItemId.HeavyArmor)
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.45f, r * 0.55f), c + new Vector2(r * 0.45f, r * 0.55f), r * 0.12f, w);
                break;
            case TroveItemId.HighCaliber:
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.5f), c + new Vector2(0, -r * 0.15f), r * 0.42f, new Color(220, 190, 90));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.2f), r * 0.21f, new Color(220, 190, 90));
                break;
            case TroveItemId.Steroids:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.45f, r * 0.45f), c + new Vector2(r * 0.25f, -r * 0.25f), r * 0.22f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.25f, -r * 0.25f), c + new Vector2(r * 0.5f, -r * 0.5f), t * 0.4f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, r * 0.25f), c + new Vector2(-r * 0.25f, r * 0.55f), t * 0.6f, w);
                break;
            case TroveItemId.LongBarrel:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.6f, r * 0.2f), c + new Vector2(r * 0.6f, -r * 0.2f), r * 0.16f, new Color(60, 60, 70));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.45f, r * 0.15f), r * 0.2f, new Color(60, 60, 70));
                break;
            case TroveItemId.HeartyMeal: {
                // a drumstick
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.08f, r * 0.12f), c + new Vector2(r * 0.46f, r * 0.5f), r * 0.15f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.42f, r * 0.56f), r * 0.1f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.54f, r * 0.44f), r * 0.1f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.12f, -r * 0.1f), r * 0.42f, new Color(238, 150, 70));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.2f, -r * 0.2f), r * 0.2f, new Color(252, 190, 110));
                break;
            }
            case TroveItemId.MagicMushroom:
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.1f), c + new Vector2(0, r * 0.5f), r * 0.3f, new Color(245, 235, 210));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.05f), r * 0.48f, new Color(220, 40, 40));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.2f, -r * 0.15f), r * 0.1f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.18f, -r * 0.22f), r * 0.08f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.05f, r * 0.05f), r * 0.07f, w);
                break;
            case TroveItemId.GlassCannon: {
                var ring = new Color(52, 116, 160);
                ToggleSwitch.DrawDisc(sb, c, r * 0.56f, ring);
                ToggleSwitch.DrawDisc(sb, c, r * 0.46f, new Color(214, 242, 255));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.16f, -r * 0.18f), r * 0.12f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.02f, -r * 0.44f), c + new Vector2(r * 0.12f, -r * 0.08f), t * 0.5f, ring);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.12f, -r * 0.08f), c + new Vector2(-r * 0.08f, r * 0.14f), t * 0.5f, ring);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.08f, r * 0.14f), c + new Vector2(r * 0.06f, r * 0.42f), t * 0.5f, ring);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.12f, -r * 0.08f), c + new Vector2(r * 0.4f, r * 0.02f), t * 0.4f, ring);
                break;
            }
            case TroveItemId.Featherweight:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.45f, r * 0.45f), c + new Vector2(r * 0.4f, -r * 0.4f), t * 0.5f, w);
                for (int i = 1; i < 5; i++) {
                    var along = c + new Vector2(-r * 0.45f, r * 0.45f) + new Vector2(r * 0.85f, -r * 0.85f) * (i / 5f);
                    ToggleSwitch.DrawLine(sb, along, along + new Vector2(-r * 0.25f, -r * 0.05f), t * 0.4f, w);
                    ToggleSwitch.DrawLine(sb, along, along + new Vector2(r * 0.05f, r * 0.25f), t * 0.4f, w);
                }
                break;
            case TroveItemId.BoomerangShells: {
                // a curved boomerang
                var a = c + new Vector2(-r * 0.5f, r * 0.3f);
                var m = c + new Vector2(0, -r * 0.4f);
                var e = c + new Vector2(r * 0.5f, r * 0.3f);
                ToggleSwitch.DrawLine(sb, a, m, t * 1.3f, new Color(120, 80, 30));
                ToggleSwitch.DrawLine(sb, m, e, t * 1.3f, new Color(120, 80, 30));
                ToggleSwitch.DrawDisc(sb, m, t * 0.7f, new Color(120, 80, 30));
                break;
            }
            case TroveItemId.MortarShells: {
                // an arc over a wall, landing in a blast
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.1f), (int)(c.Y - r * 0.05f), (int)(r * 0.2f), (int)(r * 0.5f)), r * 0.04f, new Color(90, 70, 50));
                for (int i = 0; i <= 6; i++) {
                    float u = i / 6f;
                    var pt = c + new Vector2(-r * 0.55f + r * 1.1f * u, r * 0.35f - MathF.Sin(MathHelper.Pi * u) * r * 0.75f);
                    ToggleSwitch.DrawDisc(sb, pt, t * 0.45f, w);
                }
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.55f, r * 0.35f), r * 0.2f, new Color(255, 170, 60));
                break;
            }
            case TroveItemId.Wraparound:
                // out one side, in the other
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.6f, 0), c + new Vector2(-r * 0.6f, 0) + new Vector2(0, -r * 0.01f), t, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.3f, -r * 0.2f), c + new Vector2(r * 0.55f, -r * 0.2f), t * 0.8f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.55f, -r * 0.2f), c + new Vector2(r * 0.35f, -r * 0.38f), t * 0.8f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, r * 0.2f), c + new Vector2(r * 0.3f, r * 0.2f), t * 0.8f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, r * 0.2f), c + new Vector2(-r * 0.35f, r * 0.38f), t * 0.8f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.62f, -r * 0.5f), c + new Vector2(-r * 0.62f, r * 0.5f), t * 0.5f, Color.Black * 0.4f);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.62f, -r * 0.5f), c + new Vector2(r * 0.62f, r * 0.5f), t * 0.5f, Color.Black * 0.4f);
                break;
            case TroveItemId.MegaShell:
                ToggleSwitch.DrawDisc(sb, c, r * 0.55f, new Color(60, 60, 70));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.18f, -r * 0.18f), r * 0.16f, Color.White * 0.6f);
                break;
            case TroveItemId.HolyMantle: {
                // a halo over a shield
                var steel = new Color(96, 118, 176);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.1f), r * 0.4f, steel);
                for (int i = 0; i < 5; i++) {
                    float y = r * 0.2f + i * r * 0.08f, half = r * (0.36f - i * 0.085f);
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-half, y), c + new Vector2(half, y), r * 0.1f, steel);
                }
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.1f), r * 0.27f, new Color(176, 196, 236));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.34f, -r * 0.52f), c + new Vector2(r * 0.34f, -r * 0.52f), t * 1.25f, new Color(196, 140, 24));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.3f, -r * 0.52f), c + new Vector2(r * 0.3f, -r * 0.52f), t * 0.65f, new Color(255, 226, 110));
                break;
            }
            case TroveItemId.Pyromaniac:
                // a flame
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.18f), r * 0.38f, new Color(255, 110, 30));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.12f), r * 0.26f, new Color(255, 150, 40));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.35f), r * 0.14f, new Color(255, 190, 60));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.22f), r * 0.18f, new Color(255, 230, 120));
                break;
            case TroveItemId.MineDrone:
                // a little drone with a mine below
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.5f, -r * 0.3f), c + new Vector2(r * 0.5f, -r * 0.3f), t * 0.8f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.3f), r * 0.18f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.3f), r * 0.24f, new Color(30, 30, 35));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.3f), r * 0.08f, new Color(255, 60, 40));
                break;
            case TroveItemId.LuckyFoot:
                // a four-leaf clover
                foreach (var off in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                    ToggleSwitch.DrawDisc(sb, c + off * r * 0.2f, r * 0.24f, new Color(40, 140, 50));
                ToggleSwitch.DrawLine(sb, c, c + new Vector2(r * 0.3f, r * 0.55f), t * 0.5f, new Color(40, 140, 50));
                break;
            case TroveItemId.BoxOfFriends: {
                var box = new Rectangle((int)(c.X - r * 0.42f), (int)(c.Y - r * 0.2f), (int)(r * 0.84f), (int)(r * 0.62f));
                ToggleSwitch.DrawRoundedRect(sb, box, r * 0.06f, new Color(170, 110, 50));
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.2f), c + new Vector2(0, r * 0.42f), t * 0.6f, new Color(220, 60, 60));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.2f, -r * 0.38f), r * 0.14f, new Color(60, 120, 220));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.2f, -r * 0.38f), r * 0.14f, new Color(60, 120, 220));
                break;
            }
            case TroveItemId.OilSlick:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.15f, r * 0.1f), r * 0.42f, new Color(20, 20, 28));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.25f, -r * 0.1f), r * 0.28f, new Color(20, 20, 28));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.25f, -r * 0.05f), r * 0.1f, new Color(120, 90, 200) * 0.8f);
                break;
            case TroveItemId.TeleportPads: {
                foreach (float k in new[] { -1f, 1f }) {
                    var at = c + new Vector2(k * r * 0.32f, -k * r * 0.16f);
                    ToggleSwitch.DrawDisc(sb, at, r * 0.29f, w);
                    ToggleSwitch.DrawDisc(sb, at, r * 0.2f, new Color(84, 40, 160));
                    ToggleSwitch.DrawDisc(sb, at, r * 0.1f, new Color(130, 235, 255));
                }
                for (int i = -1; i <= 1; i++)
                    ToggleSwitch.DrawDisc(sb, c + new Vector2(i * r * 0.09f, -i * r * 0.045f) + new Vector2(r * 0.02f, r * 0.36f) * 0f, r * 0.035f, w);
                break;
            }
            case TroveItemId.Battery: {
                var cell = new Rectangle((int)(c.X - r * 0.25f), (int)(c.Y - r * 0.4f), (int)(r * 0.5f), (int)(r * 0.8f));
                ToggleSwitch.DrawRoundedRect(sb, cell, r * 0.06f, new Color(50, 50, 55));
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.1f), (int)(c.Y - r * 0.5f), (int)(r * 0.2f), (int)(r * 0.12f)), r * 0.03f, new Color(50, 50, 55));
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.08f, -r * 0.25f), c + new Vector2(-r * 0.08f, r * 0.02f), t * 0.6f, new Color(255, 220, 60));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.08f, r * 0.02f), c + new Vector2(r * 0.08f, r * 0.0f), t * 0.6f, new Color(255, 220, 60));
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.08f, r * 0.0f), c + new Vector2(-r * 0.06f, r * 0.27f), t * 0.6f, new Color(255, 220, 60));
                break;
            }
            case TroveItemId.MysteryPill:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.3f, r * 0.3f), c + new Vector2(r * 0.3f, -r * 0.3f), r * 0.42f, new Color(230, 70, 70));
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, 0), c + new Vector2(r * 0.3f, -r * 0.3f), r * 0.42f, new Color(70, 130, 230));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.3f, r * 0.3f), r * 0.21f, new Color(230, 70, 70));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.3f, -r * 0.3f), r * 0.21f, new Color(70, 130, 230));
                break;
            case TroveItemId.CupidsArrow:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.55f, r * 0.55f), c + new Vector2(r * 0.5f, -r * 0.5f), t * 0.6f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.5f, -r * 0.5f), c + new Vector2(r * 0.22f, -r * 0.45f), t * 0.6f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.5f, -r * 0.5f), c + new Vector2(r * 0.45f, -r * 0.22f), t * 0.6f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.12f, -r * 0.05f), r * 0.17f, new Color(220, 40, 90));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.12f, -r * 0.05f), r * 0.17f, new Color(220, 40, 90));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.1f), r * 0.17f, new Color(220, 40, 90));
                break;
            case TroveItemId.Turbocharger: {
                // a turbo snail: a ring with a spinning fan
                ToggleSwitch.DrawDisc(sb, c, r * 0.5f, new Color(90, 90, 100));
                ToggleSwitch.DrawDisc(sb, c, r * 0.38f, new Color(255, 150, 40));
                for (int i = 0; i < 6; i++) {
                    float a = i * MathHelper.TwoPi / 6 + (float)TroveRun.PlayTime * 6f;
                    ToggleSwitch.DrawLine(sb, c, c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r * 0.34f, t * 0.5f, new Color(60, 60, 70));
                }
                break;
            }
            case TroveItemId.HairTrigger:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.45f, -r * 0.1f), c + new Vector2(r * 0.45f, -r * 0.1f), t * 1.4f, new Color(60, 60, 70));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.3f, -r * 0.1f), c + new Vector2(-r * 0.4f, r * 0.45f), t * 1.2f, new Color(60, 60, 70));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.1f, 0), c + new Vector2(-r * 0.05f, r * 0.25f), t * 0.6f, new Color(255, 90, 90));
                break;
            case TroveItemId.Adrenaline:
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.4f, r * 0.4f), c + new Vector2(r * 0.4f, -r * 0.4f), t * 1.1f, new Color(220, 220, 230));
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.25f, -r * 0.25f), c + new Vector2(r * 0.5f, -r * 0.5f), t * 0.5f, new Color(160, 160, 170));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.1f, r * 0.1f), r * 0.16f, new Color(230, 40, 110));
                break;
            case TroveItemId.OverdriveEngine:
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.45f), (int)(c.Y - r * 0.3f), (int)(r * 0.9f), (int)(r * 0.6f)), r * 0.08f, new Color(90, 90, 100));
                for (int i = 0; i < 3; i++)
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.3f + i * r * 0.3f, -r * 0.3f), c + new Vector2(-r * 0.3f + i * r * 0.3f, -r * 0.5f), t * 0.6f, new Color(255, 205, 60));
                ToggleSwitch.DrawDisc(sb, c, r * 0.12f, new Color(255, 205, 60));
                break;
            case TroveItemId.NitroBoost:
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.2f), (int)(c.Y - r * 0.5f), (int)(r * 0.4f), (int)(r * 0.8f)), r * 0.15f, new Color(80, 170, 255));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.45f), r * 0.17f, new Color(255, 160, 60));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.55f), r * 0.1f, new Color(255, 230, 120));
                break;
            case TroveItemId.GatlingBarrel:
                for (int i = -1; i <= 1; i++)
                    ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.45f, i * r * 0.2f), c + new Vector2(r * 0.5f, i * r * 0.2f), t * 0.7f, new Color(150, 155, 170));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.4f, 0), r * 0.25f, new Color(90, 90, 100));
                break;
            case TroveItemId.LightweightAlloy: {
                // a light bar of metal, moving fast
                var edge = new Color(36, 132, 136);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.42f, r * 0.16f), c + new Vector2(r * 0.5f, -r * 0.3f), t * 2.3f, edge);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.38f, r * 0.14f), c + new Vector2(r * 0.46f, -r * 0.28f), t * 1.4f, new Color(238, 255, 255));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.6f, r * 0.48f), c + new Vector2(-r * 0.05f, r * 0.2f), t * 0.6f, edge);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.3f, r * 0.56f), c + new Vector2(r * 0.12f, r * 0.34f), t * 0.6f, edge);
                break;
            }
            case TroveItemId.CoffeeThermos: {
                var body = new Rectangle((int)(c.X - r * 0.27f), (int)(c.Y - r * 0.3f), (int)(r * 0.54f), (int)(r * 0.82f));
                ToggleSwitch.DrawRoundedRect(sb, body, r * 0.1f, new Color(222, 226, 234));
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle(body.X, (int)(c.Y - r * 0.02f), body.Width, (int)(r * 0.22f)), 0, new Color(208, 62, 54));
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r * 0.2f), (int)(c.Y - r * 0.48f), (int)(r * 0.4f), (int)(r * 0.2f)), r * 0.05f, new Color(58, 42, 34));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.15f, -r * 0.2f), c + new Vector2(-r * 0.15f, r * 0.42f), t * 0.5f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.42f, -r * 0.5f), c + new Vector2(r * 0.52f, -r * 0.72f), t * 0.5f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.28f, -r * 0.58f), c + new Vector2(r * 0.36f, -r * 0.76f), t * 0.5f, w);
                break;
            }
            case TroveItemId.Boost: {
                // two flame chevrons pointing forward
                var flame = new Color(255, 230, 120);
                for (int k = 0; k < 2; k++) {
                    float x = -r * 0.32f + k * r * 0.36f;
                    ToggleSwitch.DrawLine(sb, c + new Vector2(x, -r * 0.42f), c + new Vector2(x + r * 0.3f, 0), t * 1.1f, k == 0 ? Color.White : flame);
                    ToggleSwitch.DrawLine(sb, c + new Vector2(x + r * 0.3f, 0), c + new Vector2(x, r * 0.42f), t * 1.1f, k == 0 ? Color.White : flame);
                }
                break;
            }
            case TroveItemId.ChargeShot: {
                // a ring filling up around a glowing shell
                for (int i = 0; i < 24; i++) {
                    float a0 = -MathHelper.PiOver2 + i * MathHelper.TwoPi / 24, a1 = a0 + MathHelper.TwoPi / 24 * 1.1f;
                    ToggleSwitch.DrawLine(sb, c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r * 0.5f, c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r * 0.5f,
                        t * 0.6f, i < 17 ? new Color(150, 220, 255) : new Color(60, 70, 90));
                }
                ToggleSwitch.DrawDisc(sb, c, r * 0.22f, Color.White);
                break;
            }
            case TroveItemId.SacredHeart:
                ToggleSwitch.DrawDisc(sb, c + new Vector2(-r * 0.18f, -r * 0.08f), r * 0.24f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(r * 0.18f, -r * 0.08f), r * 0.24f, w);
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.14f), r * 0.24f, w);
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.55f), c + new Vector2(0, -r * 0.3f), t * 0.6f, new Color(255, 220, 90));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.12f, -r * 0.45f), c + new Vector2(r * 0.12f, -r * 0.45f), t * 0.6f, new Color(255, 220, 90));
                break;
            case TroveItemId.BlockBust: {
                // a cracked block
                var box = new Rectangle((int)(c.X - r * 0.45f), (int)(c.Y - r * 0.45f), (int)(r * 0.9f), (int)(r * 0.9f));
                ToggleSwitch.DrawRoundedRect(sb, box, r * 0.1f, new Color(235, 200, 150));
                var crack = new Color(110, 60, 30);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.1f, -r * 0.45f), c + new Vector2(r * 0.05f, -r * 0.1f), t * 0.7f, crack);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.05f, -r * 0.1f), c + new Vector2(-r * 0.15f, r * 0.15f), t * 0.7f, crack);
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.15f, r * 0.15f), c + new Vector2(r * 0.1f, r * 0.45f), t * 0.7f, crack);
                ToggleSwitch.DrawLine(sb, c + new Vector2(r * 0.05f, -r * 0.1f), c + new Vector2(r * 0.42f, r * 0.0f), t * 0.6f, crack);
                break;
            }
        }
    }

    private static void DrawDoors(SpriteBatch sb, TroveFloor floor, TroveRoom room) {
        var center = ToScreen(Vector2.Zero);
        foreach (var d in TroveGen.Doors(floor, room)) {
            var pt = TroveRun.DoorPoint(d);
            var screen = ToScreen(pt);
            var outward = screen - center;
            if (outward.LengthSquared() < 1)
                continue;
            outward.Normalize();
            // size: about 3 blocks wide on screen
            var side = new Vector2(-outward.Y, outward.X);
            var probe = ToScreen(pt + (d is TroveDir.North or TroveDir.South ? new Vector2(Block.SIDE_LENGTH * 1.5f, 0) : new Vector2(0, Block.SIDE_LENGTH * 1.5f)));
            float width = Math.Max(30f, Vector2.Distance(probe, screen) * 2f);
            var s = TroveGen.Step(d);
            var neighbor = floor.Rooms[new Point(room.Cell.X + s.X, room.Cell.Y + s.Y)];
            var tex = !room.Cleared ? _holeLocked! : neighbor.Kind == TroveRoomKind.Boss ? _holeBoss!
                : neighbor.Kind == TroveRoomKind.Item && neighbor.Seen ? _holeItem!
                : neighbor.Kind == TroveRoomKind.Challenge && neighbor.Seen ? _holeChallenge!
                : neighbor.Kind == TroveRoomKind.MiniBoss && neighbor.Seen && !neighbor.Cleared ? _holeMini!
                : neighbor.Kind == TroveRoomKind.Secret ? _holeSecret! : _hole!;
            float rot = MathF.Atan2(outward.Y, outward.X) + MathHelper.PiOver2;
            float scale = width / tex.Width;
            sb.Draw(tex, screen, null, Color.White, rot, new Vector2(tex.Width / 2f, tex.Height), scale, SpriteEffects.None, 0f);
        }
    }

    // ---- minimap (top middle) and floor name
    private static void DrawMap(SpriteBatch sb, TroveFloor floor) {
        float s = S;
        float cell = 38 * s, gap = 7 * s;
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (var r in floor.Rooms.Values) {
            if (!r.Seen) continue;
            minX = Math.Min(minX, r.Cell.X); maxX = Math.Max(maxX, r.Cell.X);
            minY = Math.Min(minY, r.Cell.Y); maxY = Math.Max(maxY, r.Cell.Y);
        }
        if (minX == int.MaxValue)
            return;
        // which way is "north" on screen (depends on the camera)
        bool flipY = ToScreen(TroveRun.DoorPoint(TroveDir.North)).Y > ToScreen(TroveRun.DoorPoint(TroveDir.South)).Y;
        bool flipX = ToScreen(TroveRun.DoorPoint(TroveDir.West)).X > ToScreen(TroveRun.DoorPoint(TroveDir.East)).X;
        int cols = maxX - minX + 1, rows = maxY - minY + 1;
        float w = cols * (cell * 1.3f + gap) + gap, h = rows * (cell + gap) + gap;
        var panel = new Rectangle((int)(170 * s), (int)(14 * s), (int)w, (int)h);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(panel.X - 4, panel.Y - 4, panel.Width + 8, panel.Height + 8), 10 * s, new Color(24, 16, 12, 200));
        foreach (var r in floor.Rooms.Values) {
            if (!r.Seen) continue;
            int cx = flipX ? maxX - r.Cell.X : r.Cell.X - minX;
            int cy = flipY ? maxY - r.Cell.Y : r.Cell.Y - minY;
            var rect = new Rectangle((int)(panel.X + gap + cx * (cell * 1.3f + gap)), (int)(panel.Y + gap + cy * (cell + gap)), (int)(cell * 1.3f), (int)cell);
            bool here = r.Cell == TroveRun.Current;
            var col = here ? Color.White : r.Kind == TroveRoomKind.Boss ? new Color(190, 40, 30)
                : r.Kind == TroveRoomKind.Item ? new Color(220, 175, 40) : r.Kind == TroveRoomKind.Challenge ? new Color(140, 60, 170)
                : r.Kind == TroveRoomKind.Secret ? new Color(120, 120, 135) : r.Kind == TroveRoomKind.MiniBoss && !r.Cleared ? new Color(230, 120, 30) : r.Visited ? new Color(180, 170, 150) : new Color(95, 85, 75);
            ToggleSwitch.DrawRoundedRect(sb, rect, 5 * s, col);
            if (here)
                ToggleSwitch.DrawDisc(sb, rect.Center.ToVector2(), cell * 0.25f, new Color(40, 120, 230));
            else if (r.Kind == TroveRoomKind.Boss)
                Text(sb, "!", rect.Center.ToVector2(), 0.8f * s, new Color(255, 230, 200));
            else if (r.Kind is TroveRoomKind.Item or TroveRoomKind.Secret && r.PickupVisible)
                Text(sb, r.Kind == TroveRoomKind.Secret && !floor.SecretFound ? "?" : "$", rect.Center.ToVector2(), 0.7f * s, new Color(60, 40, 10));
            else if (r.Kind == TroveRoomKind.MiniBoss && !r.Cleared)
                Text(sb, "!", rect.Center.ToVector2(), 0.6f * s, new Color(255, 240, 220));
            else if (r.Kind == TroveRoomKind.Challenge && !r.Cleared)
                Text(sb, "X", rect.Center.ToVector2(), 0.6f * s, new Color(255, 220, 255));
        }
        var nameAt = new Vector2(panel.Center.X, panel.Bottom + 28 * s);
        Text(sb, $"Floor {floor.Number}: {floor.Name}", nameAt, 0.75f * s, Color.White, border: true);
        // your items (passive ones, and what's in the ITEM slot)
        int me = Net.NetPlay.GetMyClientId();
        var mine = TroveItems.Players[me];
        float ir = 22 * s, x = panel.X + ir;
        float y = nameAt.Y + 50 * s;
        // one icon per item, with a count when it's stacked
        foreach (var kv in mine.Passives) {
            DrawItemIcon(sb, kv.Key, new Vector2(x, y), ir);
            if (kv.Value > 1)
                Text(sb, $"x{kv.Value}", new Vector2(x + ir * 0.75f, y + ir * 0.8f), 0.5f * s, Color.White, border: true);
            x += ir * 2.4f;
        }
        if (mine.Shields > 0) {
            DrawItemIcon(sb, TroveItemId.Shield, new Vector2(x, y), ir);
            Text(sb, $"x{mine.Shields}", new Vector2(x + ir * 0.75f, y + ir * 0.8f), 0.5f * s, Color.White, border: true);
            x += ir * 2.4f;
        }
        // health from stat items (refills each room / floor)
        if (mine.RoomHp + mine.FloorHp > 0) {
            ToggleSwitch.DrawDisc(sb, new Vector2(x, y), ir, new Color(60, 170, 80));
            Text(sb, "+", new Vector2(x, y), 0.7f * s, Color.White, border: true);
            Text(sb, $"x{mine.RoomHp + mine.FloorHp}", new Vector2(x + ir * 0.75f, y + ir * 0.8f), 0.5f * s, Color.White, border: true);
            x += ir * 2.4f;
        }
        // (ITEM-button items are shown under the ITEM button: see DrawItemStrip)
        // synergies you have
        float sy = y + ir * 2.1f;
        foreach (var syn in TroveItems.Synergies)
            if (mine.Has(syn)) {
                var font = FontGlobals.RebirthFont;
                var t = "* " + syn.Name;
                sb.DrawString(font, t, new Vector2(panel.X + 2, sy) + new Vector2(2), Color.Black * 0.7f, new Vector2(0.6f * s), 0f, Vector2.Zero);
                sb.DrawString(font, t, new Vector2(panel.X + 2, sy), new Color(255, 215, 80), new Vector2(0.6f * s), 0f, Vector2.Zero);
                sy += font.MeasureString(t).Y * 0.6f * s * 1.1f;
            }
    }

    // ---- "READY" while a room with enemies waits to start, then "GO!" as it does
    private static bool _saidGo = true;
    private static void DrawReadyGo(SpriteBatch sb) {
        if (!CampaignGlobals.InMission || TroveRun.ReadyUntil < 0)
            return;
        float s = S;
        double left = TroveRun.ReadyUntil - TroveRun.PlayTime;
        var at = new Vector2(WindowUtils.WindowWidth / 2f, WindowUtils.WindowHeight * 0.42f);
        if (left > 0) {
            _saidGo = false;
            float pulse = 1f + 0.06f * MathF.Sin((float)TroveRun.PlayTime * 6f);
            Text(sb, "READY", at, 2.2f * s * pulse, Color.White, border: true);
        }
        else if (left > -0.7) {
            if (!_saidGo) {
                _saidGo = true;
                SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", SoundContext.Effect, 0.8f, pitchOverride: 0.3f);
            }
            float k = (float)(-left / 0.7);
            Text(sb, "GO!", at, (2.4f + k) * s, new Color(255, 220, 80) * (1 - k), border: true);
        }
    }

    // ---- "<item> broke!" for a moment after an ITEM-button item's last use
    private static void DrawBroken(SpriteBatch sb) {
        if (TroveItems.LastBroken is not { } lb)
            return;
        double age = TroveRun.PlayTime - lb.Time;
        if (age < 0 || age > 2.5)
            return;
        float s = S;
        float a = (float)Math.Clamp((2.5 - age) * 2, 0, 1);
        var tc = Internals.Common.Framework.Input.TouchControls.ItemButtonCenter;
        float R = Internals.Common.Framework.Input.TouchControls.ItemButtonRadius;
        var at = new Vector2(Math.Clamp(tc.X, 200 * s, WindowUtils.WindowWidth - 200 * s), tc.Y + R * 1.6f);
        Text(sb, $"{lb.Def.Name} broke!", at, 0.6f * s, new Color(255, 120, 100) * a, border: true);
    }

    // ---- "SYNERGY!" for a few seconds after two items click
    private static void DrawSynergyBanner(SpriteBatch sb) {
        if (TroveItems.LastSynergy is not { } ls)
            return;
        double age = TroveRun.PlayTime - ls.Time;
        if (age < 0.6 || age > 5)
            return;
        float s = S;
        float a = (float)Math.Clamp(Math.Min((age - 0.6) * 4, (5 - age) * 2), 0, 1);
        var c = new Vector2(WindowUtils.WindowWidth / 2f, (TroveRun.BossTank is { IsDestroyed: false } ? 160 : 110) * s + 150 * s);
        var box = new Rectangle((int)(c.X - 430 * s), (int)(c.Y - 50 * s), (int)(860 * s), (int)(100 * s));
        ToggleSwitch.DrawRoundedRect(sb, box, 22 * s, new Color(60, 30, 90) * (0.95f * a));
        Text(sb, $"SYNERGY!  {ls.Synergy.Name.ToUpperInvariant()}", new Vector2(c.X, box.Y + 32 * s), 0.85f * s, new Color(255, 215, 80) * a, border: true);
        Text(sb, ls.Synergy.Description, new Vector2(c.X, box.Y + 74 * s), 0.72f * s, Color.White * a);
    }

    // ---- the boss's health, across the top of the screen
    private static void DrawBossBar(SpriteBatch sb) {
        if (TroveBosses.Boss is null || TroveBosses.HitsLeft() <= 0 || TroveRun.FloorNumber < 1)
            return;
        float s = S;
        int hits = TroveBosses.HitsLeft(); // (both twins together)
        float frac = Math.Clamp(hits / (float)Math.Max(1, TroveBosses.MaxHits), 0f, 1f);
        float w = 760 * s, h = 26 * s;
        var at = new Vector2(WindowUtils.WindowWidth / 2f - w / 2f, 52 * s);
        Text(sb, TroveRun.BossTitle, new Vector2(WindowUtils.WindowWidth / 2f, at.Y - 22 * s), 0.75f * s, Color.White, border: true);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)at.X - 4, (int)at.Y - 4, (int)w + 8, (int)h + 8), h * 0.6f, new Color(20, 14, 10));
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)at.X, (int)at.Y, (int)w, (int)h), h * 0.5f, new Color(70, 20, 18));
        if (frac > 0)
            ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)at.X, (int)at.Y, (int)Math.Max(h, w * frac), (int)h), h * 0.5f, new Color(210, 40, 30));
    }

    // ---- "You got ..." for a few seconds after a pickup
    private static void DrawPickupBanner(SpriteBatch sb) {
        if (TroveItems.LastPickup is not { } lp)
            return;
        double age = TroveRun.PlayTime - lp.Time;
        if (age < 0 || age > 4)
            return;
        float s = S;
        float a = (float)Math.Clamp(Math.Min(age * 4, (4 - age) * 2), 0, 1);
        var c = new Vector2(WindowUtils.WindowWidth / 2f, TroveRun.BossTank is { IsDestroyed: false } ? 160 * s : 110 * s);
        var box = new Rectangle((int)(c.X - 430 * s), (int)(c.Y - 55 * s), (int)(860 * s), (int)(120 * s));
        ToggleSwitch.DrawRoundedRect(sb, box, 22 * s, new Color(250, 244, 230) * (0.95f * a));
        DrawItemIcon(sb, lp.Def.Id, new Vector2(box.X + 70 * s, box.Center.Y), 40 * s);
        var font = FontGlobals.RebirthFont;
        var title = lp.Stack > 1 ? $"{lp.Def.Name.ToUpperInvariant()}  x{lp.Stack}" : lp.Def.Name.ToUpperInvariant();
        sb.DrawString(font, title, new Vector2(box.X + 130 * s, box.Y + 32 * s), lp.Def.Color * a, new Vector2(1.0f * s), 0f, new Vector2(0, font.MeasureString("A").Y / 2f));
        sb.DrawString(font, lp.Def.Description, new Vector2(box.X + 130 * s, box.Y + 82 * s), new Color(80, 62, 46) * a, new Vector2(0.62f * s), 0f, new Vector2(0, font.MeasureString("A").Y / 2f));
    }

    private static readonly Vector2[] _borderOffsets = [new(-2, 0), new(2, 0), new(0, -2), new(0, 2)];
    internal static void Text(SpriteBatch sb, string t, Vector2 center, float scale, Color color, bool border = false) {
        var font = FontGlobals.RebirthFont;
        var m = font.MeasureString(t);
        // nothing on the HUD is smaller than this (tags and counters were hard to read on a phone)
        scale = Math.Max(scale, 0.6f * S);
        if (ToggleSwitch.Override is { } shade) {
            // (an icon's shadow pass)
            sb.DrawString(font, t, center + ToggleSwitch.Shift, shade, new Vector2(scale), 0f, m / 2f);
            return;
        }
        if (border)
            foreach (var o in _borderOffsets)
                sb.DrawString(font, t, center + o, Color.Black * 0.8f, new Vector2(scale), 0f, m / 2f);
        sb.DrawString(font, t, center, color, new Vector2(scale), 0f, m / 2f);
    }

    // ---- run over / victory
    private static bool Tapped(Rectangle r) {
        bool hit = !_down && _wasDown && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        if (hit) {
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);
            Internals.Common.Framework.Input.Haptics.Tap();
        }
        return hit;
    }

    private static void DrawEnd(SpriteBatch sb) {
        _wasDown = _down;
        _down = InputUtils.CurrentMouseSnapshot.LeftButton == ButtonState.Pressed;
        if (_down && !_wasDown)
            _press = MouseUtils.MousePosition;

        float s = S;
        int w = WindowUtils.WindowWidth, h = WindowUtils.WindowHeight;
        bool won = TroveRun.Ended == TroveRun.EndKind.Victory;
        bool decider = !Client.IsConnected() || Client.IsHost();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        sb.Draw(TextureGlobals.Pixels[Color.White], new Rectangle(0, 0, w, h), Color.Black * 0.6f);
        var panel = new Rectangle((int)(w / 2f - 560 * s), (int)(h / 2f - 330 * s), (int)(1120 * s), (int)(660 * s));
        int lip = (int)(10 * s);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(panel.X, panel.Y + lip, panel.Width, panel.Height), 36 * s, UITextButton.PhoneEdge);
        ToggleSwitch.DrawRoundedRect(sb, panel, 36 * s, UITextButton.PhoneFill);
        var ink = new Color(40, 30, 22);
        var cx = panel.Center.X;
        if (TroveRun.Battle)
            BattleHud.DrawFinal(sb, panel);
        else {
            Text(sb, won ? "Trove Conquered!" : "Run Over", new Vector2(cx, panel.Y + 85 * s), 2.0f * s, won ? new Color(190, 130, 20) : new Color(170, 50, 40));
            var t = TimeSpan.FromSeconds(TroveRun.PlayTime);
            Text(sb, won ? "You beat all 5 floors." : $"Reached floor {TroveRun.FloorNumber}: {TroveRun.Floor?.Name}", new Vector2(cx, panel.Y + 175 * s), 1.0f * s, ink);
            Text(sb, $"Rooms cleared: {TroveRun.RoomsCleared}     Tanks destroyed: {TroveRun.TanksDestroyed}     Time: {(int)t.TotalMinutes}:{t.Seconds:00}",
                new Vector2(cx, panel.Y + 245 * s), 0.8f * s, UITextButton.PhoneEdge);
        }

        float bw = 400 * s, bh = 130 * s, by = panel.Bottom - 70 * s - bh;
        if (decider) {
            var again = new Rectangle((int)(cx - bw - 30 * s), (int)by, (int)bw, (int)bh);
            var menu = new Rectangle((int)(cx + 30 * s), (int)by, (int)bw, (int)bh);
            Button(sb, again, TroveRun.Battle ? "Play Again" : "Try Again", new Color(255, 226, 140));
            Button(sb, menu, "Menu", Color.White);
            if (Tapped(again)) { sb.End(); TroveRun.TryAgain(); return; }
            if (Tapped(menu)) { sb.End(); TroveRun.ToMenu(); return; }
        }
        else {
            int dots = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 2) % 4;
            if (!TroveRun.Battle)
                Text(sb, "Waiting on host" + new string('.', dots), new Vector2(cx, panel.Y + 330 * s), 1.1f * s, ink);
            var leave = new Rectangle((int)(cx - bw / 2), (int)by, (int)bw, (int)bh);
            Button(sb, leave, "Leave", Color.White);
            if (Tapped(leave)) { sb.End(); TroveRun.Leave(); return; }
        }
        sb.End();
    }

    private static void Button(SpriteBatch sb, Rectangle r, string label, Color fill) => Button(sb, r, label, fill, _down, _press);
    internal static void Button(SpriteBatch sb, Rectangle r, string label, Color fill, bool down, Vector2 press) {
        int lip = Math.Max(3, (int)(r.Height * 0.07f));
        bool pressing = down && r.Contains(press) && r.Contains(MouseUtils.MousePosition);
        var face = pressing ? new Rectangle(r.X, r.Y + lip / 2, r.Width, r.Height - lip / 2) : new Rectangle(r.X, r.Y, r.Width, r.Height - lip);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X, r.Y + lip, r.Width, r.Height - lip), r.Height * 0.3f, UITextButton.PhoneEdge);
        ToggleSwitch.DrawRoundedRect(sb, face, r.Height * 0.3f, pressing ? UITextButton.PhonePressed : fill);
        var inkCol = new Color(40, 30, 22);
        if (label is "<" or ">") {
            // arrows are drawn (the font's are thin and small)
            var c = face.Center.ToVector2();
            float a = face.Height * 0.2f, dir = label == "<" ? -1f : 1f, thick = face.Height * 0.11f;
            var tip = c + new Vector2(dir * a * 0.6f, 0);
            ToggleSwitch.DrawLine(sb, tip, tip + new Vector2(-dir * a * 1.1f, -a), thick, inkCol);
            ToggleSwitch.DrawLine(sb, tip, tip + new Vector2(-dir * a * 1.1f, a), thick, inkCol);
            ToggleSwitch.DrawDisc(sb, tip, thick / 2f, inkCol);
            return;
        }
        var font = FontGlobals.RebirthFont;
        var m = font.MeasureString(label);
        float sc = Math.Min(face.Height * 0.46f / Math.Max(1f, m.Y), (face.Width - face.Height * 0.4f) / Math.Max(1f, m.X));
        sb.DrawString(font, label, face.Center.ToVector2(), inkCol, new Vector2(sc), 0f, m / 2f);
    }

    // ---- door pictures (made once): a wooden rim around a dark arch, with bars while the room isn't cleared
    private static void EnsureTextures(GraphicsDevice gd) {
        if (_hole is { IsDisposed: false })
            return;
        _hole = MakeHole(gd, new Color(150, 112, 70), false);
        _holeLocked = MakeHole(gd, new Color(120, 96, 70), true);
        _holeBoss = MakeHole(gd, new Color(170, 34, 26), false);
        _holeItem = MakeHole(gd, new Color(220, 175, 40), false);
        _holeChallenge = MakeHole(gd, new Color(140, 50, 170), false);
        _holeSecret = MakeHole(gd, new Color(120, 120, 130), false);
        _holeMini = MakeHole(gd, new Color(230, 120, 30), false);
    }

    private static Texture2D MakeHole(GraphicsDevice gd, Color rim, bool bars) {
        const int W = 128, H = 104;
        var data = new Color[W * H];
        float cx = W / 2f, r = W / 2f - 1, inner = r - 13;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++) {
                // an arch: half circle on top of a short rectangle (bottom edge = the wall line)
                float dy = Math.Max(0, (r) - y); // distance above the circle centre line (centre at y = r)
                float dist = y < r ? MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (r - y) * (r - y)) : MathF.Abs(x + 0.5f - cx);
                Color c = Color.Transparent;
                if (dist <= r) {
                    c = rim;
                    if (dist <= inner) {
                        // inside: dark, a little lighter toward the bottom
                        float t = y / (float)H;
                        c = Color.Lerp(new Color(10, 6, 4), new Color(32, 22, 16), t);
                        if (bars && ((int)(x - cx + 200) % 22) < 6)
                            c = new Color(130, 120, 110);
                    }
                    // soft outer edge
                    float edge = Math.Clamp(r - dist, 0f, 1f);
                    c = new Color((byte)(c.R * edge), (byte)(c.G * edge), (byte)(c.B * edge), (byte)(255 * edge));
                }
                data[y * W + x] = c;
            }
        var tex = new Texture2D(gd, W, H);
        tex.SetData(data);
        return tex;
    }
}
