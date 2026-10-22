using System;
using System.Collections.Generic;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.Internals;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals.UI;

namespace TanksRebirth.GameContent.UI.MainMenu;

/// <summary>
/// Phones: Level Select. A scrollable grid of every Campaign mission (with real rendered thumbnails); a cleared
/// mission opens a second grid with that mission once per difficulty modifier. Runs are one life and come back
/// here. Thumbnails are rendered once by loading each mission behind the menu, then cached as PNGs.
/// </summary>
public static partial class MainMenuUI {
    public const string LevelSelectCampaign = "Vanilla";

    private static Campaign? _lsCampaign;
    private static Texture2D?[] _lsThumbs = [];
    private static readonly Queue<int> _bakeQueue = new();
    private static int _bakeIdx = -1;
    private static int _bakeFrames;
    private static RenderTarget2D? _thumbHalf, _thumbFinal;
    private static SpriteBatch? _thumbBatch;

    private static float _lsScroll, _lsScrollStart;
    private static Vector2 _lsPressPos;
    private static bool _lsPressing, _lsDragged, _lsLaunching, _lsHooked;
    private static int _lsModMission = -1;
    private static bool _lsRestoreBackButton;

    /// <summary>Called at the start of every menu update.</summary>
    private static void LevelSelectPreUpdate() {
        if (_lsRestoreBackButton && !InLevelSelect) {
            _lsRestoreBackButton = false;
            GameUI.BackButton.IsVisible = MenuState == UIState.Campaigns;
        }
    }

    private static float LsScale => WindowUtils.WindowHeight / 1080f;
    private static string ThumbDir => Path.Combine(TankGame.SaveDirectory, "Thumbnails");
    private static string ThumbPath(int i) => Path.Combine(ThumbDir, $"{LevelSelectCampaign}_{i:D3}.png");
    private static bool InLevelSelect => MenuState is UIState.LevelSelect or UIState.LevelModifiers;

    public static void OpenLevelSelect() {
        foreach (var e in campaignNames)
            e?.Remove();
        campaignNames.Clear();
        if (!_lsHooked) {
            LevelProgress.OnRunFinished += OnLevelRunFinished;
            _lsHooked = true;
        }
        if (!LoadLevelSelectCampaign())
            return;
        _lsScroll = 0;
        _lsLaunching = false;
        GameUI.BackButton.IsVisible = false;
        MenuState = UIState.LevelSelect;
    }

    private static bool LoadLevelSelectCampaign() {
        if (_lsCampaign is not null)
            return true;
        var path = Path.Combine(TankGame.SaveDirectory, "Campaigns", LevelSelectCampaign + ".campaign");
        if (!File.Exists(path)) {
            ChatSystem.SendMessage("The Campaign isn't installed yet.", Color.Red);
            SoundPlayer.SoundError();
            return false;
        }
        _lsCampaign = Campaign.Load(path);
        _lsThumbs = new Texture2D?[_lsCampaign.CachedMissions.Length];
        Directory.CreateDirectory(ThumbDir);
        for (int i = 0; i < _lsThumbs.Length; i++) {
            try {
                if (File.Exists(ThumbPath(i))) {
                    _lsThumbs[i] = Texture2D.FromFile(TankGame.Instance.GraphicsDevice, ThumbPath(i));
                    continue;
                }
            }
            catch { /* re-render below */ }
            _bakeQueue.Enqueue(i);
        }
        return true;
    }

    private static void OnLevelRunFinished(LevelProgress.LevelRun run) {
        // back in the main menu after a run: reopen the grid it came from
        LoadLevelSelectCampaign();
        _lsLaunching = false;
        _lsModMission = run.Mission;
        GameUI.BackButton.IsVisible = false;
        MenuState = run.FromModifierGrid ? UIState.LevelModifiers : UIState.LevelSelect;
    }

    private static void LevelSelectBack() {
        if (MenuState == UIState.LevelModifiers) {
            MenuState = UIState.LevelSelect;
            return;
        }
        // leaving: stop rendering thumbnails and put the usual menu battlefield back
        CancelBake();
        LoadTemplateMission();
        GameHandler.ResetMenuStill();
        SetCampaignDisplay();
        // the Back button comes back next frame, so this same back press doesn't also leave the Campaigns screen
        _lsRestoreBackButton = true;
        MenuState = UIState.Campaigns;
    }

    private static void StartLevelRun(int mission, LevelProgress.Modifier? modifier, bool fromModifierGrid) {
        CancelBake();
        LevelProgress.BeginRun(new(LevelSelectCampaign, mission, modifier, fromModifierGrid));
        MissionCheckpoint = mission;
        if (!PrepareGameplay(LevelSelectCampaign, true, false)) {
            LevelProgress.AbortRun();
            return;
        }
        PlayerTank.StartingLives = 1; // level runs are one life
        _lsLaunching = true;
    }

