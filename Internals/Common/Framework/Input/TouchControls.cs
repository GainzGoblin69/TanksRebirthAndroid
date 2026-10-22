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
/// <para>While playing: a floating joystick drives the tank, and tapping the other side of the screen aims the
/// turret at that spot and fires. The joystick lives on one side ("home"), left by default. With the joystick
/// idle, a touch on the other side is a shot if it's a quick tap, but if it's held or dragged it becomes the
/// joystick and the joystick's home moves to that side (the mine button moves to the opposite corner), so you
/// can drive with your right thumb and shoot on the left.</para>
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

    /// <summary>True when a second-screen control panel (e.g. the AYN Thor's lower screen) is showing; the
    /// on-screen pause button then lives there instead of on the game screen.</summary>
    public static bool SecondScreenActive { get; set; }
    private static bool _pauseRequested, _mineRequested;
    /// <summary>Called by the second-screen panel's buttons (on the UI thread, same as the game loop).</summary>
    public static void RequestPause() => _pauseRequested = true;
    public static void RequestMine() => _mineRequested = true;

    /// <summary>Which side the joystick currently lives on.</summary>
    public static bool StickOnRight { get; private set; }

    /// <summary>Fraction of the screen width, measured from the joystick's side, where a touch grabs the joystick.</summary>
    public static float JoystickZone { get; set; } = 0.45f;
    public static float DeadZone { get; set; } = 0.2f;
    /// <summary>How long a touch on the shooting side must be held (seconds) before it becomes the joystick.</summary>
    public static float HoldToMoveTime { get; set; } = 0.2f;

    private static int _stickId = -1;
    private static Vector2 _stickOrigin;
    private static Vector2 _stickPos;

    // a touch on the shooting side while the joystick is idle: a tap (shoot) or the start of a hold (move)?
    private static int _pendingId = -1;
    private static Vector2 _pendingStart;
    private static Vector2 _pendingPos;
    private static DateTime _pendingSince;

    private static int _pointerId = -1;
    private static Vector2 _pointerPos;
    private static bool _pointerDown;
    private static bool _pointerRetap;
    private static bool _tapPressThisFrame;

    private static readonly HashSet<int> _buttonTouches = [];
    private static readonly HashSet<int> _seen = [];

    // ---- controller (e.g. AYN Thor, Bluetooth pads) ----
    /// <summary>True after the controller was last used (touch controls hide); a screen touch turns it off.</summary>
    public static bool GamepadMode { get; private set; }
    /// <summary>On-screen speed of the aim cursor at full right-stick tilt, in screen heights per second.</summary>
    public static float CursorSpeed { get; set; } = 1.4f;
    private static Vector2 _padCursor = new(-1);
    private static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _lastTime;
    private static bool _padClick;

    private static Texture2D? _disc;
    private static Texture2D? _ring;

    private static float UiScale => Math.Max(WindowUtils.WindowHeight, 1) / 1080f;
    private static float StickRadius => 170f * UiScale;
    private static Vector2 RestingStickCenter => new(
        StickOnRight ? WindowUtils.WindowWidth - StickRadius * 1.6f : StickRadius * 1.6f,
        WindowUtils.WindowHeight - StickRadius * 1.6f);
    private static float ButtonRadius => 85f * UiScale;
    // the mine button sits along the top edge, on the side opposite the joystick (clear of the HUD)
    private static Vector2 MineButtonCenter => new(
        StickOnRight ? WindowUtils.WindowWidth * 0.19f : WindowUtils.WindowWidth * 0.83f,
        ButtonRadius * 1.05f);
    private static Vector2 PauseButtonCenter => new(ButtonRadius * 0.9f, ButtonRadius * 0.9f);
    private static float PauseButtonRadius => ButtonRadius * 0.65f;

    private static bool InStickZone(Vector2 pos) => StickOnRight
        ? pos.X > WindowUtils.WindowWidth * (1f - JoystickZone)
        : pos.X < WindowUtils.WindowWidth * JoystickZone;

    /// <summary>True while a mission is being played (or is between missions) and no menu is covering the field.</summary>
    public static bool GameplayControlsActive =>
        !MainMenuUI.IsActive
        && !LevelEditorUI.IsActive
        && !TanksRebirth.GameContent.UI.GameUI.Paused
        && !CampaignCompleteUI.IsViewingResults
        && !ChatSystem.ActiveHandle
        && !TankGame.IsCrashInfoVisible;

    private static void GrabStick(int id, Vector2 origin, Vector2 pos) {
        _stickId = id;
        // keep the whole base on screen so the knob can travel in every direction
        _stickOrigin = new Vector2(
            MathHelper.Clamp(origin.X, StickRadius, WindowUtils.WindowWidth - StickRadius),
            MathHelper.Clamp(origin.Y, StickRadius, WindowUtils.WindowHeight - StickRadius));
        _stickPos = pos;
    }

    public static void Update() {
        MineJustPressed = _mineRequested && GameplayControlsActive;
        PauseJustPressed = _pauseRequested;
        _mineRequested = _pauseRequested = false;
        _pointerRetap = false;
        _tapPressThisFrame = false;

        bool gameplay = GameplayControlsActive;
        if (!gameplay) {
            _stickId = -1;
            _pendingId = -1;
        }

        _seen.Clear();
        var touches = TouchPanel.GetState();
        if (touches.Count > 0)
            GamepadMode = false;

        foreach (var touch in touches) {
            var pos = touch.Position;
            bool isEnding = touch.State is TouchLocationState.Released or TouchLocationState.Invalid;
            if (!isEnding)
                _seen.Add(touch.Id);

            if (touch.State == TouchLocationState.Pressed) {
                if (gameplay && !SecondScreenActive && Vector2.Distance(pos, PauseButtonCenter) <= PauseButtonRadius * 1.3f) {
                    PauseJustPressed = true;
                    _buttonTouches.Add(touch.Id);
                    continue;
                }
                if (gameplay && Vector2.Distance(pos, MineButtonCenter) <= ButtonRadius * 1.1f) {
                    MineJustPressed = true;
                    _buttonTouches.Add(touch.Id);
                    continue;
                }
                if (gameplay && _stickId == -1) {
                    if (InStickZone(pos)) {
                        GrabStick(touch.Id, pos, pos);
                        continue;
                    }
                    if (_pendingId == -1) {
                        // joystick idle, touch on the shooting side: wait to see if it's a tap or a hold
                        _pendingId = touch.Id;
                        _pendingStart = _pendingPos = pos;
                        _pendingSince = DateTime.UtcNow;
                        continue;
                    }
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
            else if (isEnding) {
                if (touch.Id == _pendingId) {
                    // lifted quickly: it was a tap, so shoot there
                    _pendingId = -1;
                    if (_pointerId == -1) {
                        _pointerPos = _pendingStart;
                        _tapPressThisFrame = true;
                    }
                }
            }
            else {
                if (touch.Id == _stickId) {
                    _stickPos = pos;
                    // let the base follow the finger when dragged past the edge, so direction changes stay quick
                    var off = _stickPos - _stickOrigin;
                    if (off.Length() > StickRadius)
                        _stickOrigin = _stickPos - Vector2.Normalize(off) * StickRadius;
                }
                else if (touch.Id == _pendingId) {
                    _pendingPos = pos;
                }
                else if (touch.Id == _pointerId) {
                    _pointerPos = pos;
                }
            }
        }

        // a pending touch that is held or dragged becomes the joystick, and the joystick moves to that side
        if (_pendingId != -1) {
            if (!_seen.Contains(_pendingId))
                _pendingId = -1;
            else if (_stickId == -1 && ((DateTime.UtcNow - _pendingSince).TotalSeconds >= HoldToMoveTime
                     || Vector2.Distance(_pendingPos, _pendingStart) > StickRadius * 0.25f)) {
                StickOnRight = _pendingStart.X > WindowUtils.WindowWidth / 2f;
                GrabStick(_pendingId, _pendingStart, _pendingPos);
                _pendingId = -1;
            }
        }

        // drop fingers that lifted
        if (_stickId != -1 && !_seen.Contains(_stickId))
            _stickId = -1;
        if (_pointerId != -1 && !_seen.Contains(_pointerId))
            _pointerId = -1;
        _buttonTouches.RemoveWhere(id => !_seen.Contains(id));

        _pointerDown = (_pointerId != -1 && !_pointerRetap) || _tapPressThisFrame;

        if (_stickId != -1) {
            var v = (_stickPos - _stickOrigin) / StickRadius;
            if (v.Length() > 1f)
                v.Normalize();
            MoveVector = v.Length() < DeadZone ? Vector2.Zero : v;
        }
        else
            MoveVector = Vector2.Zero;
    }

    /// <summary>
    /// Controller input: the right stick (or either stick in menus) moves an on-screen cursor the turret aims at.
    /// In menus, A (or RB) clicks at the cursor. Shooting and mines in missions go through PlayerTank's
    /// FireBullet/PlaceMine binds (RB/LB on Android).
    /// </summary>
    public static void UpdateGamepad(GamePadState pad) {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _lastTime, 0, 0.1);
        _lastTime = now;
        _padClick = false;

        if (!pad.IsConnected)
            return;
        if (IsPadActive(pad))
            GamepadMode = true;
        if (!GamepadMode)
            return;

        int w = WindowUtils.WindowWidth, h = WindowUtils.WindowHeight;
        if (_padCursor.X < 0)
            _padCursor = new Vector2(w / 2f, h / 2f);

        bool gameplay = GameplayControlsActive;
        var stick = pad.ThumbSticks.Right;
        if (!gameplay && stick.Length() < 0.2f)
            stick = pad.ThumbSticks.Left; // menus: either stick moves the cursor
        if (stick.Length() > 0.15f)
            _padCursor += new Vector2(stick.X, -stick.Y) * CursorSpeed * h * dt;
        _padCursor = Vector2.Clamp(_padCursor, Vector2.Zero, new Vector2(w - 1, h - 1));

        _pointerPos = _padCursor;
        _padClick = !gameplay && (pad.IsButtonDown(Buttons.A) || pad.IsButtonDown(Buttons.RightShoulder));
    }

    /// <summary>Whether the controller should count as "in use" this frame.</summary>
    private static bool IsPadActive(GamePadState s) =>
        s.Buttons.A == ButtonState.Pressed || s.Buttons.B == ButtonState.Pressed
        || s.Buttons.X == ButtonState.Pressed || s.Buttons.Y == ButtonState.Pressed
        || s.Buttons.Start == ButtonState.Pressed
        || s.Buttons.LeftShoulder == ButtonState.Pressed || s.Buttons.RightShoulder == ButtonState.Pressed
        || s.DPad.Up == ButtonState.Pressed || s.DPad.Down == ButtonState.Pressed
        || s.DPad.Left == ButtonState.Pressed || s.DPad.Right == ButtonState.Pressed
        || s.ThumbSticks.Left.Length() > 0.25f || s.ThumbSticks.Right.Length() > 0.25f
        || s.Triggers.Left > 0.3f || s.Triggers.Right > 0.3f;

    /// <summary>A mouse state built from the "pointer" finger: its position, and the left button held while it touches.</summary>
    public static MouseState GetMouseState(MouseState previous) =>
        new((int)_pointerPos.X, (int)_pointerPos.Y, previous.ScrollWheelValue,
            _pointerDown || _padClick ? ButtonState.Pressed : ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    public static Vector2 PointerPosition => _pointerPos;

    public static void Draw(SpriteBatch sb) {
        if (!Enabled || !GameplayControlsActive || GamepadMode)
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

        // pause button (two bars); lives on the second screen when there is one
        if (!SecondScreenActive)
            DrawCircle(sb, _disc!, PauseButtonCenter, PauseButtonRadius, Color.Black * 0.3f);
        if (!SecondScreenActive)
            DrawCircle(sb, _ring!, PauseButtonCenter, PauseButtonRadius, Color.White * 0.5f);
        if (!SecondScreenActive && TextureGlobals.Pixels.TryGetValue(Color.White, out var pixel)) {
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
