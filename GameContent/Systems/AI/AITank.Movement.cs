using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.AI;

/// <summary>
/// Enemy tank movement, from Tanks! Rebirth 1.9.1 ("closer to the original"): every movement opportunity the tank
/// either meanders (a random turn, biased toward its target, steered away from walls it would reach soon) or dodges
/// (shells push it sideways out of their path, mines and other dangers push it straight away), and its heading is the
/// average of its last few picks, which gives the smooth curving movement of the original game. After laying a mine
/// it picks the direction with the most room and keeps going that way until it's clear.
/// </summary>
public partial class AITank {
    /// <summary>Whether or not this tank should perform movement logic.</summary>
    public bool DoMovements = true;
    /// <summary>Whether or not this tank should update its desired direction.</summary>
    public bool DoMoveTowards = true;

    public int CurrentRandomMove;

    /// <summary>Whether or not this <see cref="AITank"/> is avoiding a dangerous object.</summary>
    public bool IsSurviving;
    /// <summary>Whether or not this tank is running from a mine it laid itself (it keeps going until it's clear).</summary>
    public bool EscapingOwnMine;
    /// <summary>The tank is dodging and its parameters say it can't shoot while fleeing.</summary>
    public bool AttackPrevented;

    public const int HISTORY_SLOTS = 8;
    /// <summary>How the closest dangers count (the original game's weights).</summary>
    static readonly float[] _dangerWeights = [1f, 0.8f, 0.6f, 0.4f];
    /// <summary>The original game's circle radius for wall checks.</summary>
    public const float WALL_SWEEP_RADIUS = 16f;

    /// <summary>The last directions this tank picked, newest first; it heads along the average of the first few.</summary>
    Vector2[]? _history;
    readonly List<Shell> _movingDangers = [];
    readonly List<IAITankDanger> _stillDangers = [];

    // the same scale the physics uses for velocity
    float SpeedPerFrame => Properties.MaxSpeed * 0.55f;

    static Vector2 DirectionOf(float rotation) => Vector2.UnitY.Rotate(rotation);
    static float RotationOf(Vector2 direction) => direction.ToRotation() - MathHelper.PiOver2;
    static Vector2 NormOr(Vector2 v, Vector2 fallback = default) {
        float l = v.Length();
        return l > 1e-6f ? v / l : fallback;
    }
    static Vector2 Perp(Vector2 v) => new(-v.Y, v.X);

    // this runs every "movement opportunity" (somewhere between RandomTimerMinMove and RandomTimerMaxMove frames)
    /// <summary>Wanders, dodges shells and mines, or runs from its own mine, then averages its history into <see cref="Tank.DesiredChassisRotation"/>.</summary>
    public void DoMovement() {
        bool shouldMove = !IsTurning && CurMineStun <= 0 && CurShootStun <= 0;
        if (!shouldMove) return;
        if (!Behaviors[0].IsModOf(CurrentRandomMove)) return;

        if (_history is null) {
            _history = new Vector2[HISTORY_SLOTS];
            Array.Fill(_history, DirectionOf(ChassisRotation));
        }

        IsSurviving = false;
        AttackPrevented = false;

        CurrentRandomMove = Client.ClientRandom.Next(Parameters.RandomTimerMinMove, Parameters.RandomTimerMaxMove);
        Behaviors[0].Value = 0;

        if (EscapingOwnMine && !_nearOwnMine)
            EscapingOwnMine = false;

        // while escaping its own mine it just keeps following its history
        if (!EscapingOwnMine) {
            if (NearbyDangers is { Count: > 0 })
                Dodge();
            else
                Meander();
        }

        var sum = Vector2.Zero;
        for (int i = 0; i < Math.Clamp(Parameters.MaxQueuedMovements, 1, HISTORY_SLOTS); i++)
            sum += _history[i];
        if (sum != Vector2.Zero)
            DesiredChassisRotation = RotationOf(NormOr(sum));
    }

    void PushHistory(Vector2 direction) {
        for (int i = _history!.Length - 1; i > 0; i--)
            _history[i] = _history[i - 1];
        _history[0] = direction;
    }

    /// <summary>A new random direction, leaning toward the target, and away from walls it would run into soon.</summary>
    void Meander() {
        var direction = _history![0];
        if (TargetTank is not null)
            direction += NormOr(TargetTank.Position - Position) * Parameters.AggressivenessBias;
        direction = NormOr(direction, _history[0]);

        // ObstacleAwarenessMovement is a number of frames here, not a distance
        var sweep = SweepForWalls(Position, direction, SpeedPerFrame, (int)Parameters.ObstacleAwarenessMovement);
        if (sweep.Hit)
            direction = NormOr(direction + NormOr(sweep.Normal), direction);

        var angle = Client.ClientRandom.NextFloat(-Parameters.MaxAngleRandomTurn, Parameters.MaxAngleRandomTurn);
        PushHistory(NormOr(direction + Perp(direction) * MathF.Tan(angle), direction));
    }

