using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// Finds where a tank's numbers first go bad (NaN or infinity). After every part of the update it checks each tank, and
/// the first time one is bad it writes one log line: which part of the update did it, which numbers went bad, and what
/// they were the last time they were fine.
/// </summary>
public static class NanWatch {
    private struct Snap {
        public Vector2 Pos, BodyVel, Vel, Knock;
        public float Rot, Desired, Speed, Dt;
        public string After;
    }
    private static readonly Dictionary<Tank, Snap> _good = [];
    private static readonly HashSet<Tank> _reported = [];
    private static int _logged;

    private static bool Bad(float f) => !float.IsFinite(f);
    private static bool Bad(Vector2 v) => !float.IsFinite(v.X) || !float.IsFinite(v.Y);

    public static void Check(Perf.S stage) {
        if (stage > Perf.S.UpdateRest)
            return;
        if (Bad(RuntimeData.DeltaTime) && _logged++ < 20)
            TankGame.ClientLog.Write($"[NaN] DeltaTime={RuntimeData.DeltaTime} LogicFPS={RuntimeData.LogicFPS} after {stage}", LogType.Warn);
        foreach (var t in GameHandler.AllTanks) {
            if (t?.Physics is null)
                continue;
            var s = new Snap {
                Pos = t.Physics.Position, BodyVel = t.Physics.LinearVelocity, Vel = t.Velocity, Knock = t.KnockbackVelocity,
                Rot = t.ChassisRotation, Desired = t.DesiredChassisRotation, Speed = t.Speed, Dt = RuntimeData.DeltaTime, After = stage.ToString(),
            };
            // (a NaN wanted rotation is normal: it means "no direction")
            bool bad = Bad(s.Pos) || Bad(s.BodyVel) || Bad(s.Vel) || Bad(s.Knock) || Bad(s.Rot) || Bad(s.Speed);
            if (!bad) {
                _good[t] = s;
                continue;
            }
            if (!_reported.Add(t) || _logged++ >= 20)
                continue;
            var sb = new StringBuilder();
            sb.Append($"[NaN] {(t is PlayerTank pt ? $"player {pt.PlayerId}" : t is AITank ai ? $"AI {ai.AiTankType}" : "tank")} #{t.WorldId} destroyed={t.IsDestroyed} went bad during {stage}: ");
            if (Bad(s.Pos)) sb.Append("POSITION ");
            if (Bad(s.BodyVel)) sb.Append("BODY-VELOCITY ");
            if (Bad(s.Vel)) sb.Append("VELOCITY ");
            if (Bad(s.Knock)) sb.Append("KNOCKBACK ");
            if (Bad(s.Rot)) sb.Append("ROTATION ");
            if (Bad(s.Speed)) sb.Append("SPEED ");
            sb.Append($"| now pos={s.Pos} bodyVel={s.BodyVel} vel={s.Vel} knock={s.Knock} rot={s.Rot} want={s.Desired} speed={s.Speed} dt={s.Dt} fps={RuntimeData.LogicFPS}");
            if (_good.TryGetValue(t, out var g))
                sb.Append($" | last good (after {g.After}) pos={g.Pos} bodyVel={g.BodyVel} vel={g.Vel} knock={g.Knock} rot={g.Rot} want={g.Desired} speed={g.Speed} dt={g.Dt}");
            sb.Append($" | maxSpeed={t.Properties.MaxSpeed} accel={t.Properties.Acceleration} decel={t.Properties.Deceleration} turn={t.Properties.TurningSpeed}");
            // what it was touching
            for (var c = t.Physics.ContactList; c is not null; c = c.Next)
                if (c.Contact.IsTouching)
                    sb.Append($" | touching {c.Other.Tag?.GetType().Name ?? "?"} at {c.Other.Position}");
            TankGame.ClientLog.Write(sb.ToString(), LogType.Warn);
        }
    }

    /// <summary>A new round or room: report again.</summary>
    public static void Clear() {
        _good.Clear();
        _reported.Clear();
    }
}
