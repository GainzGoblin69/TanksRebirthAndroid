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

    private static Vector2 ToScreen(Vector2 world) =>
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
            DrawInRoom(sb);
            DrawItemStrip(sb);
            DrawMap(sb, floor);
            DrawBossBar(sb);
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
                Text(sb, "BUDDY", ToScreen(b.Position) - new Vector2(0, 46 * s), 0.42f * s, new Color(150, 210, 255), border: true);
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
            DrawItemIcon(sb, id, at, r);
            bool ready = p.IsReady(id);
            if (!ready) {
                // dimmed while recharging, with how far along it is
                ToggleSwitch.DrawDisc(sb, at, r * 1.02f, Color.Black * 0.55f);
                var def = TroveItems.Def(id);
                string c = def.RechargeSeconds > 0 ? $"{Math.Ceiling(p.ReadyAt - TroveRun.PlayTime)}s" : $"{p.ChargeOf(id)}/{def.Recharge}";
                Text(sb, c, at, 0.38f * s, Color.White, border: true);
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
        Text(sb, d.Name + usesText, new Vector2(rowCenter.X, y), 0.6f * s, Color.Lerp(d.Color, Color.White, 0.5f) * alpha, border: true);
        foreach (var line in Wrap(d.Description, 0.5f * s, 560 * s)) {
            y += lh * 0.5f * s * 1.05f;
            Text(sb, line, new Vector2(rowCenter.X, y), 0.5f * s, Color.White * alpha, border: true);
        }
        if (n > 1 && since < 6)
            Text(sb, "tap an icon or press X to switch", new Vector2(rowCenter.X, y + lh * 0.42f * s * 1.2f), 0.4f * s, new Color(220, 210, 190) * alpha, border: true);
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
                : $"ITEM button  -  {def.MaxUses} uses, recharges every {def.Recharge} room{(def.Recharge > 1 ? "s" : "")}",
            TroveItemKind.Passive => TroveItems.Stacks(def.Id) ? "Lasts the whole run  -  stacks" : "Lasts the whole run",
            _ => "Takes effect right away",
        };
        var hint = TroveItems.SynergyHint(Net.NetPlay.GetMyClientId(), def.Id);
        float nameScale = 0.95f * s, descScale = 0.62f * s, kindScale = 0.5f * s;
        float maxW = 820 * s;
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
            case TroveItemId.HeavyShells:
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, r * 0.45f), c + new Vector2(0, -r * 0.2f), r * 0.5f, new Color(60, 55, 50));
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, -r * 0.22f), r * 0.25f, new Color(60, 55, 50));
                break;
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
            case TroveItemId.WallBuilder:
                for (int i = -1; i <= 1; i++)
                    ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X + i * r * 0.36f - r * 0.16f), (int)(c.Y - r * 0.18f), (int)(r * 0.32f), (int)(r * 0.36f)), r * 0.04f, new Color(240, 210, 160));
                break;
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
            case TroveItemId.GhostShells:
                ToggleSwitch.DrawLine(sb, c + new Vector2(0, -r * 0.55f), c + new Vector2(0, r * 0.55f), r * 0.2f, new Color(255, 255, 255, 110));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.6f, r * 0.15f), c + new Vector2(r * 0.6f, -r * 0.15f), r * 0.16f, w);
                break;
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
            case TroveItemId.SecondWind:
                foreach (float k in new[] { -1f, 1f }) {
                    ToggleSwitch.DrawLine(sb, c + new Vector2(k * r * 0.1f, 0), c + new Vector2(k * r * 0.6f, -r * 0.3f), t, w);
                    ToggleSwitch.DrawLine(sb, c + new Vector2(k * r * 0.1f, r * 0.1f), c + new Vector2(k * r * 0.5f, r * 0.05f), t * 0.8f, w);
                }
                ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.25f), r * 0.14f, w);
                break;
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
            case TroveItemId.MirrorShield:
                ToggleSwitch.DrawDisc(sb, c, r * 0.5f, w);
                ToggleSwitch.DrawDisc(sb, c, r * 0.4f, new Color(170, 200, 230));
                ToggleSwitch.DrawLine(sb, c + new Vector2(-r * 0.2f, -r * 0.2f), c + new Vector2(r * 0.05f, -r * 0.3f), t * 0.6f, w);
                break;
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
        // (ITEM-button items are shown under the ITEM button: see DrawItemStrip)
        // synergies you have
        float sy = y + ir * 2.1f;
        foreach (var syn in TroveItems.Synergies)
            if (mine.Has(syn)) {
                var font = FontGlobals.RebirthFont;
                var t = "* " + syn.Name;
                sb.DrawString(font, t, new Vector2(panel.X + 2, sy) + new Vector2(2), Color.Black * 0.7f, new Vector2(0.42f * s), 0f, Vector2.Zero);
                sb.DrawString(font, t, new Vector2(panel.X + 2, sy), new Color(255, 215, 80), new Vector2(0.42f * s), 0f, Vector2.Zero);
                sy += font.MeasureString(t).Y * 0.42f * s * 1.1f;
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
        Text(sb, ls.Synergy.Description, new Vector2(c.X, box.Y + 74 * s), 0.55f * s, Color.White * a);
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