    // ------------------------------------------------------------------ layout

    private readonly record struct LsLayout(float Margin, float Top, float TileW, float ThumbH, float TileH, float Gap, int Cols, float MaxScroll);

    private static int LsTileCount => MenuState == UIState.LevelModifiers ? LevelProgress.Modifiers.Length + 1 : _lsThumbs.Length;

    private static LsLayout GetLayout() {
        float s = LsScale, w = WindowUtils.WindowWidth, h = WindowUtils.WindowHeight;
        int cols = 6;
        float margin = 48 * s, gap = 18 * s, top = 130 * s;
        float tileW = (w - 2 * margin - (cols - 1) * gap) / cols;
        float thumbH = tileW * (h / Math.Max(w, 1f));
        float tileH = thumbH + 46 * s;
        int rows = (LsTileCount + cols - 1) / cols;
        float content = rows * (tileH + gap);
        float maxScroll = Math.Max(0, content - (h - top - 16 * s));
        return new(margin, top, tileW, thumbH, tileH, gap, cols, maxScroll);
    }

    private static Rectangle TileRect(LsLayout l, int i) {
        int col = i % l.Cols, row = i / l.Cols;
        return new Rectangle((int)(l.Margin + col * (l.TileW + l.Gap)), (int)(l.Top + row * (l.TileH + l.Gap) - _lsScroll), (int)l.TileW, (int)l.TileH);
    }

    private static Rectangle LsBackRect => new((int)(28 * LsScale), (int)(28 * LsScale), (int)(190 * LsScale), (int)(76 * LsScale));

    // ------------------------------------------------------------------ update

    private static void UpdateLevelSelect() {
        GameUI.BackButton.IsVisible = false;
        if (_lsCampaign is null)
            return;

        var layout = GetLayout();
        if (_lsLaunching)
            return; // a run is starting; leave the board to it
        HandleLevelSelectInput(layout);
        if (InLevelSelect && !_lsLaunching)
            BakeThumbnails();
    }

    private static void HandleLevelSelectInput(LsLayout layout) {
        if (InputUtils.KeyJustPressed(Keys.Escape)) {
            LevelSelectBack();
            return;
        }

        var cur = InputUtils.CurrentMouseSnapshot;
        var old = InputUtils.OldMouseSnapshot;
        var pos = new Vector2(cur.X, cur.Y);
        bool down = cur.LeftButton == ButtonState.Pressed, wasDown = old.LeftButton == ButtonState.Pressed;

        if (down && !wasDown) {
            _lsPressing = true;
            _lsDragged = false;
            _lsPressPos = pos;
            _lsScrollStart = _lsScroll;
        }
        if (down && _lsPressing) {
            float dy = pos.Y - _lsPressPos.Y;
            if (Math.Abs(dy) > 14 * LsScale)
                _lsDragged = true;
            if (_lsDragged)
                _lsScroll = MathHelper.Clamp(_lsScrollStart - dy, 0, layout.MaxScroll);
        }
        if (!down && wasDown && _lsPressing) {
            _lsPressing = false;
            if (!_lsDragged)
                HandleLevelSelectTap(layout, _lsPressPos);
        }

        // controller: d-pad scrolls (the cursor + A taps)
        var pad = InputUtils.CurrentGamePadSnapshot;
        if (pad.IsConnected) {
            float dir = (pad.DPad.Down == ButtonState.Pressed ? 1 : 0) - (pad.DPad.Up == ButtonState.Pressed ? 1 : 0);
            _lsScroll = MathHelper.Clamp(_lsScroll + dir * 18 * LsScale, 0, layout.MaxScroll);
        }
        _lsScroll = MathHelper.Clamp(_lsScroll, 0, layout.MaxScroll);
    }

    private static void HandleLevelSelectTap(LsLayout layout, Vector2 pos) {
        if (LsBackRect.Contains(pos)) {
            SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
            LevelSelectBack();
            return;
        }
        if (pos.Y < layout.Top - 10 * LsScale)
            return; // header

        for (int i = 0; i < LsTileCount; i++) {
            if (!TileRect(layout, i).Contains(pos))
                continue;
            SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
            if (MenuState == UIState.LevelSelect) {
                if (LevelProgress.IsCompleted(LevelSelectCampaign, i)) {
                    _lsModMission = i;
                    _lsScroll = 0;
                    MenuState = UIState.LevelModifiers;
                }
                else
                    StartLevelRun(i, null, false);
            }
            else {
                var mod = i == 0 ? null : LevelProgress.Modifiers[i - 1];
                StartLevelRun(_lsModMission, mod, true);
            }
            return;
        }
    }

