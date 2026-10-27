using System;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.UI.LevelEditor;
using TanksRebirth.GameContent.UI.MainMenu;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;
using TanksRebirth.Net.Online;

namespace TanksRebirth.GameContent.UI;

/// <summary>
/// Multiplayer game over: instead of a results screen each phone deals with on its own, everyone sees this.
/// The host picks Restart (everyone back to the menus: the host to Level Select, the others to the lobby) or
/// Quit (ends the game for everyone). Joined players see "Waiting on host..." and can Quit.
/// Also catches a lost connection mid-game, so nobody is left on a dead game.
/// </summary>
public static class MPEndScreen {
    public static bool Active { get; private set; }
    private static bool _wasInGame;
    private static Vector2 _press = new(-1);
    private static bool _down, _wasDown;

    public static void Show() {
        Active = true;
        _down = _wasDown = true; // ignore a finger that's already on the screen
    }

    private static bool InGame => !MainMenuUI.IsActive && !LevelEditorUI.IsActive;

    /// <summary>Called every update.</summary>
    public static void Update() {
        if (InGame && Client.IsConnected())
            _wasInGame = true;
        if (!InGame) {
            _wasInGame = false;
            Active = false;
            return;
        }
        // the game's connection went away (host left, Wi-Fi dropped): back to the menus
        if (_wasInGame && !Client.IsConnected()) {
            _wasInGame = false;
            Active = false;
            ChatSystem.SendMessage("The connection to the game was lost.", Color.Orange);
            LeaveToMenu();
            return;
        }
    }

    private static void LeaveSession() {
        if (OnlineSession.Active)
            OnlineSession.Leave();
        else
            LanSession.Leave();
    }

    /// <summary>Back to the main menu's Multiplayer screen, out of the game.</summary>
    private static void LeaveToMenu() {
        Active = false;
        LeaveSession();
        if (!LevelProgress.EndRunNow())
            BackToMenus();
        MainMenuUI.MenuState = MainMenuUI.UIState.MPMode;
    }

    private static void BackToMenus() {
        CampaignCompleteUI.IsViewingResults = false;
        TankMusicSystem.StopAll();
        IntermissionSystem.IsAwaitingNewMission = false;
        GameUI.Paused = false;
        GameUI.QuitOut(netSent: true);
    }

    /// <summary>Host: Restart. Everyone returns; the host lands on Level Select.</summary>
    private static void Restart() {
        Active = false;
        Client.SendReturnToMenu();
        if (!LevelProgress.EndRunNow())
            BackToMenus();
        MainMenuUI.OpenLevelSelect();
    }

    /// <summary>A joined player: the host chose Restart.</summary>
    public static void ReturnFromHost() {
        Active = false;
        if (!InGame)
            return;
        if (!LevelProgress.EndRunNow())
            BackToMenus();
        MainMenuUI.MenuState = MainMenuUI.UIState.MPLobby;
    }

    // ------------------------------------------------------------------ drawing and taps

    private static bool Tapped(Rectangle r) {
        var m = MouseUtils.MousePosition;
        bool hit = !_down && _wasDown && r.Contains(_press) && r.Contains(m);
        if (hit)
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);
        return hit;
    }

    public static void Draw(SpriteBatch sb) {
        if (!Active)
            return;
        _wasDown = _down;
        _down = InputUtils.CurrentMouseSnapshot.LeftButton == ButtonState.Pressed;
        if (_down && !_wasDown)
            _press = MouseUtils.MousePosition;

        float s = WindowUtils.WindowHeight / 1080f;
        int w = WindowUtils.WindowWidth, h = WindowUtils.WindowHeight;
        var font = FontGlobals.RebirthFont;
        bool host = Client.IsHost();

        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        sb.Draw(TextureGlobals.Pixels[Color.White], new Rectangle(0, 0, w, h), Color.Black * 0.55f);

        var panel = new Rectangle((int)(w / 2f - 520 * s), (int)(h / 2f - 260 * s), (int)(1040 * s), (int)(520 * s));
        int lip = (int)(10 * s);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(panel.X, panel.Y + lip, panel.Width, panel.Height), 36 * s, UITextButton.PhoneEdge);
        ToggleSwitch.DrawRoundedRect(sb, panel, 36 * s, UITextButton.PhoneFill);
        var ink = new Color(40, 30, 22);
        void Text(string t, float y, float scale, Color c) {
            var m = font.MeasureString(t);
            sb.DrawString(font, t, new Vector2(panel.Center.X, y), c, new Vector2(scale * s), 0f, m / 2f);
        }
        Text("Game Over", panel.Y + 90 * s, 2.0f, ink);

        var bw = 380 * s;
        var bh = 130 * s;
        var by = panel.Bottom - 70 * s - bh;
        if (host) {
            Text("Everyone is out. Try again?", panel.Y + 190 * s, 1.0f, UITextButton.PhoneEdge);
            var restart = new Rectangle((int)(panel.Center.X - bw - 30 * s), (int)by, (int)bw, (int)bh);
            var quit = new Rectangle((int)(panel.Center.X + 30 * s), (int)by, (int)bw, (int)bh);
            Button(sb, restart, "Restart", s, new Color(255, 226, 140));
            Button(sb, quit, "Quit", s, null);
            if (Tapped(restart)) { sb.End(); Restart(); return; }
            if (Tapped(quit)) { sb.End(); LeaveToMenu(); return; }
        }
        else {
            int dots = (int)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 2) % 4;
            Text("Waiting on host" + new string('.', dots), panel.Y + 190 * s, 1.1f, ink);
            var quit = new Rectangle((int)(panel.Center.X - bw / 2), (int)by, (int)bw, (int)bh);
            Button(sb, quit, "Quit", s, null);
            if (Tapped(quit)) { sb.End(); LeaveToMenu(); return; }
        }
        sb.End();
    }

    private static void Button(SpriteBatch sb, Rectangle r, string label, float s, Color? fill) {
        int lip = Math.Max(3, (int)(r.Height * 0.07f));
        bool pressing = _down && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        var face = pressing ? new Rectangle(r.X, r.Y + lip / 2, r.Width, r.Height - lip / 2) : new Rectangle(r.X, r.Y, r.Width, r.Height - lip);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X, r.Y + lip, r.Width, r.Height - lip), r.Height * 0.3f, UITextButton.PhoneEdge);
        ToggleSwitch.DrawRoundedRect(sb, face, r.Height * 0.3f, pressing ? UITextButton.PhonePressed : fill ?? Color.White);
        var font = FontGlobals.RebirthFont;
        var m = font.MeasureString(label);
        float scale = face.Height * 0.4f / Math.Max(1f, m.Y);
        sb.DrawString(font, label, face.Center.ToVector2(), new Color(40, 30, 22), new Vector2(scale), 0f, m / 2f);
    }
}
