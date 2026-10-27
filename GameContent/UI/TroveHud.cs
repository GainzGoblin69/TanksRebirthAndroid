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
    private static Texture2D? _hole, _holeLocked, _holeBoss;
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
        if (!banner && TroveRun.Floor is { } floor && TroveRun.Room is { } room && TroveRun.Fade < 1f) {
            DrawDoors(sb, floor, room);
            DrawMap(sb, floor);
        }
        if (TroveRun.Fade > 0)
            sb.Draw(TextureGlobals.Pixels[Color.White], new Rectangle(0, 0, WindowUtils.WindowWidth, WindowUtils.WindowHeight), Color.Black * TroveRun.Fade);
        sb.End();
        if (TroveRun.ShowingEnd)
            DrawEnd(sb);
    }

    // ---- doors: dark arched "mouse holes" in the middle of each wall that leads somewhere
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
            var tex = !room.Cleared ? _holeLocked! : neighbor.Kind == TroveRoomKind.Boss ? _holeBoss! : _hole!;
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
        var panel = new Rectangle((int)(WindowUtils.WindowWidth / 2f - w / 2f), (int)(10 * s), (int)w, (int)h);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(panel.X - 4, panel.Y - 4, panel.Width + 8, panel.Height + 8), 10 * s, new Color(24, 16, 12, 200));
        foreach (var r in floor.Rooms.Values) {
            if (!r.Seen) continue;
            int cx = flipX ? maxX - r.Cell.X : r.Cell.X - minX;
            int cy = flipY ? maxY - r.Cell.Y : r.Cell.Y - minY;
            var rect = new Rectangle((int)(panel.X + gap + cx * (cell * 1.3f + gap)), (int)(panel.Y + gap + cy * (cell + gap)), (int)(cell * 1.3f), (int)cell);
            bool here = r.Cell == TroveRun.Current;
            var col = here ? Color.White : r.Kind == TroveRoomKind.Boss ? new Color(190, 40, 30) : r.Visited ? new Color(180, 170, 150) : new Color(95, 85, 75);
            ToggleSwitch.DrawRoundedRect(sb, rect, 5 * s, col);
            if (here)
                ToggleSwitch.DrawDisc(sb, rect.Center.ToVector2(), cell * 0.25f, new Color(40, 120, 230));
            else if (r.Kind == TroveRoomKind.Boss)
                Text(sb, "!", rect.Center.ToVector2(), 0.8f * s, new Color(255, 230, 200));
        }
        Text(sb, $"Floor {floor.Number}: {floor.Name}", new Vector2(WindowUtils.WindowWidth / 2f, panel.Bottom + 30 * s), 0.85f * s, Color.White, border: true);
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
                    c = new Color(c.R, c.G, c.B, (byte)(255 * edge));
                }
                data[y * W + x] = c;
            }
        var tex = new Texture2D(gd, W, H);
        tex.SetData(data);
        return tex;
    }
}
