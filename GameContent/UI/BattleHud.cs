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
using TanksRebirth.Internals;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals.UI;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.UI;

/// <summary>Battle on screen: the options screen before a match, the scoreboard, the hill and flag, and the standings between rounds.</summary>
public static class BattleHud {
    private static float S => WindowUtils.WindowHeight / 1080f;
    private static double Now => TroveRun.PlayTime;

    // ------------------------------------------------------------------ during a round

    /// <summary>Drawn inside Tank Troves' HUD batch while a battle round is on.</summary>
    public static void DrawInGame(SpriteBatch sb) {
        if (!TroveRun.Battle || BattleMode.Fighters.Count == 0)
            return;
        float s = S;
        var fighters = BattleMode.Fighters;

        // the hill: a ring in the middle, in the colour of whoever holds it alone
        if (BattleMode.Match == BattleMatch.KingOfTheHill) {
            var c = TroveHud.ToScreen(Vector2.Zero);
            float r = Vector2.Distance(c, TroveHud.ToScreen(new Vector2(BattleMode.HILL_RADIUS, 0)));
            var on = fighters.Where(f => f.Alive && Vector2.Distance(f.Tank!.Position, Vector2.Zero) < BattleMode.HILL_RADIUS).ToList();
            var col = on.Count == 1 ? on[0].Color : on.Count > 1 ? Color.White : new Color(255, 230, 140);
            float pulse = 0.85f + 0.15f * MathF.Sin((float)Now * 4f);
            ToggleSwitch.DrawDisc(sb, c, r, col * (0.18f * pulse));
            for (int i = 0; i < 40; i++) {
                float a0 = i * MathHelper.TwoPi / 40, a1 = (i + 0.6f) * MathHelper.TwoPi / 40;
                ToggleSwitch.DrawLine(sb, c + new Vector2(MathF.Cos(a0), MathF.Sin(a0) * 0.8f) * r, c + new Vector2(MathF.Cos(a1), MathF.Sin(a1) * 0.8f) * r, 4 * s, col * 0.8f);
            }
            if (on.Count > 1)
                TroveHud.Text(sb, "CONTESTED", c - new Vector2(0, r + 18 * s), 0.5f * s, Color.White, border: true);
        }

        // capture the flag: each corner is someone's base; the flag sits in the middle until it's taken
        if (BattleMode.Match == BattleMatch.CaptureTheFlag) {
            foreach (var f in fighters) {
                var b = TroveHud.ToScreen(f.Base);
                float r = Vector2.Distance(b, TroveHud.ToScreen(f.Base + new Vector2(BattleMode.BASE_REACH, 0)));
                ToggleSwitch.DrawDisc(sb, b, r, f.Color * 0.22f);
                ToggleSwitch.DrawDisc(sb, b, r * 0.35f, f.Color * 0.4f);
            }
            var flagPos = BattleMode.FlagCarrier >= 0 && fighters[BattleMode.FlagCarrier].Tank is { } ct ? ct.Position : BattleMode.FlagAt;
            DrawFlag(sb, TroveHud.ToScreen(flagPos) - new Vector2(0, BattleMode.FlagCarrier >= 0 ? 30 * s : 0), s,
                BattleMode.FlagCarrier >= 0 ? fighters[BattleMode.FlagCarrier].Color : Color.White);
        }

        // names over the tanks (so you can tell who's who)
        foreach (var f in fighters)
            if (f.Tank is { IsDestroyed: false } t)
                TroveHud.Text(sb, f.Name, TroveHud.ToScreen(t.Position) - new Vector2(0, 50 * s), 0.42f * s, f.Color, border: true);

        DrawScoreboard(sb, s);

        // the round's name while it gets ready
        if (TroveRun.ReadyUntil > Now && !BattleMode.RoundOver && TroveRun.Floor is { } fl)
            TroveHud.Text(sb, $"ROUND {BattleMode.Round} of {BattleMode.Rounds}:  {fl.Name.ToUpperInvariant()}",
                new Vector2(WindowUtils.WindowWidth / 2f, WindowUtils.WindowHeight * 0.32f), 0.9f * s, new Color(255, 230, 150), border: true);

        if (BattleMode.RoundOver)
            DrawStandings(sb, s);
    }

