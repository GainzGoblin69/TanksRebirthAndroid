using System;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent;
using TanksRebirth.GameContent.Systems.TankSystem;

namespace TanksRebirth.Internals.Common.Framework.Input;

/// <summary>
/// Phones: short vibrations for things that happen to you (shooting, laying a mine, items, getting hit, nearby blasts).
/// The phone side (<see cref="Impl"/>) is set by the Android launcher; the joystick never vibrates.
/// Switched in Options > Graphics > Vibration.
/// </summary>
public static class Haptics {
    /// <summary>Vibrates for (milliseconds, strength 1-255). Set by the platform; null = no vibration.</summary>
    public static Action<long, int>? Impl;
    public static bool Enabled => Impl is not null && TankGame.Settings?.Vibration != false;

    static readonly Stopwatch _clock = Stopwatch.StartNew();
    static long _busyUntil;
    static int _lastStrength;

    /// <summary>A pulse. Weaker pulses are skipped while a stronger one is still going (so rapid fire doesn't buzz nonstop).</summary>
    public static void Pulse(long ms, int strength) {
        if (!Enabled)
            return;
        long now = _clock.ElapsedMilliseconds;
        if (now < _busyUntil && strength <= _lastStrength)
            return;
        _busyUntil = now + ms + 25;
        _lastStrength = strength;
        try { Impl!(ms, Math.Clamp(strength, 1, 255)); } catch { }
    }

    /// <summary>A light tick for pressing a menu button.</summary>
    public static void Tap() => Pulse(12, 90);
    public static void Shoot() => Pulse(14, 70);
    public static void Mine() => Pulse(28, 130);
    public static void ItemPickup() => Pulse(40, 160);
    public static void ItemUse() => Pulse(45, 200);
    public static void Hit() => Pulse(70, 210);
    public static void Destroyed() => Pulse(180, 255);
    public static void NearbyBlast(float closeness) => Pulse(30 + (long)(40 * closeness), 90 + (int)(140 * closeness));

    /// <summary>A player tank on this device (offline, same-device co-op, or your own tank online).</summary>
    public static bool IsLocal(PlayerTank p) => GameContent.Systems.Troves.TroveItems.IsLocal(p.PlayerId);

    static bool _installed;
    public static void Install() {
        if (_installed)
            return;
        _installed = true;
        Tank.OnDamage += (victim, destroy, _) => {
            if (victim is PlayerTank p && IsLocal(p)) {
                if (destroy) Destroyed();
                else Hit();
            }
        };
        GameContent.Mine.OnExplode += mine => {
            // a blast close to one of your tanks
            float best = 0f;
            foreach (var p in GameHandler.AllPlayerTanks) {
                if (p is null || p.IsDestroyed || !IsLocal(p)) continue;
                float d = Vector2.Distance(p.Position, mine.Position);
                best = MathF.Max(best, 1f - d / 160f);
            }
            if (best > 0f)
                NearbyBlast(best);
        };
    }
}