    // ------------------------------------------------------------------ thumbnails

    /// <summary>Stop the preview being rendered now (it is rendered again later).</summary>
    private static void CancelBake() {
        if (_bakeIdx >= 0 && _lsThumbs[_bakeIdx] is null) {
            var rest = _bakeQueue.ToArray();
            _bakeQueue.Clear();
            _bakeQueue.Enqueue(_bakeIdx);
            foreach (var r in rest)
                _bakeQueue.Enqueue(r);
        }
        _bakeIdx = -1;
        SoundPlayer.Muted = false;
    }

    private static void BakeThumbnails() {
        if (_bakeIdx < 0) {
            if (_bakeQueue.Count == 0 || _lsCampaign is null) {
                SoundPlayer.Muted = false;
                return;
            }
            _bakeIdx = _bakeQueue.Dequeue();
            _bakeFrames = 0;
            SoundPlayer.Muted = true;
            try {
                CampaignGlobals.LoadedCampaign.LoadMission(_lsCampaign.CachedMissions[_bakeIdx]);
                CampaignGlobals.LoadedCampaign.SetupLoadedMission(true);
            }
            catch (Exception e) {
                TankGame.ClientLog.Write($"Thumbnail: couldn't load mission {_bakeIdx + 1}: {e.Message}", LogType.Warn);
                _bakeIdx = -1;
                return;
            }
            GameHandler.ResetMenuStill();
            return;
        }

        _bakeFrames++;
        // wait until the tanks are positioned and the scene has been drawn (or give up waiting and take what's there)
        if ((GameHandler.MenuStillReady && _bakeFrames > 14) || _bakeFrames > 90) {
            CaptureThumbnail(_bakeIdx);
            _bakeIdx = -1;
        }
    }

    private static void CaptureThumbnail(int i) {
        var src = TankGame.GameFrameBuffer;
        var gd = TankGame.Instance.GraphicsDevice;
        if (src is null || src.IsDisposed)
            return;
        try {
            int tw = 420, th = Math.Max(1, (int)(tw * src.Height / (float)src.Width));
            int hw = tw * 2, hh = th * 2;
            if (_thumbFinal is null || _thumbFinal.Width != tw || _thumbFinal.Height != th) {
                _thumbFinal?.Dispose();
                _thumbHalf?.Dispose();
                _thumbFinal = new RenderTarget2D(gd, tw, th);
                _thumbHalf = new RenderTarget2D(gd, hw, hh);
            }
            _thumbBatch ??= new SpriteBatch(gd);

            // two downscale passes keep edges smooth
            gd.SetRenderTarget(_thumbHalf);
            gd.Clear(Color.Black);
            _thumbBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null);
            _thumbBatch.Draw(src, new Rectangle(0, 0, hw, hh), Color.White);
            _thumbBatch.End();
            gd.SetRenderTarget(_thumbFinal);
            gd.Clear(Color.Black);
            _thumbBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, null, null);
            _thumbBatch.Draw(_thumbHalf, new Rectangle(0, 0, tw, th), Color.White);
            _thumbBatch.End();
            gd.SetRenderTarget(null);

            var data = new Color[tw * th];
            _thumbFinal!.GetData(data);
            var tex = new Texture2D(gd, tw, th);
            tex.SetData(data);
            _lsThumbs[i]?.Dispose();
            _lsThumbs[i] = tex;

