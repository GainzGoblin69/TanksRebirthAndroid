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
        or UIState.MPJoin or UIState.MPLobby or UIState.MPKeypad or UIState.MPOnline or UIState.MPOnlineJoining;

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
        if (tapped)
            SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
        return tapped;
    }

    private static void PText(SpriteBatch sb, string text, Vector2 pos, float scale, Color color, bool center = true) {
        var font = FontGlobals.RebirthFont;
        var m = font.MeasureString(text);
        // a bit larger than the desktop text for phones, but always fitting inside the right-hand panel
        float sc = scale * 1.35f * PhoneScale;
        float maxW = RightPanel().Width * 0.92f;
        if (m.X * sc > maxW)
            sc = maxW / Math.Max(1f, m.X);
        sb.DrawString(font, text, pos, color, new Vector2(sc), 0f, center ? m / 2f : new Vector2(0, m.Y / 2f));
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
                GoToState(UIState.MPMode);
                break;
            case UIState.MPKeypad:
                GoToState(_keypadForCode ? UIState.MPOnline : UIState.MPJoin);
                break;
            case UIState.MPOnline:
                GoToState(UIState.MPMode);
                break;
            case UIState.MPOnlineJoining:
                Net.Online.OnlineSession.Leave();
                GoToState(UIState.MPOnline);
                break;
            case UIState.MPLobby:
                if (Net.Online.OnlineSession.Active) {
                    Net.Online.OnlineSession.Leave();
                    GoToState(UIState.MPOnline);
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
            MenuState = UIState.MPOnline;
        }
        else if (MenuState == UIState.MPLobby && !Client.IsConnected() && _lobbyGrace <= 0 && !(online && Net.Online.OnlineSession.Busy)) {
            ChatSystem.SendMessage(online ? "Disconnected from the online game." : "Disconnected from the Wi-Fi game.", Color.Orange);
            if (online) {
                Net.Online.OnlineSession.Leave();
                MenuState = UIState.MPOnline;
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
            MenuState = UIState.MPLobby;
        }
        if (InputUtils.KeyJustPressed(Keys.Escape) && !_suppressEscape)
            PhoneScreenBack();
        _suppressEscape = false;
    }
    private static bool _suppressEscape;

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
            case UIState.MPOnlineJoining: DrawMPOnlineJoining(sb); break;
        }
        sb.End();
        sb.Begin();
        if (!_pmDown)
            _pmEnteredThisFrame = false;
    }

    // ---- multiplayer: choose a mode
    private static void DrawMPMode(SpriteBatch sb) {
        int n = 5;
        var panel = RightPanel();
        DrawPanel(sb, panel);
        float s = PhoneScale;
        var cx = panel.Center.X;
        PText(sb, "Multiplayer", new Vector2(cx, panel.Y + 80 * s), 1.6f, Ink);
        PText(sb, "Same Device", new Vector2(cx, panel.Y + 200 * s), 1.05f, Ink);
        PText(sb, "Two players on this phone: touch + a controller, or two controllers.", new Vector2(cx, panel.Y + 255 * s), 0.8f, UITextButton.PhoneEdge);
        PText(sb, "Wi-Fi", new Vector2(cx, panel.Y + 360 * s), 1.05f, Ink);
        PText(sb, "Phones on the same Wi-Fi. One hosts, the others join from the list.", new Vector2(cx, panel.Y + 415 * s), 0.8f, UITextButton.PhoneEdge);
        PText(sb, "Online", new Vector2(cx, panel.Y + 520 * s), 1.05f, Ink);
        PText(sb, "Play with anyone over the internet. The host shares a 6-digit code.", new Vector2(cx, panel.Y + 575 * s), 0.8f, UITextButton.PhoneEdge);

        if (PButton(sb, PhoneColumnSlot(0, n), "Same Device"))
            GoToState(UIState.MPLocal);
        if (PButton(sb, PhoneColumnSlot(1, n), "Host on Wi-Fi")) {
            if (LanSession.Host()) {
                _lobbyGrace = 180; // give the local connection a moment
                GoToState(UIState.MPLobby);
            }
            else {
                ChatSystem.SendMessage("Couldn't start hosting. Is Wi-Fi on?", Color.Red);
                SoundPlayer.SoundError();
            }
        }
        if (PButton(sb, PhoneColumnSlot(2, n), "Join on Wi-Fi")) {
            LanSession.StartSearch();
            GoToState(UIState.MPJoin);
        }
        if (PButton(sb, PhoneColumnSlot(3, n), "Online"))
            GoToState(UIState.MPOnline);
        if (PButton(sb, PhoneColumnSlot(4, n), "Back"))
            PhoneScreenBack();
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
            PText(sb, "You get a 6-digit code. Send it to your friends.", new Vector2(cx, panel.Y + 255 * s), 0.8f, UITextButton.PhoneEdge);
            PText(sb, "Join with Code", new Vector2(cx, panel.Y + 360 * s), 1.05f, Ink);
            PText(sb, "Type the code from the host's screen.", new Vector2(cx, panel.Y + 415 * s), 0.8f, UITextButton.PhoneEdge);
            PText(sb, "Campaign, Level Select and modifiers work the same as on Wi-Fi.", new Vector2(cx, panel.Bottom - 70 * s), 0.7f, UITextButton.PhoneEdge);
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
        int n = 3;
        var panel = RightPanel();
        DrawPanel(sb, panel);
        float s = PhoneScale;
        var cx = panel.Center.X;
        int pads = LocalCoop.ConnectedPads;
        PText(sb, "Same Device", new Vector2(cx, panel.Y + 70 * s), 1.3f, Ink);
        PText(sb, $"Controllers connected: {pads}", new Vector2(cx, panel.Y + 160 * s), 0.85f, pads > 0 ? new Color(30, 130, 60) : new Color(170, 60, 40));
        PText(sb, "Touch + Controller: player 1 uses the touch controls,", new Vector2(cx, panel.Y + 250 * s), 0.6f, Ink);
        PText(sb, "player 2 uses the controller.", new Vector2(cx, panel.Y + 290 * s), 0.6f, Ink);
        PText(sb, "Two Controllers: no touch controls, one controller each.", new Vector2(cx, panel.Y + 360 * s), 0.6f, Ink);
        PText(sb, "Controller: left stick drive, right stick aim,", new Vector2(cx, panel.Y + 450 * s), 0.6f, UITextButton.PhoneEdge);
        PText(sb, "RB shoot, LB mine, Start pause.", new Vector2(cx, panel.Y + 490 * s), 0.6f, UITextButton.PhoneEdge);
        PText(sb, "Campaign, Level Select and modifiers work the same as single player.", new Vector2(cx, panel.Bottom - 60 * s), 0.5f, UITextButton.PhoneEdge);

        if (PButton(sb, PhoneColumnSlot(0, n), "Touch + Controller", enabled: pads >= 1))
            StartLocalCoop(LocalCoop.Scheme.TouchAndController);
        if (PButton(sb, PhoneColumnSlot(1, n), "Two Controllers", enabled: pads >= 2))
            StartLocalCoop(LocalCoop.Scheme.TwoControllers);
        if (PButton(sb, PhoneColumnSlot(2, n), "Back"))
            PhoneScreenBack();
    }

    private static void StartLocalCoop(LocalCoop.Scheme scheme) {
        LocalCoop.Start(scheme);
        EnterCampaigns();
    }

    // ---- Wi-Fi join
    private static void DrawMPJoin(SpriteBatch sb) {
        float s = PhoneScale;
        var games = LanSession.Found.OrderBy(g => g.Name).Take(4).ToList();
        int n = 2;
        if (PButton(sb, PhoneColumnSlot(0, n), "Enter Address")) {
            _keypadForCode = false;
            GoToState(UIState.MPKeypad);
        }
        if (PButton(sb, PhoneColumnSlot(1, n), "Back"))
            PhoneScreenBack();

        var panel = RightPanel();
        DrawPanel(sb, panel);
        var cx = panel.Center.X;
        PText(sb, "Join on Wi-Fi", new Vector2(cx, panel.Y + 70 * s), 1.3f, Ink);
        if (games.Count == 0) {
            int dots = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 2) % 4;
            PText(sb, "Looking for games" + new string('.', dots), new Vector2(cx, panel.Y + 220 * s), 0.9f, Ink);
            PText(sb, "Make sure both phones are on the same Wi-Fi", new Vector2(cx, panel.Y + 300 * s), 0.6f, UITextButton.PhoneEdge);
            PText(sb, "and the other phone tapped Host on Wi-Fi.", new Vector2(cx, panel.Y + 340 * s), 0.6f, UITextButton.PhoneEdge);
            PText(sb, "Not showing up? Tap Enter Address and type the", new Vector2(cx, panel.Y + 420 * s), 0.6f, UITextButton.PhoneEdge);
            PText(sb, "address shown on the host's screen.", new Vector2(cx, panel.Y + 460 * s), 0.6f, UITextButton.PhoneEdge);
            return;
        }
        for (int i = 0; i < games.Count; i++) {
            var g = games[i];
            var r = new Rectangle(panel.X + (int)(40 * s), panel.Y + (int)((150 + i * 130) * s), panel.Width - (int)(80 * s), (int)(110 * s));
            bool full = g.Players >= g.MaxPlayers;
            if (PButton(sb, r, $"{g.Name}  ({g.Players}/{g.MaxPlayers})", enabled: !full)) {
                LanSession.Join(g.EndPoint.Address.ToString(), g.EndPoint.Port);
                ChatSystem.SendMessage($"Joining {g.Name}...", Color.White);
            }
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
        PText(sb, _keypadText.Length == 0 ? hint : _keypadText, new Vector2(cx, panel.Y + 80 * s), _keypadText.Length == 0 ? 0.9f : 1.5f, Ink);
        string[] keys = ["1", "2", "3", "4", "5", "6", "7", "8", "9", _keypadForCode ? "" : ".", "0", "<"];
        float kw = Math.Min(panel.Width / 3.6f, 260 * s), kh = (panel.Height - 190 * s) / 4.3f;
        float x0 = cx - kw * 1.5f - 10 * s, y0 = panel.Y + 150 * s;
        for (int i = 0; i < keys.Length; i++) {
            var r = new Rectangle((int)(x0 + (i % 3) * (kw + 10 * s)), (int)(y0 + (i / 3) * (kh + 10 * s)), (int)kw, (int)kh);
            if (keys[i].Length == 0)
                continue;
            if (PButton(sb, r, keys[i] == "<" ? "Delete" : keys[i])) {
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
        PText(sb, NetPlay.ServerName ?? "Wi-Fi game", new Vector2(cx, panel.Y + 70 * s), 1.2f, Ink);
        if (Net.Online.OnlineSession.Active) {
            var os = Net.Online.OnlineSession.Code is { } code && host
                ? $"Code: {code}   (friends: Multiplayer > Online > Join with Code)"
                : host ? Net.Online.OnlineSession.Status
                : Net.Online.OnlineSession.Connection == Net.Online.OnlineSession.Link.Relay ? "Online, connected through the relay" : "Online, connected directly";
            PText(sb, os, new Vector2(cx, panel.Y + 135 * s), host && Net.Online.OnlineSession.Code is not null ? 0.9f : 0.7f, host && Net.Online.OnlineSession.Code is not null ? new Color(30, 110, 200) : UITextButton.PhoneEdge);
        }
        else if (host)
            PText(sb, $"Others: Join on Wi-Fi (address {LanSession.LocalAddress()})", new Vector2(cx, panel.Y + 135 * s), 0.6f, UITextButton.PhoneEdge);

        int count = Math.Max(Server.CurrentClientCount, 0);
        for (int i = 0; i < 4; i++) {
            var row = new Rectangle(panel.X + (int)(60 * s), panel.Y + (int)((190 + i * 90) * s), panel.Width - (int)(120 * s), (int)(74 * s));
            var c = Server.ConnectedClients is not null && i < Server.ConnectedClients.Length ? Server.ConnectedClients[i] : null;
            bool present = c is not null && i < count;
            var col = PlayerID.PlayerTankColors[i];
            ToggleSwitch.DrawRoundedRect(sb, row, 20 * s, present ? Color.Lerp(col, Color.White, 0.75f) : new Color(225, 220, 210));
            ToggleSwitch.DrawDisc(sb, new Vector2(row.X + 45 * s, row.Center.Y), 22 * s, present ? col : new Color(190, 184, 176));
            PText(sb, present ? $"P{i + 1}  {c!.Name}" + (i == 0 ? "  (host)" : "") : $"P{i + 1}  waiting for player...", new Vector2(row.X + 90 * s, row.Center.Y), 0.75f, present ? Ink : new Color(140, 130, 120), center: false);
        }

        if (host) {
            int n = 4;
            if (PButton(sb, PhoneColumnSlot(0, n), "Play", enabled: count >= 1)) {
                plrsConfirmed = 0;
                EnterCampaigns();
            }
            // slot 1: the Difficulties button (a UIElement, placed by the phone layout)
            if (PButton(sb, PhoneColumnSlot(2, n), "Tank Color"))
                GoToState(UIState.TankColors);
            if (PButton(sb, PhoneColumnSlot(3, n), "Leave"))
                PhoneScreenBack();
            if (count < 2)
                PText(sb, "Waiting for others to join...", new Vector2(cx, panel.Bottom - 60 * s), 0.65f, UITextButton.PhoneEdge);
        }
        else {
            int n = 2;
            if (PButton(sb, PhoneColumnSlot(0, n), "Tank Color"))
                GoToState(UIState.TankColors);
            if (PButton(sb, PhoneColumnSlot(1, n), "Leave"))
                PhoneScreenBack();
            PText(sb, "Waiting for the host to start...", new Vector2(cx, panel.Bottom - 60 * s), 0.75f, Ink);
        }
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
        PText(sb, twoPlayers ? $"Player {_colorSlot + 1}'s tank color" : "Your tank color", new Vector2(panel.Center.X, panel.Y + 50 * s), 1.0f, Ink);

        var gd = TankGame.Instance.GraphicsDevice;
        if (_colorTex.Length != PlayerColors.Palette.Length || _colorTex.Any(t => t is null || t.IsDisposed))
            _colorTex = PlayerColors.Palette.Select(c => (Texture2D?)PlayerColors.MakeTexture(gd, c)).ToArray();

        int cols = 4, rows = (PlayerColors.Palette.Length + cols - 1) / cols;
        float gap = 18 * s, top = panel.Y + 95 * s;
        float tw = (panel.Width - 40 * s - gap * (cols - 1)) / cols;
        float th = (panel.Bottom - 20 * s - top - gap * (rows - 1)) / rows;
        int chosen = PlayerColors.GetChoice(_colorSlot);
        var tiles = new Rectangle[PlayerColors.Palette.Length];
        for (int i = 0; i < tiles.Length; i++) {
            var r = new Rectangle((int)(panel.X + 20 * s + (i % cols) * (tw + gap)), (int)(top + (i / cols) * (th + gap)), (int)tw, (int)th);
            tiles[i] = r;
            bool sel = i == chosen;
            if (sel)
                ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X - (int)(6 * s), r.Y - (int)(6 * s), r.Width + (int)(12 * s), r.Height + (int)(12 * s)), 26 * s, new Color(52, 199, 89));
            ToggleSwitch.DrawRoundedRect(sb, r, 22 * s, sel ? new Color(255, 250, 238) : new Color(232, 226, 214));
            var name = PlayerColors.Palette[i].Name;
            PText(sb, name, new Vector2(r.Center.X, r.Bottom - 26 * s), 0.6f, Ink);
            if (!_pmDown && _pmWasDown && r.Contains(_pmPress) && r.Contains(MouseUtils.MousePosition) && !_pmEnteredThisFrame) {
                PlayerColors.SetChoice(_colorSlot, i);
                SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
            }
        }
        // the tanks themselves (3D), then the check on the chosen one
        sb.End();
        float yaw = (float)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 0.8 % (Math.PI * 2));
        gd.Clear(ClearOptions.DepthBuffer, Color.Black, 1f, 0);
        for (int i = 0; i < tiles.Length; i++) {
            var r = tiles[i];
            var modelRect = new Rectangle(r.X, r.Y + (int)(4 * s), r.Width, r.Height - (int)(52 * s));
            DrawTankPreview(gd, modelRect, _colorTex[i]!, i == chosen ? yaw : 0.6f);
        }
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        var cr = tiles[chosen];
        ToggleSwitch.DrawCheckBadge(sb, new Vector2(cr.Right - 26 * s, cr.Y + 26 * s), 20 * s, new Color(52, 199, 89));
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
