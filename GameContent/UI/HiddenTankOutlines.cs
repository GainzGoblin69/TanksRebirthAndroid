using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;

namespace TanksRebirth.GameContent.UI;

/// <summary>
/// Tanks hidden behind blocks (from the camera's point of view) get a faint dotted outline, so you can still tell
/// where they are. Invisible tanks stay invisible.
/// </summary>
public static class HiddenTankOutlines {
    private static readonly List<BoundingBox> _boxes = [];

    public static void Draw(SpriteBatch sb) {
        if (MainMenu.MainMenuUI.IsActive || LevelEditor.LevelEditorUI.IsActive || CameraGlobals.IsUsingFirstPresonCamera)
            return;
        if (IntermissionSystem.BlackAlpha > 0.5f)
            return;
        // the blocks that can hide something (solid and taller than the floor)
        _boxes.Clear();
        float h = Block.SIDE_LENGTH / 2f;
        foreach (var b in Block.AllBlocks) {
            if (b is null || !b.Properties.IsSolid || b.HeightFromGround < 4f)
                continue;
            _boxes.Add(new BoundingBox(new Vector3(b.Position.X - h, 0, b.Position.Y - h), new Vector3(b.Position.X + h, b.HeightFromGround, b.Position.Y + h)));
        }
        if (_boxes.Count == 0)
            return;

        var vp = TankGame.Instance.GraphicsDevice.Viewport;
        var view = CameraGlobals.GameView;
        var proj = CameraGlobals.GameProjection;
        bool begun = false;
        foreach (var tank in GameHandler.AllTanks) {
            if (tank is null || tank.IsDestroyed)
                continue;
            if (tank.Properties.Invisible && tank is not PlayerTank)
                continue;
            float sc = tank.Scaling.X;
            var center = tank.Position3D + new Vector3(0, 8 * sc, 0);
            // which way is the camera from here (works for both the overhead and the perspective camera)
            var sp = vp.Project(center, proj, view, Matrix.Identity);
            if (sp.Z is < 0 or > 1)
                continue;
            var near = vp.Unproject(new Vector3(sp.X, sp.Y, 0f), proj, view, Matrix.Identity);
            var toCam = near - center;
            if (toCam.LengthSquared() < 1e-4f)
                continue;
            toCam.Normalize();
            // hidden when the middle and the top of the tank are both behind a block
            if (!Blocked(center, toCam) || !Blocked(tank.Position3D + new Vector3(0, 15 * sc, 0), toCam))
                continue;
            if (!begun) {
                sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
                begun = true;
            }
            DrawOutline(sb, tank, vp, view, proj, ColorOf(tank));
        }
        if (begun)
            sb.End();
    }

    private static bool Blocked(Vector3 from, Vector3 toCam) {
        var ray = new Ray(from, toCam);
        foreach (var box in _boxes) {
            if (box.Contains(from) == ContainmentType.Contains)
                continue;
            if (ray.Intersects(box) is float d && d > 0.5f)
                return true;
        }
        return false;
    }

    private static Color ColorOf(Tank tank) {
        if (tank is PlayerTank pt) {
            int ci = PlayerColors.ColorIndexFor(pt.PlayerId);
            if (ci >= 0 && ci < PlayerColors.Count)
                return PlayerColors.MainOf(ci);
            return PlayerID.PlayerTankColors.TryGetValue(pt.PlayerId, out var c) ? c : Color.White;
        }
        if (tank is AITank ai && AITank.TankDestructionColors.TryGetValue(ai.AiTankType, out var dc))
            return dc;
        return Color.White;
    }

    /// <summary>A dotted box around the tank's body and a dotted barrel, faint.</summary>
    private static void DrawOutline(SpriteBatch sb, Tank tank, Viewport vp, Matrix view, Matrix proj, Color color) {
        float sc = tank.Scaling.X;
        float halfL = 13f * sc, halfW = 10f * sc, top = 12f * sc;
        var fwd = new Vector2(0, 1).Rotate(-tank.ChassisRotation);
        var side = new Vector2(-fwd.Y, fwd.X);
        var p = tank.Position;
        Vector2 S(Vector2 xz, float y) {
            var v = vp.Project(new Vector3(xz.X, y, xz.Y), proj, view, Matrix.Identity);
            return new Vector2(v.X, v.Y);
        }
        var corners = new[] { p + fwd * halfL + side * halfW, p + fwd * halfL - side * halfW, p - fwd * halfL - side * halfW, p - fwd * halfL + side * halfW };
        float pulse = 0.55f + 0.15f * MathF.Sin((float)RuntimeData.RunTime / 60f * 4f);
        var col = Color.Lerp(color, Color.White, 0.35f) * pulse;
        float thick = Math.Max(2f, WindowUtilsHeight() / 1080f * 3f);
        for (int i = 0; i < 4; i++) {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            Dotted(sb, S(a, 0), S(b, 0), thick, col);
            Dotted(sb, S(a, top), S(b, top), thick, col);
            Dotted(sb, S(a, 0), S(a, top), thick, col);
        }
        // the barrel, pointing where the turret aims
        var aim = tank.TurretPosition - tank.Position;
        if (aim.LengthSquared() > 0.01f) {
            aim.Normalize();
            Dotted(sb, S(p, top + 2 * sc), S(p + aim * 20f * sc, top + 2 * sc), thick, col);
        }
    }

    private static float WindowUtilsHeight() => Internals.Common.Utilities.WindowUtils.WindowHeight;

    private static void Dotted(SpriteBatch sb, Vector2 a, Vector2 b, float thick, Color col) {
        float len = Vector2.Distance(a, b);
        if (len < 1f)
            return;
        float dot = thick * 2.2f, gap = thick * 2.4f;
        var dir = (b - a) / len;
        for (float d = 0; d < len; d += dot + gap)
            ToggleSwitch.DrawLine(sb, a + dir * d, a + dir * MathF.Min(len, d + dot), thick, col);
    }
}