            using var fs = File.Create(ThumbPath(i));
            tex.SaveAsPng(fs, tw, th);
        }
        catch (Exception e) {
            gd.SetRenderTarget(null);
            TankGame.ClientLog.Write($"Thumbnail {i + 1} failed: {e.Message}", LogType.Warn);
        }
    }

    // ------------------------------------------------------------------ drawing

    private static readonly Color LsBackground = new(26, 22, 18);
    private static readonly Color LsGreen = new(52, 199, 89);
    private static readonly Color LsGold = new(245, 190, 40);

    public static void DrawLevelSelect(SpriteBatch sb) {
        if (_lsCampaign is null)
            return;
        var layout = GetLayout();
        float s = LsScale;
        var font = FontGlobals.RebirthFont;
        var px = TextureGlobals.Pixels[Color.White];

        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);

        sb.Draw(px, WindowUtils.ScreenRect, LsBackground * 0.96f);

        bool mods = MenuState == UIState.LevelModifiers;
        Texture2D? modThumb = mods && _lsModMission >= 0 && _lsModMission < _lsThumbs.Length ? _lsThumbs[_lsModMission] : null;
        var modRec = mods ? LevelProgress.Get(LevelSelectCampaign, _lsModMission) : null;

        for (int i = 0; i < LsTileCount; i++) {
            var r = TileRect(layout, i);
            if (r.Bottom < layout.Top - 20 * s || r.Top > WindowUtils.WindowHeight)
                continue;

            DrawUtils.DrawNineSliced(sb, UIElement.UIPanelBackground, 12, r, Color.WhiteSmoke, Vector2.Zero);
            var inset = (int)(7 * s);
            var thumbRect = new Rectangle(r.X + inset, r.Y + inset, r.Width - 2 * inset, (int)layout.ThumbH - inset);
            var thumb = mods ? modThumb : _lsThumbs[i];
            if (thumb is not null)
                sb.Draw(thumb, thumbRect, Color.White);
            else {
                sb.Draw(px, thumbRect, new Color(70, 62, 52));
                DrawCentered(sb, font, "...", thumbRect.Center.ToVector2(), Color.White * 0.7f, 0.9f * s);
            }

            string label;
            bool cleared, gold = false;
            if (!mods) {
                label = $"Mission {i + 1}";
                cleared = LevelProgress.IsCompleted(LevelSelectCampaign, i);
                gold = LevelProgress.AllModifiersCleared(LevelSelectCampaign, i);
            }
            else if (i == 0) {
                label = "No modifier";
                cleared = modRec!.Completed;
            }
            else {
                var m = LevelProgress.Modifiers[i - 1];
                label = m.Name;
                cleared = modRec!.Modifiers.Contains(m.Key);
            }
            var labelPos = new Vector2(r.Center.X, thumbRect.Bottom + (r.Bottom - thumbRect.Bottom) / 2f);
            float labelScale = Math.Min(0.62f * s, (r.Width - 16 * s) / Math.Max(1f, font.MeasureString(label).X));
            DrawCentered(sb, font, label, labelPos, Color.Black, labelScale);

            float badge = 20 * s;
            var badgePos = new Vector2(thumbRect.Right - badge * 0.9f, thumbRect.Top + badge * 0.9f);
            if (gold) {
                ToggleSwitch.DrawCheckBadge(sb, badgePos - new Vector2(badge * 1.2f, 0), badge, LsGold);
                ToggleSwitch.DrawCheckBadge(sb, badgePos, badge, LsGold);
            }
            else if (cleared)
                ToggleSwitch.DrawCheckBadge(sb, badgePos, badge, LsGreen);
        }

        // header (drawn last so scrolled tiles slide under it)
        sb.Draw(px, new Rectangle(0, 0, WindowUtils.WindowWidth, (int)(layout.Top - 8 * s)), LsBackground);
        var back = LsBackRect;
        DrawUtils.DrawNineSliced(sb, UIElement.UIPanelBackground, 12, back, Color.WhiteSmoke, Vector2.Zero);
        DrawCentered(sb, font, "Back", back.Center.ToVector2(), Color.Black, 0.8f * s);

        string title = mods ? $"Mission {_lsModMission + 1}: Challenges" : "Level Select";
        DrawCentered(sb, FontGlobals.RebirthFont, title, new Vector2(WindowUtils.WindowWidth / 2f, back.Center.Y), Color.White, 1.15f * s);

        string progress;
        if (mods) {
            int done = 0;
            foreach (var m in LevelProgress.Modifiers)
                if (modRec!.Modifiers.Contains(m.Key))
                    done++;
            progress = $"{done}/{LevelProgress.Modifiers.Length} modifiers";
        }
        else
            progress = $"{LevelProgress.CompletedCount(LevelSelectCampaign)}/{_lsThumbs.Length} cleared";
        var pm = font.MeasureString(progress) * 0.75f * s;
        sb.DrawString(font, progress, new Vector2(WindowUtils.WindowWidth - 36 * s - pm.X, back.Center.Y - pm.Y / 2f), Color.White * 0.85f, new Vector2(0.75f * s));

        if (_bakeIdx >= 0 || _bakeQueue.Count > 0) {
            string baking = $"Preparing previews... {_lsThumbs.Length - _bakeQueue.Count}/{_lsThumbs.Length}";
            var bm = font.MeasureString(baking) * 0.5f * s;
            sb.DrawString(font, baking, new Vector2(WindowUtils.WindowWidth - 36 * s - bm.X, back.Bottom + 4 * s), Color.White * 0.6f, new Vector2(0.5f * s));
        }

        sb.End();
        sb.Begin();
    }

    private static void DrawCentered(SpriteBatch sb, SpriteFontBase font, string text, Vector2 center, Color color, float scale) {
        var m = font.MeasureString(text);
        sb.DrawString(font, text, center, color, new Vector2(scale), 0f, m / 2f);
    }
}
