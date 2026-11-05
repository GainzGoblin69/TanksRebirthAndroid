using System;
using Microsoft.Xna.Framework;
using TanksRebirth.Enums;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Graphics;
using TanksRebirth.GameContent.GameMechanics;
using TanksRebirth.Internals.Common.Framework.Collisions;

namespace TanksRebirth.GameContent;

public enum ShellSimResult { None, Ricochet, Destroyed }

/// <summary>
/// From Tanks! Rebirth 1.9.1: a <see cref="Shell"/> stripped down to its movement and ricochet rules, stepped one tick
/// at a time exactly like a real shell (<see cref="Shell.MOVE_SCALE"/>, the same small steps between collision checks),
/// so enemy tanks predict their bounce shots with the same physics the shell will actually follow.
/// No allocations, so it's safe and cheap on the AI's worker threads.
/// </summary>
public struct ShellSim(Vector2 position, Vector2 velocity, int ricochets) {
    public Vector2 Position = position;
    public Vector2 Velocity = velocity;
    public int RicochetsRemaining = ricochets;
    /// <summary>Ticks stepped (like a shell's tick count).</summary>
    public int LifeTime;
    /// <summary>Distance travelled since the last ricochet (or the start).</summary>
    public float Distance;

    public static ShellSim From(Tank owner, Vector2 position, Vector2 direction) {
        var d = direction.LengthSquared() > 1e-8f ? Vector2.Normalize(direction) : Vector2.UnitY;
        return new(position, d * owner.Properties.ShellSpeed, (int)owner.Properties.RicochetCount);
    }

    /// <summary>One tick. <paramref name="collides"/> picks which blocks the shell reacts to.</summary>
    public ShellSimResult Step(Func<Block, bool> collides, out Block? block) {
        var result = ShellSimResult.None;
        block = null;
        int substeps = Shell.NumSubsteps((Velocity * Shell.MOVE_SCALE).Length());
        for (int i = 0; i < substeps; i++) {
            var sub = Velocity * Shell.MOVE_SCALE / substeps;
            Position += sub;
            Distance += sub.Length();
            var r = Collide(collides, out var b);
            if (b is not null) block = b;
            if (r == ShellSimResult.Destroyed) { result = r; break; }
            if (r == ShellSimResult.Ricochet) result = r;
        }
        LifeTime++;
        return result;
    }

    ShellSimResult Collide(Func<Block, bool> collides, out Block? block) {
        var result = ShellSimResult.None;
        block = null;
        if (Position.X < GameScene.MIN_X) { Position.X = GameScene.MIN_X; result = Bounce(true, 1); }
        else if (Position.X > GameScene.MAX_X) { Position.X = GameScene.MAX_X; result = Bounce(true, 1); }
        if (result == ShellSimResult.Destroyed) return result;
        if (Position.Y < GameScene.MIN_Z) { Position.Y = GameScene.MIN_Z; result = Bounce(false, 1); }
        else if (Position.Y > GameScene.MAX_Z) { Position.Y = GameScene.MAX_Z; result = Bounce(false, 1); }
        if (result == ShellSimResult.Destroyed) return result;

        // the same 4x4 box a real shell uses
        var hitbox = new Rectangle((int)(Position.X - 2), (int)(Position.Y - 2), 4, 4);
        var dummy = Vector2.Zero;
        Collision.HandleCollisionSimple_ForBlocks(hitbox, Velocity, ref dummy, out var dir, out block, out bool corner, false, collides);
        if (corner) return ShellSimResult.Destroyed;
        if (block is null || block.Type == BlockID.Teleporter || !block.Properties.AllowShotPathBounce) return result;
        var bounce = dir switch {
            CollisionDirection.Up or CollisionDirection.Down => Bounce(false, block.Properties.PathBounceCount),
            CollisionDirection.Left or CollisionDirection.Right => Bounce(true, block.Properties.PathBounceCount),
            _ => ShellSimResult.None,
        };
        return bounce == ShellSimResult.None ? result : bounce;
    }

    ShellSimResult Bounce(bool horizontal, int cost) {
        // the same rules as Shell.Ricochet (and a shell that hits something right away is gone)
        if (RicochetsRemaining < cost || LifeTime <= Shell.TOO_SHORT_TICKS) return ShellSimResult.Destroyed;
        if (horizontal) Velocity.X = -Velocity.X;
        else Velocity.Y = -Velocity.Y;
        RicochetsRemaining -= cost;
        return ShellSimResult.Ricochet;
    }
}