    private static void DrawFlag(SpriteBatch sb, Vector2 foot, float s, Color cloth) {
        float wave = MathF.Sin((float)Now * 6f) * 3 * s;
        ToggleSwitch.DrawLine(sb, foot + new Vector2(2 * s, 2 * s), foot + new Vector2(2 * s, -40 * s), 4 * s, Color.Black * 0.4f);
        ToggleSwitch.DrawLine(sb, foot, foot - new Vector2(0, 42 * s), 4 * s, new Color(90, 70, 50));
        var top = foot - new Vector2(0, 42 * s);
        for (int i = 0; i < 6; i++) {
            float u = i / 5f;
            ToggleSwitch.DrawLine(sb, top + new Vector2(u * 26 * s, MathF.Sin(u * 3 + (float)Now * 6f) * 2 * s),
                top + new Vector2(u * 26 * s, 16 * s + wave * u), 5 * s, cloth);
        }
    }

    /// <summary>Across the top: each tank's colour, name and round wins, and the round number.</summary>
    private static void DrawScoreboard(SpriteBatch sb, float s) {
        var fighters = BattleMode.Fighters;
        float slot = 300 * s, h = 60 * s;
        float x0 = WindowUtils.WindowWidth / 2f - slot * fighters.Count / 2f;
        float y = 16 * s;
        TroveHud.Text(sb, $"Round {BattleMode.Round}/{BattleMode.Rounds}  -  {BattleMode.MatchName}", new Vector2(WindowUtils.WindowWidth / 2f, y + h + 22 * s), 0.45f * s, Color.White, border: true);
        int top = fighters.Max(f => f.Wins);
        for (int i = 0; i < fighters.Count; i++) {
            var f = fighters[i];
            var box = new Rectangle((int)(x0 + i * slot + 6 * s), (int)y, (int)(slot - 12 * s), (int)h);
            ToggleSwitch.DrawRoundedRect(sb, box, 16 * s, new Color(20, 16, 12) * (f.Alive ? 0.7f : 0.45f));
            ToggleSwitch.DrawDisc(sb, new Vector2(box.X + 28 * s, box.Center.Y), 16 * s, f.Alive ? f.Color : f.Color * 0.35f);
            if (f.Wins == top && top > 0)
                TroveHud.Text(sb, "1st", new Vector2(box.X + 28 * s, box.Center.Y), 0.32f * s, Color.White, border: true);
            var font = FontGlobals.RebirthFont;
            string name = f.Name.Length > 11 ? f.Name[..10] + "." : f.Name;
            sb.DrawString(font, name, new Vector2(box.X + 54 * s, box.Center.Y), (f.Alive ? Color.White : Color.Gray), new Vector2(0.5f * s), 0f,
                new Vector2(0, font.MeasureString("A").Y / 2f));
            TroveHud.Text(sb, f.Wins.ToString(), new Vector2(box.Right - 26 * s, box.Center.Y), 0.75f * s, new Color(255, 215, 80), border: true);
            // king of the hill: how long they've held it
            if (BattleMode.Match == BattleMatch.KingOfTheHill && f.Hill > 0) {
                float frac = Math.Clamp(f.Hill / BattleMode.HILL_SECONDS, 0, 1);
                var bar = new Rectangle(box.X + (int)(10 * s), box.Bottom - (int)(9 * s), (int)((box.Width - 20 * s) * frac), (int)(6 * s));
                ToggleSwitch.DrawRoundedRect(sb, bar, 3 * s, f.Color);
            }
            if (BattleMode.Match == BattleMatch.CaptureTheFlag && BattleMode.FlagCarrier == i)
                DrawFlag(sb, new Vector2(box.Right - 60 * s, box.Bottom - 8 * s), s * 0.6f, f.Color);
        }
    }

