using System;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Graphics.DynamicLighting;

namespace TanksRebirth.GameContent.Systems.Troves;

/// <summary>
/// Tank Troves' own light sources, added to each frame's lights: items waiting to be picked up glow in their color,
/// fires flicker, lightning flashes, mortar shells glow as they fall, lit fuses pulse red, nests and bosses glow, and so
/// on. They're soft glows without shadows, at a low priority, so when a room is busy the important lights win.
/// </summary>
public static class TroveLights {
    private static double Now => TroveRun.PlayTime;
    private static Vector3 At(Vector2 p, float height) => new(p.X, height, p.Y);
    private static float Pulse(float speed, float phase = 0f) => 0.5f + 0.5f * MathF.Sin((float)Now * speed + phase);

    /// <summary>Called while a frame's lights are collected (in a room, during play).</summary>
    public static void AddFrameLights() {
        if (!TroveRun.Active || !CampaignGlobals.InMission)
            return;
        float now = (float)Now;

        // ---- items lying around: the room's prize on its pedestal, and anything dropped on the floor
        if (TroveRun.Room is { } room) {
            // (only once it's showing: a fight room's prize stays hidden until the room is cleared)
            if (room.PickupVisible && room.Pickup is { } prize)
                GameplayLights.AddGlow(At(TroveRun.PickupPoint, 14), TroveItems.Def(prize).Color, 1.1f + 0.3f * Pulse(2.4f), 80f, 3);
            foreach (var g in room.GroundDrops)
                if (!g.Taken)
                    GameplayLights.AddGlow(At(g.At, 9), TroveItems.Def(g.Item).Color, 0.9f + 0.2f * Pulse(3f, g.At.X), 55f, 2);
        }

        // ---- the players' items at work
        foreach (var b in TroveItems.Bolts) {
            // lightning: both ends flare for a moment
            float age = (float)(Now - b.Time);
            if (age is < 0f or > 0.3f)
                continue;
            float k = 1f - age / 0.3f;
            var c = new Color(170, 215, 255);
            GameplayLights.AddGlow(At(b.To, 12), c, 2.4f * k, 95f, 4);
            GameplayLights.AddGlow(At(Vector2.Lerp(b.From, b.To, 0.5f), 12), c, 1.5f * k, 80f, 3);
        }
        foreach (var f in TroveItems.Fires)
            if (Now < f.Until)
                GameplayLights.AddGlow(At(f.At, 8), new Color(255, 130, 40), 1.1f + 0.35f * Pulse(23f, f.At.X), 70f, 2);
        foreach (var w in TroveItems.Wells)
            if (Now < w.Until)
                GameplayLights.AddGlow(At(w.At, 10), new Color(170, 90, 255), 1.2f + 0.3f * Pulse(5f), 90f, 2);
        foreach (var f in TroveItems.Flags)
            if (Now < f.Until)
                GameplayLights.AddGlow(At(f.At, 18), new Color(255, 214, 90), 1.0f, 80f, 2);
        foreach (var fb in TroveItems.FreezeBombs) {
            // blinks faster as its fuse runs down
            float left = (float)(fb.Fuse - Now);
            float blink = Pulse(left < 1.2f ? 30f : 10f);
            GameplayLights.AddGlow(At(fb.At, 9), new Color(140, 215, 255), 0.6f + 1.2f * blink, 65f, 3);
        }
        foreach (var w in TroveItems.Waves) {
            float age = (float)(Now - w.Time);
            if (age is >= 0f and < 0.45f)
                GameplayLights.AddGlow(At(w.At, 10), new Color(200, 235, 255), 2.2f * (1f - age / 0.45f), 120f + age * 260f, 4);
        }
        foreach (var (tank, until) in TroveItems.IcedUntil)
            if (Now < until && tank is { IsDestroyed: false })
                GameplayLights.AddGlow(tank.Position3D + new Vector3(0, 10, 0), new Color(150, 220, 255), 0.7f, 50f, 1);
        foreach (var (tank, _) in TroveItems.Turrets)
            if (tank is { IsDestroyed: false })
                GameplayLights.AddGlow(tank.Position3D + new Vector3(0, 12, 0), new Color(90, 230, 255), 0.7f, 50f, 1);
        for (int i = 0; i < 4; i++)
            if (GameHandler.AllPlayerTanks[i] is { IsDestroyed: false } pt && Now < TroveItems.Players[i].InvincibleUntil)
                GameplayLights.AddGlow(pt.Position3D + new Vector3(0, 12, 0), new Color(255, 214, 80), 1.6f + 0.4f * Pulse(12f), 95f, 4);

        // ---- enemies
        foreach (var t in GameHandler.AllAITanks) {
            if (t is null || t.IsDestroyed || t.Properties.Invisible)
                continue;
            switch (TroveEnemies.VariantOf(t)) {
                case EnemyVariant.Nest:
                    GameplayLights.AddGlow(t.Position3D + new Vector3(0, 14, 0), new Color(255, 200, 70), 0.8f + 0.5f * Pulse(2.6f, t.WorldId * 1.7f), 75f, 2);
                    break;
                case EnemyVariant.Turret:
                    GameplayLights.AddGlow(t.Position3D + new Vector3(0, 18, 0), new Color(255, 60, 50), 0.5f + 0.4f * Pulse(5f, t.WorldId * 1.7f), 40f, 1);
                    break;
                case EnemyVariant.Crawler:
                case EnemyVariant.Kamikaze:
                    // a lit fuse pulses fast and bright; a crawler's light blinks slowly otherwise
                    if (TroveEnemies.IsFused(t))
                        GameplayLights.AddGlow(t.Position3D + new Vector3(0, 10, 0), new Color(255, 50, 30), 0.8f + 1.8f * Pulse(44f), 75f, 4);
                    else if (TroveEnemies.VariantOf(t) == EnemyVariant.Crawler && (int)((now + t.WorldId * 1.7f) * 2.2f) % 2 == 0)
                        GameplayLights.AddGlow(t.Position3D + new Vector3(0, 8, 0), new Color(255, 50, 30), 0.7f, 34f, 1);
                    break;
                case EnemyVariant.Medic:
                    GameplayLights.AddGlow(t.Position3D + new Vector3(0, 12, 0), new Color(90, 255, 120), 0.6f + 0.2f * Pulse(3f), 55f, 1);
                    break;
            }
        }
        foreach (var m in TroveEnemies.Mortars) {
            // the shell glows along its arc, and the spot it's falling on reddens as it comes down
            float span = (float)Math.Max(0.01, m.LandAt - m.FiredAt);
            float k = MathHelper.Clamp((float)(Now - m.FiredAt) / span, 0f, 1f);
            var ground = Vector2.Lerp(m.From, m.To, k);
            GameplayLights.AddGlow(At(ground, 12f + 90f * 4f * k * (1f - k)), new Color(255, 150, 60), 1.2f, 70f, 2);
            GameplayLights.AddGlow(At(m.To, 6), new Color(255, 40, 30), 0.4f + 1.4f * k * Pulse(8f + 30f * k), 60f, 3);
        }
        foreach (var s in TroveEnemies.SniperLines)
            if (s.Sniper is { IsDestroyed: false } sn)
                GameplayLights.AddGlow(sn.TurretPosition3D + new Vector3(0, 6, 0), new Color(255, 40, 40), 1.5f, 55f, 3);
        foreach (var d in TroveDrops.Pending) {
            // where a parachuting tank is about to land
            float left = (float)(d.LandAt - Now);
            if (left is > 0f and < 1.5f)
                GameplayLights.AddGlow(At(d.At, 8), new Color(255, 70, 50), 0.5f + 0.9f * Pulse(14f), 55f, 2);
        }

        // ---- bosses
        if (TroveBosses.Boss is { IsDestroyed: false } boss && !boss.Properties.Invisible) {
            var c = TroveBosses.HeadlightColor(boss);
            GameplayLights.AddGlow(boss.Position3D + new Vector3(0, 6, 0), c, 1.0f + 0.25f * Pulse(2f), 95f, 3);
            if (TroveBosses.Twin is { IsDestroyed: false } twin)
                GameplayLights.AddGlow(twin.Position3D + new Vector3(0, 6, 0), TroveBosses.HeadlightColor(twin), 1.0f + 0.25f * Pulse(2f, 2f), 95f, 3);
        }
        foreach (var f in TroveBosses.Fires)
            if (Now < f.Until)
                GameplayLights.AddGlow(At(f.At, 8), new Color(255, 120, 30), 1.1f + 0.35f * Pulse(21f, f.At.Y), 70f, 2);
        foreach (var b in TroveBosses.Bubbles)
            if (Now < b.Until)
                GameplayLights.AddGlow(At(b.At, 8), new Color(110, 255, 90), 0.9f + 0.3f * Pulse(4f, b.At.X), 60f, 2);
        foreach (var l in TroveBosses.Lines) {
            // an aiming line charges up red at the barrel
            float left = (float)(l.FireAt - Now);
            if (left > 0f)
                GameplayLights.AddGlow(At(l.From, 12), new Color(255, 50, 40), 0.8f + 1.2f * Pulse(18f), 60f, 3);
        }
        foreach (var tg in TroveBosses.Targets) {
            float left = (float)(tg.LandAt - Now);
            if (left > 0f)
                GameplayLights.AddGlow(At(tg.At, 6), new Color(255, 60, 40), 0.5f + 1.1f * Pulse(left < 0.6f ? 34f : 12f), 60f, 2);
        }

        // ---- the ways out, once they open (drawn by the HUD; they light the floor in front of them)
        if (TroveRun.Room is { Cleared: true } open && TroveRun.Floor is { } floor && !TroveRun.Battle)
            foreach (var d in TroveGen.Doors(floor, open)) {
                var s = TroveGen.Step(d);
                var to = floor.Rooms[new Point(open.Cell.X + s.X, open.Cell.Y + s.Y)];
                GameplayLights.AddGlow(At(TroveRun.DoorPoint(d), 16), DoorColor(to.Kind, to.Cleared), 1.2f + 0.25f * Pulse(2.2f, (int)d), 85f, 2);
            }
    }

    /// <summary>The color a doorway glows: what kind of room is behind it.</summary>
    public static Color DoorColor(TroveRoomKind kind, bool cleared) => kind switch {
        TroveRoomKind.Boss => new Color(255, 70, 60),
        TroveRoomKind.MiniBoss => new Color(255, 120, 50),
        TroveRoomKind.Item => new Color(255, 214, 80),
        TroveRoomKind.Secret => new Color(190, 120, 255),
        TroveRoomKind.Challenge => new Color(255, 150, 50),
        _ => cleared ? new Color(170, 200, 230) : new Color(255, 236, 196),
    };
}
