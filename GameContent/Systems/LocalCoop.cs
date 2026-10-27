using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.Internals;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Framework.Input;
using TanksRebirth.Internals.Common.Utilities;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// Same-device co-op: two players on one phone. Either player 1 uses the touch controls and player 2 a
/// controller, or both use controllers (the touch controls are hidden). Controller players drive with the left
/// stick (or d-pad), move their own aim cursor with the right stick, shoot with RB (or RT) and lay mines with
/// LB (or LT / A).
/// </summary>
public static class LocalCoop {
    public enum Scheme { TouchAndController, TwoControllers }

    public static bool Active { get; private set; }
    public static Scheme Mode { get; private set; }
    public const int PlayerCount = 2;

    private sealed class Slot {
        public bool Touch;
        public PlayerIndex Pad;
        public GamePadState Cur, Old;
        public Vector2 Cursor = new(-1);
    }

    private static readonly Slot[] _slots = [new(), new()];
    private static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _last;

    public static void Start(Scheme scheme) {
        Active = true;
        Mode = scheme;
        _slots[0] = scheme == Scheme.TouchAndController ? new Slot { Touch = true } : new Slot { Pad = PlayerIndex.One };
        _slots[1] = new Slot { Pad = scheme == Scheme.TouchAndController ? PlayerIndex.One : PlayerIndex.Two };
    }

    public static void Stop() => Active = false;

    /// <summary>Both players on controllers: no on-screen joystick or buttons.</summary>
    public static bool HidesTouch => Active && Mode == Scheme.TwoControllers;

    /// <summary>Whether this player tank is driven by local co-op input (instead of the normal single-player input).</summary>
    public static bool ControlsPlayer(int playerId) => Active && playerId >= 0 && playerId < PlayerCount;

    public static bool UsesTouch(int playerId) => ControlsPlayer(playerId) && _slots[playerId].Touch;

    public static bool IsPadConnected(PlayerIndex i) {
        try { return GamePad.GetState(i).IsConnected; }
        catch { return false; }
    }

    /// <summary>How many controllers are connected right now (0-2 checked).</summary>
    public static int ConnectedPads => (IsPadConnected(PlayerIndex.One) ? 1 : 0) + (IsPadConnected(PlayerIndex.Two) ? 1 : 0);

    /// <summary>Called once per frame from input polling.</summary>
    public static void Update() {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = (float)Math.Clamp(now - _last, 0, 0.1);
        _last = now;
        if (!Active)
            return;

        int w = WindowUtils.WindowWidth, h = WindowUtils.WindowHeight;
        for (int i = 0; i < PlayerCount; i++) {
            var s = _slots[i];
            if (s.Touch)
                continue;
            s.Old = s.Cur;
            try { s.Cur = GamePad.GetState(s.Pad); }
            catch { s.Cur = default; }

            if (s.Cursor.X < 0)
                s.Cursor = new Vector2(w * (i == 0 ? 0.4f : 0.6f), h * 0.5f);
            var stick = s.Cur.ThumbSticks.Right;
            if (stick.Length() > 0.15f)
                s.Cursor += new Vector2(stick.X, -stick.Y) * TouchControls.CursorSpeed * h * dt;
            s.Cursor = Vector2.Clamp(s.Cursor, Vector2.Zero, new Vector2(w - 1, h - 1));

            // player 2's Start pauses too (player 1's pad already does)
            if (s.Pad != PlayerIndex.One && s.Cur.IsButtonDown(Buttons.Start) && !s.Old.IsButtonDown(Buttons.Start))
                TouchControls.RequestPause();
        }
    }

    private static bool Just(Slot s, Buttons b) => s.Cur.IsButtonDown(b) && !s.Old.IsButtonDown(b);

    /// <summary>Drive direction in screen space (+Y down), zero when idle.</summary>
    public static Vector2 Move(int playerId) {
        var s = _slots[playerId];
        if (s.Touch) {
            var move = TouchControls.MoveVector;
            return move;
        }
        var ls = s.Cur.ThumbSticks.Left;
        var v = new Vector2(ls.X, -ls.Y);
        if (s.Cur.DPad.Left == ButtonState.Pressed) v.X = -1;
        if (s.Cur.DPad.Right == ButtonState.Pressed) v.X = 1;
        if (s.Cur.DPad.Up == ButtonState.Pressed) v.Y = -1;
        if (s.Cur.DPad.Down == ButtonState.Pressed) v.Y = 1;
        return v.Length() > 0.2f ? v : Vector2.Zero;
    }

    public static bool ShootPressed(int playerId) {
        var s = _slots[playerId];
        if (s.Touch)
            return false; // touch shooting comes through the tap (synthesized mouse click)
        return Just(s, Buttons.RightShoulder) || (s.Cur.Triggers.Right > 0.5f && s.Old.Triggers.Right <= 0.5f);
    }

    public static bool MinePressed(int playerId) {
        var s = _slots[playerId];
        if (s.Touch)
            return TouchControls.MineJustPressed;
        return Just(s, Buttons.LeftShoulder) || Just(s, Buttons.A) || (s.Cur.Triggers.Left > 0.5f && s.Old.Triggers.Left <= 0.5f);
    }

    /// <summary>Use a missile upgrade: Y on a controller, the MISSILE button for the touch player.</summary>
    public static bool MissilePressed(int playerId) {
        var s = _slots[playerId];
        return s.Touch ? TouchControls.MissileJustPressed : Just(s, Buttons.Y);
    }

    /// <summary>Where this player is aiming, in screen pixels.</summary>
    public static Vector2 AimScreen(int playerId) {
        var s = _slots[playerId];
        return s.Touch ? MouseUtils.MousePosition : s.Cursor;
    }

    /// <summary>Aim dots and a crosshair (in the player's color) for each controller player.</summary>
    public static void DrawAim(SpriteBatch sb) {
        if (!Active || !TouchControls.GameplayControlsActive)
            return;
        var dot = GameResources.GetGameResource<Texture2D>("Assets/textures/misc/mouse_dot");
        var cursor = GameResources.GetGameResource<Texture2D>("Assets/textures/misc/cursor_1");
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
        for (int i = 0; i < PlayerCount; i++) {
            var tank = GameHandler.AllPlayerTanks[i];
            if (tank is null || tank.IsDestroyed)
                continue;
            var aim = AimScreen(i);
            var tankPos = MatrixUtils.ConvertWorldToScreen(new Vector3(0, 11, 0), tank.World, CameraGlobals.GameView, CameraGlobals.GameProjection);
            var col = PlayerColors.Palette[PlayerColors.GetChoice(i)].Main;
            for (int d = 1; d < 10; d++)
                sb.Draw(dot, Vector2.Lerp(tankPos, aim, d / 10f), null, Color.Lerp(Color.White, col, 0.5f), 0f, dot.Size() / 2f, new Vector2(0.35f).ToResolution(), default, default);
            if (!_slots[i].Touch)
                sb.Draw(cursor, aim, null, col, 0f, cursor.Size() / 2f, new Vector2(1f).ToResolution(), default, default);
        }
        sb.End();
    }
}
