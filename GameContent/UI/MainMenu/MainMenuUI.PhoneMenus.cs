using System;
using System.Collections.Generic;
using System.Linq;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Globals.Assets;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.Graphics;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.UI.MainMenu;

/// <summary>
/// Phones: Tank Color picker and the multiplayer screens (same device, Wi-Fi host / join, lobby).
/// These are drawn directly (not with UIElements) in the same rounded style as the other phone buttons.
/// </summary>
public static partial class MainMenuUI {
    private static bool IsPhoneScreen => MenuState is UIState.TankColors or UIState.MPMode or UIState.MPLocal
        or UIState.MPJoin or UIState.MPLobby or UIState.MPKeypad or UIState.MPOnline or UIState.MPOnlineJoining
        or UIState.StatsMenu;

    /// <summary>Where Back from the Difficulties screen goes.</summary>
    private static UIState _difficultiesReturn = UIState.Campaigns;

    // ------------------------------------------------------------------ immediate-mode buttons

    private static Vector2 _pmPress = new(-1);
    private static bool _pmDown, _pmWasDown, _pmEnteredThisFrame;
    private static readonly Color Ink = new(40, 30, 22);
    private static readonly Color PanelFill = UITextButton.PhoneFill;

    private static void PhoneInputFrame() {
        _pmWasDown = _pmDown;
        _pmDown = InputUtils.CurrentMouseSnapshot.LeftButton == ButtonState.Pressed;
        if (_pmDown && !_pmWasDown)
            _pmPress = MouseUtils.MousePosition;
    }

