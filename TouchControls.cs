using System;
using System.Collections.Generic;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using TanksRebirth.GameContent;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.UI;
using TanksRebirth.GameContent.UI.LevelEditor;
using TanksRebirth.GameContent.UI.MainMenu;
using TanksRebirth.Internals.Common.Utilities;

namespace TanksRebirth.Internals.Common.Framework.Input;

/// <summary>
/// On-screen touch controls for phones and tablets.
/// <para>While playing: a floating joystick on the left side of the screen drives the tank, and tapping anywhere
/// on the right side aims the turret at that spot and fires. Small buttons lay mines and pause.</para>
/// <para>Everywhere else (menus, pause screen, results) the first finger simply acts as the mouse, so the
/// existing mouse-driven UI keeps working.</para>
/// </summary>
public static class TouchControls {
    /// <summary>Turned on by the Android launcher. Off on desktop, so nothing changes there.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Joystick direction in screen space (-1..1 on each axis, +Y is down). Zero inside the dead zone.</summary>
    public static Vector2 MoveVector { get; private set; }
    public static bool MineJustPressed { get; private set; }
    public static bool PauseJustPressed { get; private set; }

    /// <summary>Fraction of the screen width (from the left) where a new touch grabs the joystick.</summary>
    public static float JoystickZone { get; set; } = 0.45f;
    public static float DeadZone { get; set; } = 0.2f;

    private static int _stickId = -1;
    private static Vector2 _stickOrigin;
    private static Vector2 _stickPos;

    private static int _pointerId = -1;
    private static Vector2 _pointerPos;
    private static bool _pointerDown;
    private static bool _pointerRetap;

    private static readonly HashSet<int> _buttonTouches = [];
    private static readonly HashSet<int> _seen = [];

    private static Texture2D? _disc;
    private static Texture2D? _ring;

    private static float UiScale => Math.Max(WindowUtils.WindowHeight, 1) / 1080f;
    private static float StickRadius => 170f * UiScale;
    private static Vector2 RestingStickCenter => new(StickRadius * 1.6f, WindowUtils.WindowHeight - StickRadius * 1.6f);
    private static float ButtonRadius => 85f * UiScale;
    private static Vector2 MineButtonCenter => new(WindowUtils.WindowWidth - ButtonRadius * 1.5f, WindowUtils.WindowHeight - ButtonRadius * 1.5f);
    private static Vector2 PauseButtonCenter => new(ButtonRadius * 0.9f, ButtonRadius * 0.9f);
    private static float PauseButtonRadius => ButtonRadius * 0.65f;

    /// <summary>True while a mission is being played (or is between missions) and no menu is covering the field.</summary>
    public static bool GameplayControlsActive =>
        !MainMenuUI.IsActive
        && !LevelEditorUI.IsActive
        && !TanksRebirth.GameContent.UI.GameUI.Paused
        && !CampaignCompleteUI.IsViewingResults
        && !ChatSystem.ActiveHandle
        && !TankGame.IsCrashInfoVisible;

    public static void Update() {
        MineJustPressed = false;
        PauseJustPressed = false;
        _pointerRetap = false;

        bool gameplay = GameplayControlsActive;
        if (!gameplay)
            _stickId = -1;

        _seen.Clear();
        var touches = TouchPanel.GetState();

        foreach (var touch in touches) {
            var pos = touch.Position;
            bool isEnding = touch.State is TouchLocationState.Released or TouchLocationState.Invalid;
            if (!isEnding)
                _seen.Add(touch.Id);

            if (touch.State == TouchLocationState.Pressed) {
                if (gameplay && Vector2.Distance(pos, PauseButtonCenter) <= PauseButtonRadius * 1.3f) {
                    PauseJustPressed = true;
                    _buttonTouches.Add(touch.Id);
                    continue;
                }
                if (gameplay && Vector2.Distance(pos, MineButtonCenter) <= ButtonRadius * 1.1f) {
                    MineJustPressed = true;
                    _buttonTouches.Add(touch.Id);
                    continue;
                }
                if (gameplay && _stickId == -1 && pos.X < WindowUtils.WindowWidth * JoystickZone) {
                    _stickId = touch.Id;
                    // keep the whole base on screen so the knob can travel in every direction
                    _stickOrigin = new Vector2(
                        MathHelper.Clamp(pos.X, StickRadius, WindowUtils.WindowWidth - StickRadius),
                        MathHelper.Clamp(pos.Y, StickRadius, WindowUtils.WindowHeight - StickRadius));
                    _stickPos = pos;
                    continue;
                }
                if (_pointerId == -1) {
                    _pointerId = touch.Id;
                    _pointerPos = pos;
                }
                else if (gameplay) {
                    // another finger tapped while one is still down: aim there and let go for one frame,
                    // so the next frame registers as a fresh click (a new shot)
                    _pointerId = touch.Id;
                    _pointerPos = pos;
                    _pointerRetap = true;
                }
            }
            else if (!isEnding) {
                if (touch.Id == _stickId) {
                    _stickPos = pos;
                    // let the base follow the finger when dragged past the edge, so direction changes stay quick
                    var off = _stickPos - _stickOrigin;
                    if (off.Length() > StickRadius)
                        _stickOrigin = _stickPos - Vector2.Normalize(off) * StickRadius;
                }
                else if (touch.Id == _pointerId) {
                    _pointerPos = pos;
                }
            }
        }

        // drop fingers that lifted
        if (_stickId != -1 && !_seen.Contains(_stickId))
            _stickId = -1;
        if (_pointerId != -1 && !_seen.Contains(_pointerId))
            _pointerId = -1;
        _buttonTouches.RemoveWhere(id => !_seen.Contains(id));

        _pointerDown = _pointerId != -1 && !_pointerRetap;

        if (_stickId != -1) {
            var v = (_stickPos - _stickOrigin) / StickRadius;
            if (v.Length() > 1f)
                v.Normalize();
            MoveVector = v.Length() < DeadZone ? Vector2.Zero : v;
        }
        else
            MoveVector = Vector2.Zero;
    }