    /// <summary>Shells push it sideways out of their path (and away); mines and other dangers push it straight away.</summary>
    void Dodge() {
        IsSurviving = true;
        AttackPrevented = Parameters.CantShootWhileFleeing;
        var position = Position;

        _movingDangers.Clear();
        _stillDangers.Clear();
        foreach (var d in NearbyDangers) {
            if (d is Shell shell) _movingDangers.Add(shell);
            else _stillDangers.Add(d);
        }
        _movingDangers.Sort((a, b) => Vector2.Distance(a.Position, position).CompareTo(Vector2.Distance(b.Position, position)));
        _stillDangers.Sort((a, b) => Vector2.Distance(a.Position, position).CompareTo(Vector2.Distance(b.Position, position)));

        var push = Vector2.Zero;
        for (int i = 0; i < Math.Min(_movingDangers.Count, _dangerWeights.Length); i++) {
            var shell = _movingDangers[i];
            var heading = NormOr(shell.Velocity);
            var away = position - shell.Position;
            // step out of the shell's path, not just straight away from it
            var sideways = NormOr(away - heading * Vector2.Dot(away, heading), Perp(heading));
            push += _dangerWeights[i] * (sideways + NormOr(away));
        }
        if (_movingDangers.Count > 0) {
            push = NormOr(push, _history![0]);
            PushHistory(push); PushHistory(push); // (twice, like the original)
        }
        if (_stillDangers.Count == 0) return;

        for (int i = 0; i < Math.Min(_stillDangers.Count, _dangerWeights.Length); i++)
            push += _dangerWeights[i] * NormOr(position - _stillDangers[i].Position);
        push = NormOr(push, _history![0]);
        PushHistory(push); PushHistory(push);
    }

    /// <summary>Picks the direction to run in after laying a mine: any clear way, or the one with the most room.</summary>
    /// <returns>False if there's nowhere good to go (so the mine shouldn't be laid).</returns>
    public bool CanEscapeOwnMine() {
        var forward = DirectionOf(ChassisRotation);
        Span<Vector2> directions = [forward, -forward, Perp(forward), -Perp(forward)];
        var escapeDistance = GameUtils.Value_WiiTanksUnits(Parameters.ObstacleAwarenessMine);
        var frames = (int)(3f * Parameters.ObstacleAwarenessMine / MathF.Max(Properties.MaxSpeed, 1e-6f));

        var farthest = escapeDistance;
        int farthestIndex = -1;
        for (int i = 0; i < directions.Length; i++) {
            var sweep = SweepForWalls(Position, directions[i], SpeedPerFrame, frames);
            if (!sweep.Hit) {
                InitMineEscape(directions[i]);
                return true;
            }
            if (sweep.Distance > farthest) {
                farthest = sweep.Distance;
                farthestIndex = i;
            }
        }
        if (farthestIndex == -1) return false;
        InitMineEscape(directions[farthestIndex]);
        return true;
    }

    void InitMineEscape(Vector2 direction) {
        _history ??= new Vector2[HISTORY_SLOTS];
        Array.Fill(_history, direction);
        EscapingOwnMine = true;
        DesiredChassisRotation = RotationOf(direction);
    }

    // ------------------------------------------------------------------ wall sweeps

    public struct WallSweep {
        public bool Hit;
        public int HitFrame;
        public float Distance;
        public Vector2 Normal;
    }

    /// <summary>Whether a circle at <paramref name="position"/> touches a solid block or the edge of the arena (and which way is out).</summary>
    public static bool TouchesWall(Vector2 position, out Vector2 normal, float scanRadius = WALL_SWEEP_RADIUS) {
        normal = Vector2.Zero;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            return false;
        bool hit = false;
        float radius = GameUtils.Value_WiiTanksUnits(scanRadius);
        var half = new Vector2(Block.SIDE_LENGTH / 2f);
        var blocks = Block.AllBlocks;
        for (int i = 0; i < blocks.Length; i++) {
            var block = blocks[i];
            if (block is null || !block.Properties.IsCollidable) continue;
            if (!CircleIntersectsRect(position, radius, block.Position - half, block.Position + half, out var n)) continue;
            normal += n;
            hit = true;
        }
        // the arena's edges
        if (position.X - radius < Graphics.GameScene.MIN_X) { normal += Vector2.UnitX; hit = true; }
        if (position.X + radius > Graphics.GameScene.MAX_X) { normal -= Vector2.UnitX; hit = true; }
        if (position.Y - radius < Graphics.GameScene.MIN_Z) { normal += Vector2.UnitY; hit = true; }
        if (position.Y + radius > Graphics.GameScene.MAX_Z) { normal -= Vector2.UnitY; hit = true; }
        return hit;
    }

    /// <summary>Moves a circle along <paramref name="direction"/> and checks for walls on frames 1, 3, 5... below <paramref name="numFrames"/>.</summary>
    public static WallSweep SweepForWalls(Vector2 start, Vector2 direction, float speedPerFrame, int numFrames, float scanRadius = WALL_SWEEP_RADIUS) {
        var sweep = new WallSweep();
        for (int frame = 1; frame < numFrames; frame += 2) {
            if (!TouchesWall(start + direction * speedPerFrame * frame, out var normal, scanRadius)) continue;
            sweep.Hit = true;
            sweep.HitFrame = frame;
            sweep.Distance = speedPerFrame * frame;
            sweep.Normal += normal;
            break;
        }
        return sweep;
    }

    static bool CircleIntersectsRect(Vector2 center, float radius, Vector2 min, Vector2 max, out Vector2 normal) {
        normal = Vector2.Zero;
        var diff = center - Vector2.Clamp(center, min, max);
        if (diff.LengthSquared() >= radius * radius) return false;
        if (diff != Vector2.Zero) {
            normal = MathF.Abs(diff.X) > MathF.Abs(diff.Y) ? new(MathF.Sign(diff.X), 0) : new(0, MathF.Sign(diff.Y));
            return true;
        }
        var toMin = center - min;
        var toMax = max - center;
        var nearest = MathF.Min(MathF.Min(toMin.X, toMax.X), MathF.Min(toMin.Y, toMax.Y));
        normal = nearest == toMin.X ? -Vector2.UnitX : nearest == toMax.X ? Vector2.UnitX : nearest == toMin.Y ? -Vector2.UnitY : Vector2.UnitY;
        return true;
    }
}