    /// <summary>A rounded button; true on the frame a tap that started on it is released on it.</summary>
    private static bool PButton(SpriteBatch sb, Rectangle r, string label, bool enabled = true, Color? fill = null, bool selected = false) {
        float s = PhoneScale;
        var mouse = MouseUtils.MousePosition;
        bool pressing = enabled && _pmDown && r.Contains(_pmPress) && r.Contains(mouse);
        int lip = Math.Max(3, (int)(r.Height * 0.07f));
        float radius = Math.Min(r.Height * 0.32f, 40 * s);
        var face = pressing ? new Rectangle(r.X, r.Y + lip / 2, r.Width, r.Height - lip / 2) : new Rectangle(r.X, r.Y, r.Width, r.Height - lip);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X, r.Y + lip, r.Width, r.Height - lip), radius, enabled ? UITextButton.PhoneEdge : new Color(150, 140, 128));
        var col = !enabled ? new Color(205, 200, 192) : pressing ? UITextButton.PhonePressed : selected ? new Color(255, 226, 140) : fill ?? PanelFill;
        ToggleSwitch.DrawRoundedRect(sb, face, radius, col);
        var font = FontGlobals.RebirthFont;
        var size = font.MeasureString(label);
        float scale = Math.Min(Math.Min(face.Height * 0.40f, 46f * s) / Math.Max(1f, size.Y), (face.Width - face.Height * 0.5f) / Math.Max(1f, size.X));
        sb.DrawString(font, label, face.Center.ToVector2(), enabled ? Ink : new Color(120, 112, 104), new Vector2(scale), 0f, size / 2f);

        bool tapped = enabled && !_pmDown && _pmWasDown && r.Contains(_pmPress) && r.Contains(mouse) && !_pmEnteredThisFrame;
        if (tapped) {
            SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
            Internals.Common.Framework.Input.Haptics.Tap();
        }
        return tapped;
    }

    /// <summary>How much bigger the phone screens' text is than the desktop's.</summary>
    private const float PTextK = 1.6f;
    private static void PText(SpriteBatch sb, string text, Vector2 pos, float scale, Color color, bool center = true, float maxWidth = 0) {
        var font = FontGlobals.RebirthFont;
        var m = font.MeasureString(text);
        // a bit larger than the desktop text for phones, but always fitting inside the right-hand panel
        float sc = scale * PTextK * PhoneScale;
        float maxW = maxWidth > 0 ? maxWidth : RightPanel().Width * 0.92f;
        if (m.X * sc > maxW)
            sc = maxW / Math.Max(1f, m.X);
        sb.DrawString(font, text, pos, color, new Vector2(sc), 0f, center ? m / 2f : new Vector2(0, m.Y / 2f));
    }

    /// <summary>Text wrapped to a width, centered on <paramref name="x"/>, starting at <paramref name="y"/>; returns the y after it.</summary>
    private static float PWrap(SpriteBatch sb, string text, float x, float y, float scale, Color color, float maxWidth, bool center = true) {
        var font = FontGlobals.RebirthFont;
        float sc = scale * PTextK * PhoneScale;
        float lineH = font.MeasureString("Ag").Y * sc * 1.12f;
        var line = "";
        foreach (var word in text.Split(' ')) {
            var test = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureString(test).X * sc > maxWidth) {
                PText(sb, line, new Vector2(x, y + lineH / 2f), scale, color, center, maxWidth);
                y += lineH;
                line = word;
            }
            else
                line = test;
        }
        if (line.Length > 0) {
            PText(sb, line, new Vector2(x, y + lineH / 2f), scale, color, center, maxWidth);
            y += lineH;
        }
        return y;
    }

    /// <summary>The panel to the right of the button column.</summary>
    private static Rectangle RightPanel(float top = 110, float bottomPad = 56) {
        float s = PhoneScale;
        float left = PhoneColumnRight + 40 * s;
        return new Rectangle((int)left, (int)(top * s), (int)(WindowUtils.WindowWidth - left - 56 * s), (int)(WindowUtils.WindowHeight - (top + bottomPad) * s));
    }

    private static void DrawPanel(SpriteBatch sb, Rectangle r) {
        float s = PhoneScale;
        int lip = (int)(10 * s);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X, r.Y + lip, r.Width, r.Height), 36 * s, UITextButton.PhoneEdge);
        ToggleSwitch.DrawRoundedRect(sb, r, 36 * s, PanelFill);
    }

    private static void GoToState(UIState state) {
        _pmEnteredThisFrame = true; // the tap that opened a screen must not also press something on it
        if (state == UIState.PlayList)
            GameUI.BackButton.IsVisible = true;
        MenuState = state;
    }

    /// <summary>The Single Player (campaign) screen, also used by both kinds of multiplayer.</summary>
    internal static void EnterCampaigns() {
        _pmEnteredThisFrame = true;
        SetCampaignDisplay();
        GameUI.BackButton.IsVisible = true;
        MenuState = UIState.Campaigns;
    }

    // ------------------------------------------------------------------ flow

    public static void PhonePlayMultiplayer() {
        foreach (var e in campaignNames) e?.Remove();
        campaignNames.Clear();
        GoToState(Client.IsConnected() ? UIState.MPLobby : UIState.MPMode);
    }

    public static void PhoneOpenTankColors() => GoToState(UIState.TankColors);

    public static void PhoneOpenDifficulties() {
        _difficultiesReturn = MenuState;
        foreach (var e in campaignNames) e?.Remove();
        campaignNames.Clear();
        ArrangeDifficultyButtons();
        GameUI.BackButton.IsVisible = true;
        MenuState = UIState.Difficulties;
    }

    /// <summary>Phones: Back (button or gesture) for the screens that don't use the desktop logic. True if handled.</summary>
    public static bool PhoneHandleBack() {
        if (!Phone || !IsActive)
            return false;
        _suppressEscape = true; // this back press is used up; the screen it lands on must not see it too
        switch (MenuState) {
            case UIState.Difficulties:
                if (_difficultiesReturn == UIState.Campaigns)
                    EnterCampaigns();
                else
                    MenuState = _difficultiesReturn;
                return true;
            case UIState.Campaigns when LocalCoop.Active:
                foreach (var e in campaignNames) e?.Remove();
                campaignNames.Clear();
                MenuState = UIState.MPLocal;
                return true;
            case UIState.Campaigns when Client.IsConnected():
                foreach (var e in campaignNames) e?.Remove();
                campaignNames.Clear();
                MenuState = UIState.MPLobby;
                return true;
        }
        return false;
    }

    private static void PhoneScreenBack() {
        switch (MenuState) {
            case UIState.TankColors:
            case UIState.MPMode:
                GoToState(UIState.PlayList);
                break;
            case UIState.MPLocal:
                LocalCoop.Stop();
                GoToState(UIState.MPMode);
                break;
            case UIState.MPJoin:
                LanSession.Leave();
                Net.Online.OnlineBrowser.Stop();
                GoToState(UIState.MPMode);
                break;
            case UIState.MPKeypad:
                GoToState(UIState.MPJoin);
                break;
            case UIState.MPOnline:
                GoToState(UIState.MPMode);
                break;
            case UIState.StatsMenu:
                GoToState(UIState.PrimaryMenu);
                break;
            case UIState.MPOnlineJoining:
                Net.Online.OnlineSession.Leave();
                OpenJoin();
                break;
            case UIState.MPLobby:
                if (Net.Online.OnlineSession.Active) {
                    Net.Online.OnlineSession.Leave();
                    GoToState(UIState.MPMode);
                }
                else {
                    LanSession.Leave();
                    GoToState(UIState.MPMode);
                }
                break;
        }
    }

    /// <summary>Called from Update for the phone-only screens.</summary>
    private static void UpdatePhoneScreens() {
        GameUI.BackButton.IsVisible = false;
        LanSession.Update();
        // the lobby follows the connection: drop back if it's lost
        var online = Net.Online.OnlineSession.Active;
        if (MenuState == UIState.MPLobby && online && Net.Online.OnlineSession.IsHost && Net.Online.OnlineSession.Error is not null) {
            // hosting couldn't start: show why on the Online screen
            ChatSystem.SendMessage(Net.Online.OnlineSession.Error, Color.Red);
            Net.Online.OnlineSession.Leave();
            MenuState = UIState.MPMode;
        }
        else if (MenuState == UIState.MPLobby && !Client.IsConnected() && _lobbyGrace <= 0 && !(online && Net.Online.OnlineSession.Busy)) {
            ChatSystem.SendMessage(online ? "Disconnected from the online game." : "Disconnected from the Wi-Fi game.", Color.Orange);
            if (online) {
                Net.Online.OnlineSession.Leave();
                MenuState = UIState.MPMode;
            }
            else {
                LanSession.Leave();
                MenuState = UIState.MPMode;
            }
        }
        if (MenuState == UIState.MPOnlineJoining && Client.IsConnected() && !Net.Online.OnlineSession.Busy)
            MenuState = UIState.MPLobby;
        if (_lobbyGrace > 0)
            _lobbyGrace--;
        if (MenuState == UIState.MPJoin && Client.IsConnected()) {
            LanSession.StopSearch();
            Net.Online.OnlineBrowser.Stop();
            MenuState = UIState.MPLobby;
        }
        // host: whenever someone joins, send them the current difficulty choices
        if (MenuState == UIState.MPLobby && Client.IsConnected() && Client.IsHost() && Server.CurrentClientCount != _lastLobbyCount) {
            _lastLobbyCount = Server.CurrentClientCount;
            if (_lastLobbyCount > 1)
                Client.SendDiffiulties();
        }
        if (InputUtils.KeyJustPressed(Keys.Escape) && !_suppressEscape)
            PhoneScreenBack();
        _suppressEscape = false;
    }
    private static bool _suppressEscape;
    private static int _lastLobbyCount;

    private static int _lobbyGrace;

    // ------------------------------------------------------------------ drawing (and the taps)

    public static void DrawPhoneScreens(SpriteBatch sb) {
        PhoneInputFrame();
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        var state = MenuState;
        switch (state) {
            case UIState.TankColors: DrawTankColors(sb); break;
            case UIState.MPMode: DrawMPMode(sb); break;
            case UIState.MPLocal: DrawMPLocal(sb); break;
            case UIState.MPJoin: DrawMPJoin(sb); break;
            case UIState.MPKeypad: DrawKeypad(sb); break;
            case UIState.MPLobby: DrawLobby(sb); break;
            case UIState.MPOnline: DrawMPOnline(sb); break;
            case UIState.StatsMenu: DrawStatsPhone(sb); break;
            case UIState.MPOnlineJoining: DrawMPOnlineJoining(sb); break;
        }
        sb.End();
        sb.Begin();
        if (!_pmDown)
            _pmEnteredThisFrame = false;
    }

    // ---- multiplayer: choose a mode
    private static void DrawMPMode(SpriteBatch sb) {
        int n = 4;
        var panel = RightPanel();
        DrawPanel(sb, panel);
        float s = PhoneScale;
        var cx = panel.Center.X;
        PText(sb, "Multiplayer", new Vector2(cx, panel.Y + 80 * s), 1.6f, Ink);
        var sub = UITextButton.PhoneEdge;
        float wrapW = panel.Width - 140 * s;
        float step = (panel.Height - 170 * s) / 3f;
        (string Head, string Body)[] modes = [
            ("Same Device", "Two players on this phone: touch and a controller, or two controllers."),
            ("Host", "Start a game. Phones on your Wi-Fi and players online can join."),
            ("Join", "Pick a game from the list. On the same Wi-Fi it connects directly, which is faster."),
        ];
        for (int i = 0; i < modes.Length; i++) {
            float top = panel.Y + 170 * s + i * step;
            PText(sb, modes[i].Head, new Vector2(cx, top + 26 * s), 1.4f, Ink);
            PWrap(sb, modes[i].Body, cx, top + 62 * s, 1.05f, sub, wrapW);
        }

        if (PButton(sb, PhoneColumnSlot(0, n), "Same Device"))
            GoToState(UIState.MPLocal);
        if (PButton(sb, PhoneColumnSlot(1, n), "Host")) {
            if (Net.Online.OnlineSession.Configured) {
                Net.Online.OnlineSession.Host();
                _lobbyGrace = 240; // give the local connection a moment
                GoToState(UIState.MPLobby);
            }
            else if (LanSession.Host()) {
                _lobbyGrace = 180;
                GoToState(UIState.MPLobby);
            }
            else {
                ChatSystem.SendMessage("Couldn't start hosting. Is Wi-Fi on?", Color.Red);
                SoundPlayer.SoundError();
            }
        }
        if (PButton(sb, PhoneColumnSlot(2, n), "Join"))
            OpenJoin();
        if (PButton(sb, PhoneColumnSlot(3, n), "Back"))
            PhoneScreenBack();
    }

    private static void OpenJoin() {
        LanSession.StartSearch();
        Net.Online.OnlineBrowser.Start();
        GoToState(UIState.MPJoin);
    }

    // ---- online: host or join with a code
    private static void DrawMPOnline(SpriteBatch sb) {
        int n = 3;
        var panel = RightPanel();
        DrawPanel(sb, panel);
        float s = PhoneScale;
        var cx = panel.Center.X;
        bool ready = Net.Online.OnlineSession.Configured;
        PText(sb, "Online", new Vector2(cx, panel.Y + 80 * s), 1.6f, Ink);
        if (!ready) {
            PText(sb, "Online play isn't set up in this build yet.", new Vector2(cx, panel.Y + 230 * s), 0.9f, new Color(170, 60, 40));
        }
        else {
            PText(sb, "Host Online", new Vector2(cx, panel.Y + 200 * s), 1.05f, Ink);
            PText(sb, "You get a 6-digit code to send to friends.", new Vector2(cx, panel.Y + 255 * s), 0.9f, UITextButton.PhoneEdge);
            PText(sb, "Join with Code", new Vector2(cx, panel.Y + 360 * s), 1.05f, Ink);
            PText(sb, "Type the code from the host's screen.", new Vector2(cx, panel.Y + 415 * s), 0.9f, UITextButton.PhoneEdge);
            PText(sb, "Campaign, Level Select and modifiers", new Vector2(cx, panel.Bottom - 110 * s), 0.8f, UITextButton.PhoneEdge);
            PText(sb, "work the same as on Wi-Fi.", new Vector2(cx, panel.Bottom - 65 * s), 0.8f, UITextButton.PhoneEdge);
        }
        if (PButton(sb, PhoneColumnSlot(0, n), "Host Online", enabled: ready)) {
            Net.Online.OnlineSession.Host();
            _lobbyGrace = 60;
            GoToState(UIState.MPLobby);
        }
        if (PButton(sb, PhoneColumnSlot(1, n), "Join with Code", enabled: ready)) {
            _keypadText = "";
            _keypadForCode = true;
            GoToState(UIState.MPKeypad);
        }
        if (PButton(sb, PhoneColumnSlot(2, n), "Back"))
            PhoneScreenBack();
    }

    // ---- online: joining (status while connecting)
    private static void DrawMPOnlineJoining(SpriteBatch sb) {
        int n = 1;
        var panel = RightPanel();
        DrawPanel(sb, panel);
        float s = PhoneScale;
        var cx = panel.Center.X;
        var os = Net.Online.OnlineSession.Error is null ? Net.Online.OnlineSession.Status : Net.Online.OnlineSession.Error;
        PText(sb, $"Code {Net.Online.OnlineSession.Code}", new Vector2(cx, panel.Y + 80 * s), 1.6f, Ink);
        int dots = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 2) % 4;
        PText(sb, os + (Net.Online.OnlineSession.Busy ? new string('.', dots) : ""), new Vector2(cx, panel.Y + 260 * s), 1.0f,
            Net.Online.OnlineSession.Error is null ? Ink : new Color(170, 60, 40));
        if (PButton(sb, PhoneColumnSlot(0, n), Net.Online.OnlineSession.Error is null ? "Cancel" : "Back"))
            PhoneScreenBack();
    }

    // ---- same device
    private static void DrawMPLocal(SpriteBatch sb) {
        int n = 4;
        var panel = RightPanel();
        DrawPanel(sb, panel);
        float s = PhoneScale;
        var cx = panel.Center.X;
        int pads = LocalCoop.ConnectedPads;
        float wrapW = panel.Width - 140 * s;
        PText(sb, "Same Device", new Vector2(cx, panel.Y + 80 * s), 1.6f, Ink);
        PText(sb, $"Controllers connected: {pads}", new Vector2(cx, panel.Y + 170 * s), 1.15f, pads > 0 ? new Color(30, 130, 60) : new Color(170, 60, 40));
        float ty = panel.Y + 240 * s;
        ty = PWrap(sb, "Touch + Controller: player 1 uses the touch controls, player 2 uses the controller.", cx, ty, 1.0f, Ink, wrapW) + 22 * s;
        ty = PWrap(sb, "Two Controllers: no touch controls, one controller each.", cx, ty, 1.0f, Ink, wrapW) + 40 * s;
        ty = PWrap(sb, "Controller: left stick drives, right stick aims, RB shoots, LB lays a mine, Start pauses.", cx, ty, 0.95f, UITextButton.PhoneEdge, wrapW);
        PWrap(sb, "Campaign, Level Select and modifiers work the same as in single player.", cx, panel.Bottom - 130 * s, 0.85f, UITextButton.PhoneEdge, wrapW);

        if (PButton(sb, PhoneColumnSlot(0, n), "Touch + Controller", enabled: pads >= 1))
            StartLocalCoop(LocalCoop.Scheme.TouchAndController);
        if (PButton(sb, PhoneColumnSlot(1, n), "Two Controllers", enabled: pads >= 2))
            StartLocalCoop(LocalCoop.Scheme.TwoControllers);
        if (PButton(sb, PhoneColumnSlot(2, n), "Difficulties"))
            PhoneOpenDifficulties();
        if (PButton(sb, PhoneColumnSlot(3, n), "Back"))
            PhoneScreenBack();
    }

    private static void StartLocalCoop(LocalCoop.Scheme scheme) {
        LocalCoop.Start(scheme);
        EnterCampaigns();
    }

    // ---- join: games on this Wi-Fi and online games, in one list
    private static void DrawMPJoin(SpriteBatch sb) {
        float s = PhoneScale;
        int n = 2;
        if (PButton(sb, PhoneColumnSlot(0, n), "Enter Code", enabled: Net.Online.OnlineSession.Configured)) {
            _keypadText = "";
            _keypadForCode = true;
            GoToState(UIState.MPKeypad);
        }
        if (PButton(sb, PhoneColumnSlot(1, n), "Back"))
            PhoneScreenBack();

        // a game on the same Wi-Fi shows once, and is joined directly
        var rows = new List<(string Label, bool Enabled, Action Join)>();
        var lan = LanSession.Found.OrderBy(g => g.Name).ToList();
        foreach (var g in lan)
            rows.Add(($"{g.Name}  ({g.Players}/{g.MaxPlayers})  -  Wi-Fi", g.Players < g.MaxPlayers, () => {
                Net.Online.OnlineBrowser.Stop();
                LanSession.Join(g.EndPoint.Address.ToString(), g.EndPoint.Port);
                ChatSystem.SendMessage($"Joining {g.Name}...", Color.White);
            }));
        foreach (var g in Net.Online.OnlineBrowser.Games) {
            if (g.Lan is { } le && lan.Any(l => l.EndPoint.Address.Equals(le.Address)))
                continue;
            var code = g.Code;
            rows.Add(($"{g.Host}  ({g.Players}/{g.Max})  -  {(g.Playing ? "playing" : "Online")}", !g.Playing && g.Players < g.Max, () => {
                Net.Online.OnlineBrowser.Stop();
                LanSession.Leave();
                Net.Online.OnlineSession.Join(code);
                GoToState(UIState.MPOnlineJoining);
            }));
        }

        var panel = RightPanel();
        DrawPanel(sb, panel);
        var cx = panel.Center.X;
        PText(sb, "Join a Game", new Vector2(cx, panel.Y + 80 * s), 1.6f, Ink);
        if (rows.Count == 0) {
            int dots = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 2) % 4;
            float wrapW = panel.Width - 140 * s;
            PText(sb, "Looking for games" + new string('.', dots), new Vector2(cx, panel.Y + 240 * s), 1.3f, Ink);
            float ty = panel.Y + 320 * s;
            ty = PWrap(sb, "Ask a friend to tap Multiplayer > Host. Games on your Wi-Fi and online games both show up here.", cx, ty, 1.0f, UITextButton.PhoneEdge, wrapW) + 36 * s;
            ty = PWrap(sb, "Have a code? Tap Enter Code.", cx, ty, 1.0f, UITextButton.PhoneEdge, wrapW) + 36 * s;
            if (Net.Online.OnlineBrowser.Failed)
                PWrap(sb, "Online games can't be listed: no internet.", cx, ty, 0.95f, new Color(170, 60, 40), wrapW);
            return;
        }
        for (int i = 0; i < Math.Min(rows.Count, 5); i++) {
            var row = rows[i];
            var r = new Rectangle(panel.X + (int)(40 * s), panel.Y + (int)((140 + i * 118) * s), panel.Width - (int)(80 * s), (int)(100 * s));
            if (PButton(sb, r, row.Label, enabled: row.Enabled))
                row.Join();
        }
    }

    // ---- type an address
    private static string _keypadText = "";
    private static bool _keypadForCode;
    private static void DrawKeypad(SpriteBatch sb) {
        float s = PhoneScale;
        int n = 2;
        bool valid = _keypadForCode ? _keypadText.Length == 6 : System.Net.IPAddress.TryParse(_keypadText, out _);
        if (PButton(sb, PhoneColumnSlot(0, n), "Join", enabled: valid)) {
            if (_keypadForCode) {
                Net.Online.OnlineBrowser.Stop();
                LanSession.Leave();
                Net.Online.OnlineSession.Join(_keypadText);
                GoToState(UIState.MPOnlineJoining);
            }
            else {
                LanSession.Join(_keypadText);
                ChatSystem.SendMessage($"Joining {_keypadText}...", Color.White);
                GoToState(UIState.MPJoin);
            }
        }
        if (PButton(sb, PhoneColumnSlot(1, n), "Back"))
            PhoneScreenBack();

        var panel = RightPanel();
        DrawPanel(sb, panel);
        var cx = panel.Center.X;
        string hint = _keypadForCode ? "Enter the 6-digit code" : "Host address (e.g. 192.168.1.20)";
        PText(sb, _keypadText.Length == 0 ? hint : _keypadText, new Vector2(cx, panel.Y + 80 * s), _keypadText.Length == 0 ? 1.1f : 1.6f, Ink);
        string[] keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", _keypadForCode ? "" : ".", "0", "<"];
        float kw = Math.Min(panel.Width / 3.6f, 260 * s), kh = (panel.Height - 190 * s) / 4.3f;
        float x0 = cx - kw * 1.5f - 10 * s, y0 = panel.Y + 150 * s;
        for (int i = 0; i < keys.Length; i++) {
            var r = new Rectangle((int)(x0 + (i % 3) * (kw + 10 * s)), (int)(y0 + (i / 3) * (kh + 10 * s)), (int)kw, (int)kh);
            if (keys[i].Length == 0)
                continue;
            if (PButton(sb, r, keys[i] == "<" ? "Delete" : keys[i], fill: new Color(240, 233, 218))) {
                if (keys[i] == "<") {
                    if (_keypadText.Length > 0)
                        _keypadText = _keypadText[..^1];
                }
                else if (_keypadText.Length < (_keypadForCode ? 6 : 15))
                    _keypadText += keys[i];
            }
        }
    }

    // ---- lobby (host and joined players)
    private static void DrawLobby(SpriteBatch sb) {
        float s = PhoneScale;
        bool host = Client.IsHost();
        var panel = RightPanel();
        DrawPanel(sb, panel);
        var cx = panel.Center.X;
        PText(sb, NetPlay.ServerName ?? "Wi-Fi game", new Vector2(cx, panel.Y + 70 * s), 1.5f, Ink);
        bool showCode = Net.Online.OnlineSession.Active && host && Net.Online.OnlineSession.Code is not null;
        if (showCode) {
            // the code is what friends need: make it big
            var c = Net.Online.OnlineSession.Code!;
            PText(sb, $"Code  {c[..3]} {c[3..]}", new Vector2(cx, panel.Y + 150 * s), 1.7f, new Color(30, 110, 200));
            PText(sb, "Friends: Multiplayer > Join (your game is in the list)", new Vector2(cx, panel.Y + 218 * s), 0.95f, UITextButton.PhoneEdge);
        }
        else if (Net.Online.OnlineSession.Active) {
            var os = Net.Online.OnlineSession.Code is { } code && host
                ? $"Code: {code}   (friends: Multiplayer > Join)"
                : host ? Net.Online.OnlineSession.Status
                : Net.Online.OnlineSession.Connection == Net.Online.OnlineSession.Link.Relay ? "Online, connected through the relay" : "Online, connected directly";
            PText(sb, os, new Vector2(cx, panel.Y + 135 * s), host && Net.Online.OnlineSession.Code is not null ? 1.1f : 0.95f, host && Net.Online.OnlineSession.Code is not null ? new Color(30, 110, 200) : UITextButton.PhoneEdge);
        }
        else if (host)
            PText(sb, $"Others: Join on Wi-Fi (address {LanSession.LocalAddress()})", new Vector2(cx, panel.Y + 138 * s), 0.95f, UITextButton.PhoneEdge);

        int count = Math.Max(Server.CurrentClientCount, 0);
        for (int i = 0; i < 4; i++) {
            float rowsTop = showCode ? 265 : 195, rowsBottom = panel.Height / s - 110;
            float rowStep = Math.Min(128, (rowsBottom - rowsTop) / 4f), rowH = Math.Min(104, rowStep - 14);
            var row = new Rectangle(panel.X + (int)(60 * s), panel.Y + (int)((rowsTop + i * rowStep) * s), panel.Width - (int)(120 * s), (int)(rowH * s));
            var c = Server.ConnectedClients is not null && i < Server.ConnectedClients.Length ? Server.ConnectedClients[i] : null;
            bool present = c is not null && i < count;
            var col = PlayerID.PlayerTankColors[i];
            ToggleSwitch.DrawRoundedRect(sb, row, 26 * s, present ? Color.Lerp(col, Color.White, 0.75f) : new Color(225, 220, 210));
            ToggleSwitch.DrawDisc(sb, new Vector2(row.X + 52 * s, row.Center.Y), row.Height * 0.3f, present ? col : new Color(190, 184, 176));
            PText(sb, present ? $"P{i + 1}  {c!.Name}" + (i == 0 ? "  (host)" : "") : $"P{i + 1}  waiting for player...", new Vector2(row.X + 104 * s, row.Center.Y), 1.05f, present ? Ink : new Color(140, 130, 120), center: false);
        }

        if (host) {
            int n = 4;
            if (PButton(sb, PhoneColumnSlot(0, n), "Play", enabled: count >= 1)) {
                plrsConfirmed = 0;
                EnterCampaigns();
            }
            if (PButton(sb, PhoneColumnSlot(1, n), "Difficulties"))
                PhoneOpenDifficulties();
            if (PButton(sb, PhoneColumnSlot(2, n), "Tank Color"))
                GoToState(UIState.TankColors);
            if (PButton(sb, PhoneColumnSlot(3, n), "Leave"))
                PhoneScreenBack();
            if (count < 2)
                PText(sb, "Waiting for others to join...", new Vector2(cx, panel.Bottom - 58 * s), 0.95f, UITextButton.PhoneEdge);
        }
        else {
            // joined players see the host's difficulty choices (only the host can change them)
            int n = 3;
            if (PButton(sb, PhoneColumnSlot(0, n), "Difficulties"))
                PhoneOpenDifficulties();
            if (PButton(sb, PhoneColumnSlot(1, n), "Tank Color"))
                GoToState(UIState.TankColors);
            if (PButton(sb, PhoneColumnSlot(2, n), "Leave"))
                PhoneScreenBack();
            PText(sb, "Waiting for the host to start...", new Vector2(cx, panel.Bottom - 58 * s), 1.0f, Ink);
        }
    }

    // ---- game stats and achievements
    private static bool _statsAchTab;
    private static int _achSelected;

    private static void DrawStatsPhone(SpriteBatch sb) {
        float s = PhoneScale;
        int n = 3;
        if (PButton(sb, PhoneColumnSlot(0, n), "Stats", selected: !_statsAchTab))
            _statsAchTab = false;
        if (PButton(sb, PhoneColumnSlot(1, n), "Achievements", selected: _statsAchTab))
            _statsAchTab = true;
        if (PButton(sb, PhoneColumnSlot(2, n), "Back")) {
            PhoneScreenBack();
            return;
        }
        var panel = RightPanel(70, 40);
        DrawPanel(sb, panel);
        if (_statsAchTab)
            DrawAchievementsPanel(sb, panel);
        else
            DrawStatsPanel(sb, panel);
    }

    private static void DrawStatsPanel(SpriteBatch sb, Rectangle panel) {
        float s = PhoneScale;
        RequestStats();
        var font = FontGlobals.RebirthFont;
        PText(sb, "Game Stats", new Vector2(panel.Center.X, panel.Y + 62 * s), 1.6f, Ink);
        float leftX = panel.X + 60 * s, rightX = panel.X + panel.Width * 0.56f;
        float top = panel.Y + 150 * s;
        float line = Math.Min(78 * s, (panel.Bottom - 40 * s - top) / Math.Max(1, _info.Length));
        float colW = rightX - leftX - 40 * s;
        float y = top;
        for (int i = 0; i < _info.Length; i++) {
            // a faint band behind every other row, so the eye can follow a line across
            if (i % 2 == 0)
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(leftX - 24 * s), (int)(y - line / 2f + 4 * s), (int)(colW + 48 * s), (int)(line - 8 * s)), 16 * s, new Color(40, 30, 22, 14));
            // "label: value" -> the label on the left, the value on the right in bold ink
            var t = _info[i];
            int cut = t.LastIndexOf(':');
            if (cut > 0 && cut < t.Length - 1) {
                PText(sb, t[..cut], new Vector2(leftX, y), 0.92f, UITextButton.PhoneEdge, center: false, maxWidth: colW * 0.68f);
                var val = t[(cut + 1)..].Trim();
                var font2 = FontGlobals.RebirthFont;
                float vsc = 1.05f * PTextK * s;
                PText(sb, val, new Vector2(leftX + colW - font2.MeasureString(val).X * vsc, y), 1.05f, Ink, center: false);
            }
            else
                PText(sb, t, new Vector2(leftX, y), 0.92f, Ink, center: false, maxWidth: colW);
            y += line;
        }
        y = top;
        line = 62 * s;
        PText(sb, TankGame.GameLanguage.TankKillsPerType + ":", new Vector2(rightX, y), 1.05f, Ink, center: false, maxWidth: panel.Right - rightX - 40 * s);
        y += line * 1.1f;
        int shown = 0;
        for (int i = 1; i < TankGame.SaveFile.TankKills.Count; i++) {
            var elem = TankGame.SaveFile.TankKills.ElementAt(i);
            if (elem.Value == 0)
                continue;
            var name = TankID.Collection.GetKey(elem.Key)!.SplitByCamel();
            var col = Systems.AI.AITank.TankDestructionColors.TryGetValue(elem.Key, out var c) ? Color.Lerp(c, Color.Black, 0.35f) : Ink;
            PText(sb, $"{name}: {elem.Value}", new Vector2(rightX, y), 0.9f, col, center: false, maxWidth: panel.Right - rightX - 40 * s);
            y += line * 0.9f;
            if (y > panel.Bottom - 50 * s || ++shown >= 14) break;
        }
        if (shown == 0)
            PText(sb, "None yet!", new Vector2(rightX, y), 0.9f, UITextButton.PhoneEdge, center: false);
    }

    private static void DrawAchievementsPanel(SpriteBatch sb, Rectangle panel) {
        float s = PhoneScale;
        var list = Achievements.VanillaAchievements.Repository.GetAchievements();
        int unlocked = list.Count(a => a.IsComplete);
        PText(sb, $"Achievements   {unlocked} / {list.Count} unlocked", new Vector2(panel.Center.X, panel.Y + 56 * s), 1.4f, Ink);

        // grid: as big as fits above the details area
        float gap = 14 * s, top = panel.Y + 112 * s, detailsH = 190 * s;
        float availW = panel.Width - 60 * s, availH = panel.Height - (top - panel.Y) - detailsH;
        float size = 110 * s;
        int cols = 1, rows = 1;
        for (; size > 30 * s; size -= 4 * s) {
            cols = Math.Max(1, (int)((availW + gap) / (size + gap)));
            rows = (list.Count + cols - 1) / cols;
            if (rows * (size + gap) - gap <= availH)
                break;
        }
        float gridW = cols * (size + gap) - gap;
        float x0 = panel.Center.X - gridW / 2f;
        _achSelected = Math.Clamp(_achSelected, 0, Math.Max(0, list.Count - 1));
        for (int i = 0; i < list.Count; i++) {
            var a = list[i];
            var r = new Rectangle((int)(x0 + (i % cols) * (size + gap)), (int)(top + (i / cols) * (size + gap)), (int)size, (int)size);
            if (i == _achSelected)
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X - (int)(5 * s), r.Y - (int)(5 * s), r.Width + (int)(10 * s), r.Height + (int)(10 * s)), 14 * s, new Color(30, 110, 200));
            ToggleSwitch.DrawRoundedRect(sb, r, 12 * s, a.IsComplete ? new Color(255, 236, 170) : new Color(200, 194, 184));
            var tex = (a as Achievements.Achievement)?.Texture ?? Achievements.Achievement.MysteryTexture;
            if (tex is not null) {
                var inner = new Rectangle(r.X + (int)(6 * s), r.Y + (int)(6 * s), r.Width - (int)(12 * s), r.Height - (int)(12 * s));
                sb.Draw(tex, inner, a.IsComplete ? Color.White : new Color(70, 70, 70, 200));
            }
            if (!a.IsComplete)
                DrawLock(sb, new Vector2(r.Right - size * 0.22f, r.Bottom - size * 0.24f), size * 0.16f);
            if (!_pmDown && _pmWasDown && r.Contains(_pmPress) && r.Contains(MouseUtils.MousePosition) && !_pmEnteredThisFrame) {
                _achSelected = i;
                SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
            }
        }
        if (list.Count > 0) {
            var sel = list[_achSelected];
            var desc = (sel as Achievements.Achievement)?.Description ?? "";
            float y = panel.Bottom - detailsH + 48 * s;
            PText(sb, (sel.IsComplete ? "Unlocked:  " : "Locked:  ") + sel.Name, new Vector2(panel.Center.X, y), 1.25f,
                sel.IsComplete ? new Color(30, 130, 60) : Ink);
            PWrap(sb, desc, panel.Center.X, y + 36 * s, 1.0f, UITextButton.PhoneEdge, panel.Width - 120 * s);
        }
    }

    /// <summary>A small padlock (body + shackle).</summary>
    private static void DrawLock(SpriteBatch sb, Vector2 c, float r) {
        var dark = new Color(60, 52, 44);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle((int)(c.X - r), (int)(c.Y - r * 0.2f), (int)(r * 2), (int)(r * 1.5f)), r * 0.3f, dark);
        float t = Math.Max(2f, r * 0.28f);
        ToggleSwitch.DrawLine(sb, new Vector2(c.X - r * 0.6f, c.Y - r * 0.1f), new Vector2(c.X - r * 0.6f, c.Y - r * 0.75f), t, dark);
        ToggleSwitch.DrawLine(sb, new Vector2(c.X + r * 0.6f, c.Y - r * 0.1f), new Vector2(c.X + r * 0.6f, c.Y - r * 0.75f), t, dark);
        ToggleSwitch.DrawLine(sb, new Vector2(c.X - r * 0.6f - t / 2, c.Y - r * 0.9f), new Vector2(c.X + r * 0.6f + t / 2, c.Y - r * 0.9f), t, dark);
    }

    // ---- tank color
    private static int _colorSlot;
    private static Texture2D?[] _colorTex = [];

    private static void DrawTankColors(SpriteBatch sb) {
        float s = PhoneScale;
        bool twoPlayers = !Client.IsConnected();
        int n = twoPlayers ? 3 : 1;
        if (twoPlayers) {
            if (PButton(sb, PhoneColumnSlot(0, n), "Player 1", selected: _colorSlot == 0))
                _colorSlot = 0;
            if (PButton(sb, PhoneColumnSlot(1, n), "Player 2", selected: _colorSlot == 1))
                _colorSlot = 1;
        }
        else
            _colorSlot = 0;
        if (PButton(sb, PhoneColumnSlot(n - 1, n), "Back")) {
            GoToState(Client.IsConnected() ? UIState.MPLobby : UIState.PlayList);
            return;
        }

        var panel = RightPanel(70, 40);
        DrawPanel(sb, panel);
        var gd = TankGame.Instance.GraphicsDevice;
        int chosen = PlayerColors.GetChoice(_colorSlot);

        // tabs: plain colors / special skins (unlocked by beating things)
        bool Tapped(Rectangle r) => !_pmDown && _pmWasDown && r.Contains(_pmPress) && r.Contains(MouseUtils.MousePosition) && !_pmEnteredThisFrame;
        string whose = twoPlayers ? $"P{_colorSlot + 1} " : "";
        string[] tabNames = [whose + "Colors", $"Special  {Unlocks.UnlockedCount}/{PlayerColors.Skins.Length}"];
        float tabW = 330 * s, tabH = 64 * s, tabGap = 14 * s;
        float tabX = panel.Center.X - tabW - tabGap / 2f;
        var track = new Rectangle((int)(tabX - 8 * s), (int)(panel.Y + 18 * s), (int)(tabW * 2 + tabGap + 16 * s), (int)(tabH + 16 * s));
        ToggleSwitch.DrawRoundedRect(sb, track, track.Height / 2f, new Color(222, 214, 198));
        for (int t = 0; t < 2; t++) {
            var tr = new Rectangle((int)(tabX + t * (tabW + tabGap)), (int)(panel.Y + 26 * s), (int)tabW, (int)tabH);
            bool on = _colorTab == t;
            if (on) {
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle(tr.X, tr.Y + (int)(4 * s), tr.Width, tr.Height), tr.Height / 2f, new Color(0, 0, 0, 40));
                ToggleSwitch.DrawRoundedRect(sb, tr, tr.Height / 2f, t == 1 ? new Color(255, 206, 84) : new Color(255, 250, 238));
            }
            PText(sb, tabNames[t], tr.Center.ToVector2(), 0.95f, on ? Ink : Ink * 0.6f, maxWidth: tr.Width - 24 * s);
            if (Tapped(tr) && _colorTab != t) {
                _colorTab = t;
                SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
                Internals.Common.Framework.Input.Haptics.Tap();
            }
        }

        int count = PlayerColors.Count;
        if (_colorTex.Length != count)
            _colorTex = new Texture2D?[count];
        for (int i = 0; i < count; i++) {
            if (_colorTex[i] is null || _colorTex[i]!.IsDisposed)
                _colorTex[i] = PlayerColors.MakeTexture(gd, i);
            else if (PlayerColors.IsAnimated(i))
                PlayerColors.Animate(_colorTex[i]!, i);
        }
        if (_lockedTex is null || _lockedTex.IsDisposed) {
            _lockedTex = new Texture2D(gd, 32, 32);
            _lockedTex.SetData(Enumerable.Repeat(new Color(58, 56, 62), 32 * 32).ToArray());
        }

        bool special = _colorTab == 1;
        int first = special ? PlayerColors.Palette.Length : 0;
        int tileCount = special ? PlayerColors.Skins.Length : PlayerColors.Palette.Length;
        int cols = special ? 5 : 4, rows = (tileCount + cols - 1) / cols;
        float gap = (special ? 12 : 18) * s, top = panel.Y + 118 * s;
        // Special: a card under the grid tells the story of the skin you picked (and how to unlock it)
        float cardH = special ? 310 * s : 0;
        var card = new Rectangle((int)(panel.X + 20 * s), (int)(panel.Bottom - 20 * s - cardH), (int)(panel.Width - 40 * s), (int)cardH);
        float gridBottom = special ? card.Y - 14 * s : panel.Bottom - 20 * s;
        float tw = (panel.Width - 40 * s - gap * (cols - 1)) / cols;
        float th = (gridBottom - top - gap * (rows - 1)) / rows;
        if (special && (_infoSkin < first || _infoSkin >= PlayerColors.Count))
            _infoSkin = chosen >= first ? chosen : first;
        var tiles = new Rectangle[tileCount];
        for (int k = 0; k < tileCount; k++) {
            int i = first + k;
            var r = new Rectangle((int)(panel.X + 20 * s + (k % cols) * (tw + gap)), (int)(top + (k / cols) * (th + gap)), (int)tw, (int)th);
            tiles[k] = r;
            bool sel = i == chosen;
            bool open = PlayerColors.IsUnlocked(i);
            bool info = special && i == _infoSkin && !sel;
            if (sel || info)
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X - (int)(5 * s), r.Y - (int)(5 * s), r.Width + (int)(10 * s), r.Height + (int)(10 * s)), 22 * s,
                    sel ? new Color(52, 199, 89) : new Color(255, 180, 60));
            ToggleSwitch.DrawRoundedRect(sb, r, 18 * s, !open ? new Color(214, 208, 198) : sel ? new Color(255, 250, 238) : new Color(232, 226, 214));
            float nameScale = special ? 0.8f : 0.9f;
            PText(sb, PlayerColors.NameOf(i), new Vector2(r.Center.X, r.Bottom - (special ? 24 : 32) * s), nameScale, open ? Ink : Ink * 0.55f, maxWidth: r.Width - 16 * s);
            if (!open && PlayerColors.SkinAt(i) is { } lsk && Unlocks.GoalFor(lsk.Unlock) is { Target: > 1 } goal) {
                // how far along you are
                int have = Unlocks.Progress(goal.Key);
                var bar = new Rectangle(r.X + (int)(16 * s), r.Bottom - (int)(54 * s), r.Width - (int)(32 * s), (int)(8 * s));
                ToggleSwitch.DrawRoundedRect(sb, bar, 4 * s, new Color(190, 182, 170));
                if (have > 0)
                    ToggleSwitch.DrawRoundedRect(sb, new Rectangle(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * have / goal.Target), bar.Height), 4 * s, new Color(255, 180, 60));
            }
            if (Tapped(r)) {
                if (special)
                    _infoSkin = i;
                if (open) {
                    PlayerColors.SetChoice(_colorSlot, i);
                    SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
                }
                else {
                    SoundPlayer.SoundError();
                    Internals.Common.Framework.Input.Haptics.Pulse(60, 80);
                }
            }
        }
        if (special && PlayerColors.SkinAt(_infoSkin) is { } isk)
            DrawSkinCard(sb, card, isk, PlayerColors.IsUnlocked(_infoSkin), s);

        // the tanks themselves (3D), then the check on the chosen one and padlocks on the locked ones
        sb.End();
        float yaw = (float)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 0.8 % (Math.PI * 2));
        gd.Clear(ClearOptions.DepthBuffer, Color.Black, 1f, 0);
        for (int k = 0; k < tileCount; k++) {
            int i = first + k;
            var r = tiles[k];
            bool open = PlayerColors.IsUnlocked(i);
            int textRoom = special ? (open ? 46 : 62) : 62;
            var modelRect = new Rectangle(r.X, r.Y + (int)(2 * s), r.Width, r.Height - (int)(textRoom * s));
            DrawTankPreview(gd, modelRect, open ? _colorTex[i]! : _lockedTex, i == chosen || (special && i == _infoSkin) ? yaw : 0.6f);
        }
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        float badge = special ? 15 : 20;
        for (int k = 0; k < tileCount; k++) {
            int i = first + k;
            var r = tiles[k];
            if (i == chosen)
                ToggleSwitch.DrawCheckBadge(sb, new Vector2(r.Right - (badge + 6) * s, r.Y + (badge + 6) * s), badge * s, new Color(52, 199, 89));
            else if (!PlayerColors.IsUnlocked(i))
                DrawPadlock(sb, new Vector2(r.Right - (badge + 8) * s, r.Y + (badge + 10) * s), badge * 0.9f * s);
        }
    }

    private static int _infoSkin = -1;

    /// <summary>The selected skin's name, where it's from, its story, and (locked) what to do for it.</summary>
    private static void DrawSkinCard(SpriteBatch sb, Rectangle card, PlayerColors.Skin skin, bool open, float s) {
        ToggleSwitch.DrawRoundedRect(sb, card, 22 * s, new Color(255, 250, 238));
        var font = FontGlobals.RebirthFont;
        float k = PTextK * PhoneScale; // (the same text size as the rest of the phone menus)
        float pad = 24 * s;
        float x = card.X + pad, y = card.Y + pad * 0.7f, maxW = card.Width - pad * 2;
        bool locked = !open && Unlocks.GoalFor(skin.Unlock) is not null;

        // name, then where and when it's from
        float nameSc = 1.2f * k;
        var nm = font.MeasureString(skin.Name) * nameSc;
        sb.DrawString(font, skin.Name, new Vector2(x, y), Ink, new Vector2(nameSc), 0f, Vector2.Zero);
        if (!string.IsNullOrEmpty(skin.Origin)) {
            float oSc = 0.85f * k;
            var om = font.MeasureString(skin.Origin) * oSc;
            sb.DrawString(font, skin.Origin, new Vector2(x + nm.X + 16 * s, y + nm.Y - om.Y - 2 * s), Ink * 0.55f, new Vector2(oSc), 0f, Vector2.Zero);
        }
        y += nm.Y + 6 * s;

        // the story, as many lines as fit above the unlock line
        float bodySc = 0.86f * k;
        float lineH = font.LineHeight * bodySc;
        float bottom = card.Bottom - pad * 0.6f - (locked ? lineH + 10 * s : 0);
        var lines = WrapToWidth(skin.Blurb, bodySc, maxW);
        for (int l = 0; l < lines.Count && y + lineH <= bottom; l++) {
            bool last = l + 1 < lines.Count && y + lineH * 2 > bottom;
            sb.DrawString(font, last ? lines[l] + "..." : lines[l], new Vector2(x, y), Ink * 0.85f, new Vector2(bodySc), 0f, Vector2.Zero);
            y += lineH;
        }

        if (locked && Unlocks.GoalFor(skin.Unlock) is { } goal) {
            string need = goal.Target > 1 ? $"To unlock: {goal.Task}  ({Unlocks.Progress(goal.Key):N0} / {goal.Target:N0})" : $"To unlock: {goal.Task}";
            float lock_ = lineH * 0.42f;
            float ny = card.Bottom - pad * 0.6f - lineH;
            DrawPadlock(sb, new Vector2(x + lock_, ny + lineH * 0.55f), lock_);
            float tx = x + lock_ * 2.6f;
            float nSc = Math.Min(bodySc, (card.Right - pad - tx) / Math.Max(1f, font.MeasureString(need).X));
            sb.DrawString(font, need, new Vector2(tx, ny), new Color(196, 110, 20), new Vector2(nSc), 0f, Vector2.Zero);
        }
    }

    /// <summary>Splits text into lines that fit <paramref name="maxWidth"/> pixels at <paramref name="scale"/>.</summary>
    private static List<string> WrapToWidth(string text, float scale, float maxWidth) {
        var font = FontGlobals.RebirthFont;
        var lines = new List<string>();
        string line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
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

    private static int _colorTab;
    private static Texture2D? _lockedTex;

    /// <summary>A little padlock (body + shackle).</summary>
    private static void DrawPadlock(SpriteBatch sb, Vector2 c, float size) {
        var dark = new Color(70, 62, 54);
        // shackle: a thick ring, its lower half hidden by the body
        ToggleSwitch.DrawDisc(sb, c - new Vector2(0, size * 0.35f), size * 0.62f, dark);
        ToggleSwitch.DrawDisc(sb, c - new Vector2(0, size * 0.35f), size * 0.36f, new Color(214, 208, 198));
        var body = new Rectangle((int)(c.X - size * 0.8f), (int)(c.Y - size * 0.25f), (int)(size * 1.6f), (int)(size * 1.2f));
        ToggleSwitch.DrawRoundedRect(sb, body, size * 0.25f, dark);
        ToggleSwitch.DrawDisc(sb, new Vector2(body.Center.X, body.Center.Y - size * 0.05f), size * 0.18f, new Color(214, 208, 198));
    }

    private static Matrix[] _previewBones = [];

    /// <summary>Draws the player tank model with a given texture inside a screen rectangle.</summary>
    private static void DrawTankPreview(GraphicsDevice gd, Rectangle rect, Texture2D tex, float yaw) {
        var model = ModelGlobals.TankPlayer.Asset;
        if (model is null || rect.Width <= 0 || rect.Height <= 0)
            return;
        var bounds = new Rectangle(0, 0, gd.PresentationParameters.BackBufferWidth, gd.PresentationParameters.BackBufferHeight);
        rect = Rectangle.Intersect(rect, bounds);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var oldViewport = gd.Viewport;
        gd.Viewport = new Viewport(rect);
        gd.DepthStencilState = DepthStencilState.Default;
        gd.BlendState = BlendState.Opaque;
        gd.RasterizerState = RasterizerState.CullCounterClockwise;
        gd.SamplerStates[0] = SamplerState.PointClamp;

        float aspect = rect.Width / (float)rect.Height;
        const float viewHeight = 34f;
        var projection = Matrix.CreateOrthographic(viewHeight * aspect, viewHeight, -500f, 500f);
        var view = Matrix.CreateLookAt(new Vector3(0, 70, 70), new Vector3(0, 6, 0), Vector3.Up);

        var oldRoot = model.Root.Transform;
        var cannon = model.Meshes["Cannon"];
        var oldCannon = cannon.ParentBone.Transform;
        model.Root.Transform = Matrix.CreateRotationY(yaw);
        cannon.ParentBone.Transform = Matrix.Identity;
        if (_previewBones.Length != model.Bones.Count)
            _previewBones = new Matrix[model.Bones.Count];
        model.CopyAbsoluteBoneTransformsTo(_previewBones);

        foreach (var mesh in model.Meshes) {
            if (mesh.Name == "Shadow")
                continue;
            foreach (BasicEffect effect in mesh.Effects) {
                effect.World = _previewBones[mesh.ParentBone.Index];
                effect.View = view;
                effect.Projection = projection;
                effect.TextureEnabled = true;
                effect.Texture = tex;
                effect.Alpha = 1f;
                effect.EmissiveColor = Vector3.Zero;
                effect.SetDefaultGameLighting_IngameEntities(specular: true, ambientMultiplier: 2f);
            }
            mesh.Draw();
        }

        model.Root.Transform = oldRoot;
        cannon.ParentBone.Transform = oldCannon;
        gd.Viewport = oldViewport;
    }
}