    /// <summary>A mouse state built from the "pointer" finger: its position, and the left button held while it touches.</summary>
    public static MouseState GetMouseState(MouseState previous) =>
        new((int)_pointerPos.X, (int)_pointerPos.Y, previous.ScrollWheelValue,
            _pointerDown ? ButtonState.Pressed : ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    public static Vector2 PointerPosition => _pointerPos;

    public static void Draw(SpriteBatch sb) {
        if (!Enabled || !GameplayControlsActive)
            return;

        EnsureTextures(sb.GraphicsDevice);

        sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

        // joystick
        bool held = _stickId != -1;
        var baseCenter = held ? _stickOrigin : RestingStickCenter;
        var knobCenter = baseCenter;
        if (held) {
            var off = _stickPos - _stickOrigin;
            if (off.Length() > StickRadius)
                off = Vector2.Normalize(off) * StickRadius;
            knobCenter += off;
        }
        DrawCircle(sb, _disc!, baseCenter, StickRadius, Color.Black * (held ? 0.25f : 0.15f));
        DrawCircle(sb, _ring!, baseCenter, StickRadius, Color.White * (held ? 0.55f : 0.35f));
        DrawCircle(sb, _disc!, knobCenter, StickRadius * 0.42f, Color.White * (held ? 0.6f : 0.35f));

        // mine button
        DrawCircle(sb, _disc!, MineButtonCenter, ButtonRadius, Color.Black * 0.3f);
        DrawCircle(sb, _ring!, MineButtonCenter, ButtonRadius, Color.White * 0.5f);
        DrawLabel(sb, "MINE", MineButtonCenter, 0.55f);

        // pause button (two bars)
        DrawCircle(sb, _disc!, PauseButtonCenter, PauseButtonRadius, Color.Black * 0.3f);
        DrawCircle(sb, _ring!, PauseButtonCenter, PauseButtonRadius, Color.White * 0.5f);
        if (TextureGlobals.Pixels.TryGetValue(Color.White, out var pixel)) {
            var barSize = new Vector2(PauseButtonRadius * 0.22f, PauseButtonRadius * 0.8f);
            var gap = PauseButtonRadius * 0.18f;
            sb.Draw(pixel, new Rectangle((int)(PauseButtonCenter.X - gap - barSize.X), (int)(PauseButtonCenter.Y - barSize.Y / 2), (int)barSize.X, (int)barSize.Y), Color.White * 0.8f);
            sb.Draw(pixel, new Rectangle((int)(PauseButtonCenter.X + gap), (int)(PauseButtonCenter.Y - barSize.Y / 2), (int)barSize.X, (int)barSize.Y), Color.White * 0.8f);
        }

        sb.End();
    }

    private static void DrawCircle(SpriteBatch sb, Texture2D tex, Vector2 center, float radius, Color color) {
        var scale = radius * 2f / tex.Width;
        sb.Draw(tex, center, null, color, 0f, new Vector2(tex.Width / 2f, tex.Height / 2f), scale, SpriteEffects.None, 0f);
    }

    private static void DrawLabel(SpriteBatch sb, string text, Vector2 center, float scale) {
        var font = FontGlobals.RebirthFont;
        if (font is null)
            return;
        var s = scale * UiScale;
        var size = font.MeasureString(text) * s;
        sb.DrawString(font, text, center - size / 2f, Color.White * 0.85f, new Vector2(s));
    }

    private static void EnsureTextures(GraphicsDevice gd) {
        if (_disc is not null && !_disc.IsDisposed && _ring is not null && !_ring.IsDisposed)
            return;

        const int size = 256;
        const float r = size / 2f;
        var disc = new Color[size * size];
        var ring = new Color[size * size];
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r));
                // soft 2px edge; premultiplied alpha for BlendState.AlphaBlend
                float discA = MathHelper.Clamp(r - 1f - d, 0f, 1f);
                float ringA = MathHelper.Clamp(r - 1f - d, 0f, 1f) * MathHelper.Clamp(d - (r - 11f), 0f, 1f);
                disc[y * size + x] = new Color(discA, discA, discA, discA);
                ring[y * size + x] = new Color(ringA, ringA, ringA, ringA);
            }
        }
        _disc = new Texture2D(gd, size, size);
        _disc.SetData(disc);
        _ring = new Texture2D(gd, size, size);
        _ring.SetData(ring);
    }
}