    /// <summary>Between rounds: who won it, and the standings so far.</summary>
    private static void DrawStandings(SpriteBatch sb, float s) {
        double age = Now - BattleMode.RoundEndedAt;
        if (age < 0.5)
            return;
        float a = (float)Math.Clamp((age - 0.5) * 4, 0, 1);
        var fighters = BattleMode.Fighters;
        int w = WindowUtils.WindowWidth, h = WindowUtils.WindowHeight;
        sb.Draw(TextureGlobals.Pixels[Color.White], new Rectangle(0, 0, w, h), Color.Black * (0.45f * a));
        float rowH = 78 * s;
        float ph = 250 * s + rowH * fighters.Count;
        var panel = new Rectangle((int)(w / 2f - 500 * s), (int)(h / 2f - ph / 2f), (int)(1000 * s), (int)ph);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(panel.X, panel.Y + (int)(10 * s), panel.Width, panel.Height), 34 * s, UITextButton.PhoneEdge * a);
        ToggleSwitch.DrawRoundedRect(sb, panel, 34 * s, UITextButton.PhoneFill * a);
        var ink = new Color(40, 30, 22) * a;
        string head = BattleMode.LastWinner >= 0 ? $"{fighters[BattleMode.LastWinner].Name} wins round {BattleMode.Round}!" : $"Round {BattleMode.Round}: a draw!";
        TroveHud.Text(sb, head, new Vector2(panel.Center.X, panel.Y + 60 * s), 1.0f * s,
            (BattleMode.LastWinner >= 0 ? fighters[BattleMode.LastWinner].Color : new Color(120, 100, 80)) * a, border: true);
        DrawRows(sb, s, panel, panel.Y + 125 * s, rowH, a);
        TroveHud.Text(sb, BattleMode.LeaderLine(false), new Vector2(panel.Center.X, panel.Bottom - 70 * s), 0.8f * s, new Color(170, 110, 20) * a);
        string next = BattleMode.Round >= BattleMode.Rounds ? "Final results next..." : $"Round {BattleMode.Round + 1} of {BattleMode.Rounds} next...";
        TroveHud.Text(sb, next, new Vector2(panel.Center.X, panel.Bottom - 30 * s), 0.5f * s, ink);
    }

    private static void DrawRows(SpriteBatch sb, float s, Rectangle panel, float y, float rowH, float a) {
        var font = FontGlobals.RebirthFont;
        var list = BattleMode.Standings();
        int place = 0, lastWins = int.MaxValue;
        for (int i = 0; i < list.Count; i++) {
            var f = list[i];
            if (f.Wins < lastWins) { place = i + 1; lastWins = f.Wins; }
            var row = new Rectangle(panel.X + (int)(60 * s), (int)y, panel.Width - (int)(120 * s), (int)(rowH - 10 * s));
            ToggleSwitch.DrawRoundedRect(sb, row, 18 * s, (place == 1 && f.Wins > 0 ? new Color(255, 226, 140) : new Color(250, 244, 230)) * a);
            string ord = place switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => "4th" };
            TroveHud.Text(sb, ord, new Vector2(row.X + 55 * s, row.Center.Y), 0.6f * s, new Color(40, 30, 22) * a);
            ToggleSwitch.DrawDisc(sb, new Vector2(row.X + 125 * s, row.Center.Y), 18 * s, f.Color * a);
            sb.DrawString(font, f.Name, new Vector2(row.X + 160 * s, row.Center.Y), new Color(40, 30, 22) * a, new Vector2(0.6f * s), 0f, new Vector2(0, font.MeasureString("A").Y / 2f));
            // a star per round won
            for (int k = 0; k < f.Wins; k++)
                DrawStar(sb, new Vector2(row.Right - 40 * s - k * 36 * s, row.Center.Y), 14 * s, new Color(230, 170, 30) * a);
            if (f.Wins == 0)
                TroveHud.Text(sb, "0", new Vector2(row.Right - 40 * s, row.Center.Y), 0.6f * s, new Color(120, 100, 80) * a);
            y += rowH;
        }
    }

    private static void DrawStar(SpriteBatch sb, Vector2 c, float r, Color col) {
        for (int i = 0; i < 5; i++) {
            float ang = -MathHelper.PiOver2 + i * MathHelper.TwoPi / 5;
            ToggleSwitch.DrawLine(sb, c, c + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * r, r * 0.55f, col);
        }
        ToggleSwitch.DrawDisc(sb, c, r * 0.45f, col);
    }

    /// <summary>The end of the match: the final standings (drawn in Tank Troves' end-screen panel).</summary>
    public static void DrawFinal(SpriteBatch sb, Rectangle panel) {
        float s = S;
        var fighters = BattleMode.Fighters;
        TroveHud.Text(sb, "Battle Over", new Vector2(panel.Center.X, panel.Y + 62 * s), 1.4f * s, new Color(190, 130, 20));
        TroveHud.Text(sb, BattleMode.LeaderLine(true), new Vector2(panel.Center.X, panel.Y + 130 * s), 0.85f * s, new Color(40, 30, 22));
        float rowH = Math.Min(70 * s, 300 * s / Math.Max(1, fighters.Count));
        DrawRows(sb, s, panel, panel.Y + 170 * s, rowH, 1f);
    }

    // ------------------------------------------------------------------ the options screen (from the campaign list)

    public static bool SetupOpen { get; private set; }
    private static readonly List<UIElement> _hidden = [];
    private static Vector2 _press = new(-1);
    private static bool _down, _wasDown;

    public static void OpenSetup() {
        // automated testing: "battle_test.txt" in the save folder holds a match type, rounds and CPU tanks
        try {
            var tp = System.IO.Path.Combine(TankGame.SaveDirectory, "battle_test.txt");
            if (System.IO.File.Exists(tp)) {
                var parts = System.IO.File.ReadAllText(tp).Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && Enum.TryParse<BattleMatch>(parts[0], out var m)) BattleMode.Match = m;
                if (parts.Length > 1 && int.TryParse(parts[1], out var r)) BattleMode.Rounds = Math.Clamp(r, 1, 15);
                if (parts.Length > 2 && int.TryParse(parts[2], out var c)) BattleMode.Cpus = Math.Clamp(c, 0, 3);
            }
        }
        catch { }
        SetupOpen = true;
        _down = _wasDown = true; // (the tap that opened it doesn't count)
        HideMenu();
    }

    private static void CloseSetup(bool restore) {
        SetupOpen = false;
        if (restore)
            foreach (var e in _hidden)
                e.IsVisible = true;
        _hidden.Clear();
    }

    private static void HideMenu() {
        foreach (var e in UIElement.AllUIElements)
            if (e is not null && e.IsVisible) {
                e.IsVisible = false;
                _hidden.Add(e);
            }
    }

    /// <summary>Every update, before the menu's buttons take taps: keep them out of the way while this screen is up.</summary>
    public static void UpdateSetup() {
        if (!SetupOpen)
            return;
        if (!MainMenu.MainMenuUI.IsActive || MainMenu.MainMenuUI.MenuState != MainMenu.MainMenuUI.UIState.Campaigns) {
            CloseSetup(restore: false);
            return;
        }
        HideMenu();
    }

    private static bool Tapped(Rectangle r) {
        bool hit = !_down && _wasDown && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        if (hit)
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);
        return hit;
    }

    public static void DrawSetup(SpriteBatch sb) {
        if (!SetupOpen || !MainMenu.MainMenuUI.IsActive)
            return;
        _wasDown = _down;
        _down = InputUtils.CurrentMouseSnapshot.LeftButton == ButtonState.Pressed;
        if (_down && !_wasDown)
            _press = MouseUtils.MousePosition;

        float s = S;
        int w = WindowUtils.WindowWidth, h = WindowUtils.WindowHeight;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        sb.Draw(TextureGlobals.Pixels[Color.White], new Rectangle(0, 0, w, h), Color.Black * 0.55f);
        var panel = new Rectangle((int)(w / 2f - 640 * s), (int)(h / 2f - 470 * s), (int)(1280 * s), (int)(940 * s));
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(panel.X, panel.Y + (int)(10 * s), panel.Width, panel.Height), 36 * s, UITextButton.PhoneEdge);
        ToggleSwitch.DrawRoundedRect(sb, panel, 36 * s, UITextButton.PhoneFill);
        var ink = new Color(40, 30, 22);
        TroveHud.Text(sb, "Battle", new Vector2(panel.Center.X, panel.Y + 70 * s), 1.7f * s, new Color(170, 50, 40));

        int humans = BattleMode.HumanCount;
        string who = Client.IsConnected() ? $"{humans} player{(humans == 1 ? "" : "s")} connected"
            : LocalCoop.Active ? "2 players on this device" : "1 player on this device";
        int total = humans + BattleMode.CpuCount;
        TroveHud.Text(sb, $"{who}  -  {total} tanks", new Vector2(panel.Center.X, panel.Y + 140 * s), 0.6f * s, UITextButton.PhoneEdge);

        bool offline = !Client.IsConnected();
        var rows = new List<(string Label, string Value, Action Prev, Action Next)> {
            ("Match", BattleMode.MatchName, () => BattleMode.Match = (BattleMatch)(((int)BattleMode.Match + 2) % 3), () => BattleMode.Match = (BattleMatch)(((int)BattleMode.Match + 1) % 3)),
            ("Rounds", BattleMode.Rounds.ToString(), () => BattleMode.Rounds = BattleMode.Rounds <= 1 ? 15 : BattleMode.Rounds - 1, () => BattleMode.Rounds = BattleMode.Rounds >= 15 ? 1 : BattleMode.Rounds + 1),
            ("Hit Points", BattleMode.HitPoints.ToString(), () => BattleMode.HitPoints = BattleMode.HitPoints <= 1 ? 5 : BattleMode.HitPoints - 1, () => BattleMode.HitPoints = BattleMode.HitPoints >= 5 ? 1 : BattleMode.HitPoints + 1),
            ("Items", BattleMode.Items ? "On" : "Off", () => BattleMode.Items = !BattleMode.Items, () => BattleMode.Items = !BattleMode.Items),
        };
        if (offline) {
            int max = 4 - humans, min = humans < 2 ? 1 : 0;
            rows.Add(("CPU Tanks", BattleMode.CpuCount.ToString(),
                () => BattleMode.Cpus = BattleMode.CpuCount <= min ? max : BattleMode.CpuCount - 1,
                () => BattleMode.Cpus = BattleMode.CpuCount >= max ? min : BattleMode.CpuCount + 1));
        }
        float rowY = panel.Y + 215 * s, rowH = 100 * s;
        foreach (var (label, value, prev, next) in rows) {
            var font = FontGlobals.RebirthFont;
            sb.DrawString(font, label, new Vector2(panel.X + 90 * s, rowY + rowH / 2f), ink, new Vector2(0.8f * s), 0f, new Vector2(0, font.MeasureString("A").Y / 2f));
            var left = new Rectangle((int)(panel.X + 560 * s), (int)(rowY + 10 * s), (int)(110 * s), (int)(rowH - 20 * s));
            var right = new Rectangle((int)(panel.Right - 200 * s), (int)(rowY + 10 * s), (int)(110 * s), (int)(rowH - 20 * s));
            TroveHud.Button(sb, left, "<", Color.White, _down, _press);
            TroveHud.Button(sb, right, ">", Color.White, _down, _press);
            TroveHud.Text(sb, value, new Vector2((left.Right + right.X) / 2f, rowY + rowH / 2f), 0.85f * s, new Color(170, 50, 40));
            if (Tapped(left)) prev();
            if (Tapped(right)) next();
            rowY += rowH;
        }
        string tip = BattleMode.Match switch {
            BattleMatch.CaptureTheFlag => "Grab the flag from the middle and bring it to your corner",
            BattleMatch.KingOfTheHill => $"Stay alone on the hill in the middle for {(int)BattleMode.HILL_SECONDS} seconds",
            _ => "Be the last tank standing",
        };
        TroveHud.Text(sb, tip + (BattleMode.Match == BattleMatch.Deathmatch ? "" : " (or be the last tank standing)"),
            new Vector2(panel.Center.X, rowY + 30 * s), 0.5f * s, UITextButton.PhoneEdge);

        float bw = 380 * s, bh = 120 * s, by = panel.Bottom - 60 * s - bh;
        var back = new Rectangle((int)(panel.Center.X - bw - 30 * s), (int)by, (int)bw, (int)bh);
        var start = new Rectangle((int)(panel.Center.X + 30 * s), (int)by, (int)bw, (int)bh);
        bool canStart = !Client.IsConnected() || Client.IsHost();
        TroveHud.Button(sb, back, "Back", Color.White, _down, _press);
        TroveHud.Button(sb, start, canStart ? "Start" : "Host starts", canStart ? new Color(255, 226, 140) : new Color(200, 200, 200), _down, _press);
        bool goBack = Tapped(back) || InputUtils.KeyJustPressed(Keys.Escape);
        bool go = canStart && Tapped(start);
        sb.End();
        if (goBack) {
            CloseSetup(restore: true);
            return;
        }
        if (go) {
            CloseSetup(restore: true);
            BattleMode.StartFromMenu();
        }
    }
}
