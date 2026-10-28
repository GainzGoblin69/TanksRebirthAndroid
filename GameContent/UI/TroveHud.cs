using System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.Systems.Troves;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.UI;

/// <summary>Tank Troves on screen: the mouse-hole doors, the minimap, the floor name, room fades and the end screen.</summary>
public static class TroveHud {
    private static Texture2D? _hole, _holeLocked, _holeBoss, _holeItem;
    private static Vector2 _press = new(-1);
    private static bool _down, _wasDown;

    public static void EndShown() => _down = _wasDown = true;

    private static float S => WindowUtils.WindowHeight / 1080f;

    private static Vector2 ToScreen(Vector2 world) =>
        MatrixUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(world.X, 0, world.Y), CameraGlobals.GameView, CameraGlobals.GameProjection);

    public static void Draw(SpriteBatch sb) {
        if (!TroveRun.Active || MainMenu.MainMenuUI.IsActive)
            return;
        EnsureTextures(sb.GraphicsDevice);
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        // not while the floor banner (intermission) is up
        bool banner = IntermissionSystem.Alpha > 0.05f || IntermissionSystem.BlackAlpha > 0.05f;
        if (!banner && TroveRun.Floor is { } floor && TroveRun.Fade < 1f) {
            DrawMap(sb, floor);
            DrawBossBar(sb);
            DrawPickupBanner(sb);
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
        if (room.PickupVisible)
            DrawPedestal(sb, room);
    }

    // ---- an item in the middle of the room (on a stone pedestal in item rooms)
    private static void DrawPedestal(SpriteBatch sb, TroveRoom room) {
        var c = ToScreen(TroveRun.PickupPoint);
        var edge = ToScreen(TroveRun.PickupPoint + new Vector2(Block.SIDE_LENGTH * 0.8f, 0));
        float r = Math.Max(14f, Vector2.Distance(c, edge));
        float bob = MathF.Sin((float)TroveRun.PlayTime * 3f) * r * 0.12f;
        if (room.Kind == TroveRoomKind.Item) {
            DiscPM(sb, c + new Vector2(0, r * 0.25f), r * 1.15f, new Color(0, 0, 0) * 0.35f);
            DiscPM(sb, c, r * 1.05f, new Color(110, 104, 98));
            DiscPM(sb, c, r * 0.85f, new Color(150, 144, 136));
        }
        else
            DiscPM(sb, c + new Vector2(0, r * 0.2f), r * 0.8f, new Color(0, 0, 0) * 0.3f);
        // glow
        DiscPM(sb, c - new Vector2(0, r * 0.9f + bob), r * 1.0f, new Color(255, 220, 120) * 0.25f);
        DrawItemIcon(sb, room.Pickup!.Value, c - new Vector2(0, r * 0.9f + bob), r * 0.75f, premultiplied: true);
    }

    // discs inside the game's HUD batch (premultiplied alpha)
    private static void DiscPM(SpriteBatch sb, Vector2 c, float r, Color col) => ToggleSwitch.DrawDisc(sb, c, r, col);

    /// <summary>An item's picture: a colored disc with a simple symbol.</summary>
    public static void DrawItemIcon(SpriteBatch sb, TroveItemId id, Vector2 c, float r, bool premultiplied = false) {
        var def = TroveItems.Def(id);
        ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.08f), r * 1.05f, Color.Black * 0.4f);
        ToggleSwitch.DrawDisc(sb, c, r, def.Color);
        ToggleSwitch.DrawDisc(sb, c, r * 0.82f, Color.Lerp(def.Color, Color.White, 0.25f));
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
                : neighbor.Kind == TroveRoomKind.Item && neighbor.Seen ? _holeItem! : _hole!;
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
                : r.Kind == TroveRoomKind.Item ? new Color(220, 175, 40) : r.Visited ? new Color(180, 170, 150) : new Color(95, 85, 75);
            ToggleSwitch.DrawRoundedRect(sb, rect, 5 * s, col);
            if (here)
                ToggleSwitch.DrawDisc(sb, rect.Center.ToVector2(), cell * 0.25f, new Color(40, 120, 230));
            else if (r.Kind == TroveRoomKind.Boss)
                Text(sb, "!", rect.Center.ToVector2(), 0.8f * s, new Color(255, 230, 200));
            else if (r.Kind == TroveRoomKind.Item && r.PickupVisible)
                Text(sb, "$", rect.Center.ToVector2(), 0.7f * s, new Color(110, 70, 10));
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
            Text(sb, $"x{mine.Shields}", new Vector2(x + ir * 1.6f, y), 0.55f * s, Color.White, border: true);
        }
    }

    // ---- the boss's health, across the top of the screen
    private static void DrawBossBar(SpriteBatch sb) {
        if (TroveRun.BossTank is not { IsDestroyed: false } boss || TroveRun.FloorNumber < 1)
            return;
        float s = S;
        int hits = (boss.Properties.Armor?.HitPoints ?? 0) + 1;
        float frac = Math.Clamp(hits / (float)Math.Max(1, TroveRun.BossMaxHits), 0f, 1f);
        float w = 760 * s, h = 26 * s;
        var at = new Vector2(WindowUtils.WindowWidth / 2f - w / 2f, 52 * s);
        Text(sb, TroveRun.BossNames[Math.Clamp(TroveRun.FloorNumber - 1, 0, 4)], new Vector2(WindowUtils.WindowWidth / 2f, at.Y - 22 * s), 0.75f * s, Color.White, border: true);
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

    private static void Text(SpriteBatch sb, string t, Vector2 center, float scale, Color color, bool border = false) {
        var font = FontGlobals.RebirthFont;
        var m = font.MeasureString(t);
        if (border)
            foreach (var o in new[] { new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, -2), new Vector2(0, 2) })
                sb.DrawString(font, t, center + o, Color.Black * 0.8f, new Vector2(scale), 0f, m / 2f);
        sb.DrawString(font, t, center, color, new Vector2(scale), 0f, m / 2f);
    }

    // ---- run over / victory
    private static bool Tapped(Rectangle r) {
        bool hit = !_down && _wasDown && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        if (hit)
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);
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
        Text(sb, won ? "Trove Conquered!" : "Run Over", new Vector2(cx, panel.Y + 85 * s), 2.0f * s, won ? new Color(190, 130, 20) : new Color(170, 50, 40));
        var t = TimeSpan.FromSeconds(TroveRun.PlayTime);
        Text(sb, won ? "You beat all 5 floors." : $"Reached floor {TroveRun.FloorNumber}: {TroveRun.Floor?.Name}", new Vector2(cx, panel.Y + 175 * s), 1.0f * s, ink);
        Text(sb, $"Rooms cleared: {TroveRun.RoomsCleared}     Tanks destroyed: {TroveRun.TanksDestroyed}     Time: {(int)t.TotalMinutes}:{t.Seconds:00}",
            new Vector2(cx, panel.Y + 245 * s), 0.8f * s, UITextButton.PhoneEdge);

        float bw = 400 * s, bh = 130 * s, by = panel.Bottom - 70 * s - bh;
        if (decider) {
            var again = new Rectangle((int)(cx - bw - 30 * s), (int)by, (int)bw, (int)bh);
            var menu = new Rectangle((int)(cx + 30 * s), (int)by, (int)bw, (int)bh);
            Button(sb, again, "Try Again", new Color(255, 226, 140));
            Button(sb, menu, "Menu", Color.White);
            if (Tapped(again)) { sb.End(); TroveRun.TryAgain(); return; }
            if (Tapped(menu)) { sb.End(); TroveRun.ToMenu(); return; }
        }
        else {
            int dots = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 2) % 4;
            Text(sb, "Waiting on host" + new string('.', dots), new Vector2(cx, panel.Y + 330 * s), 1.1f * s, ink);
            var leave = new Rectangle((int)(cx - bw / 2), (int)by, (int)bw, (int)bh);
            Button(sb, leave, "Leave", Color.White);
            if (Tapped(leave)) { sb.End(); TroveRun.Leave(); return; }
        }
        sb.End();
    }

    private static void Button(SpriteBatch sb, Rectangle r, string label, Color fill) {
        int lip = Math.Max(3, (int)(r.Height * 0.07f));
        bool pressing = _down && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        var face = pressing ? new Rectangle(r.X, r.Y + lip / 2, r.Width, r.Height - lip / 2) : new Rectangle(r.X, r.Y, r.Width, r.Height - lip);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X, r.Y + lip, r.Width, r.Height - lip), r.Height * 0.3f, UITextButton.PhoneEdge);
        ToggleSwitch.DrawRoundedRect(sb, face, r.Height * 0.3f, pressing ? UITextButton.PhonePressed : fill);
        var font = FontGlobals.RebirthFont;
        var m = font.MeasureString(label);
        sb.DrawString(font, label, face.Center.ToVector2(), new Color(40, 30, 22), new Vector2(face.Height * 0.4f / Math.Max(1f, m.Y)), 0f, m / 2f);
    }

    // ---- door pictures (made once): a wooden rim around a dark arch, with bars while the room isn't cleared
    private static void EnsureTextures(GraphicsDevice gd) {
        if (_hole is { IsDisposed: false })
            return;
        _hole = MakeHole(gd, new Color(150, 112, 70), false);
        _holeLocked = MakeHole(gd, new Color(120, 96, 70), true);
        _holeBoss = MakeHole(gd, new Color(170, 34, 26), false);
        _holeItem = MakeHole(gd, new Color(220, 175, 40), false);
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
