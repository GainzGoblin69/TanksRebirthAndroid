using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using TanksRebirth.Enums;
using TanksRebirth.GameContent.GameMechanics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.PingSystem;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Graphics;
using TanksRebirth.Internals.Common.Framework;
using TanksRebirth.Internals.Common.Framework.Collisions;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.AI;
public partial class AITank {
    bool _predicts;
    bool _isSeeking;
    float _seekRotation;

    public bool DoAttack = true;

    public Tank? TargetTank;
    public float TurretRotationMultiplier = 1f;
    public bool IsEnemySpotted;

    public int CurrentRandomMineLay;
    public int CurrentRandomShoot;

    public float ObstacleAwarenessMineReal;

    /// <summary>The location(s) of which this tank's shot path hits an obstacle.</summary>
    public Vector2[] ShotPathRicochetPoints { get; private set; } = [];
    /// <summary>The location(s) of which this tank's shot path hits an tank.</summary>
    public Vector2[] ShotPathTankCollPoints { get; private set; } = [];
    /// <summary>Updates turret directions/targets and updates tanks in the shoot path.</summary>
    public void HandleTurret() {
        TargetTurretRotation %= MathHelper.TwoPi;

        TurretRotation %= MathHelper.TwoPi;

        var diff = TargetTurretRotation - TurretRotation;
        if (diff > MathHelper.Pi)
            TargetTurretRotation -= MathHelper.TwoPi;
        else if (diff < -MathHelper.Pi)
            TargetTurretRotation += MathHelper.TwoPi;

        TurretRotation = MathUtils.RoughStep(TurretRotation, TargetTurretRotation, Parameters.TurretSpeed * TurretRotationMultiplier * RuntimeData.DeltaTime);

        // update things prior to timer check
        if (TargetTank is null) return;
        if (!_isSeeking && !_predicts) {
            IsEnemySpotted = false;
            if (TargetTank!.Properties.Invisible && TargetTank.TimeSinceLastAction < Parameters.Rememberance) {
                AimTarget = TargetTank.Position;
                IsEnemySpotted = true;
            }

            if (!TargetTank.Properties.Invisible) {
                AimTarget = TargetTank.Position;
                IsEnemySpotted = true;
            }
        }
        UpdateAim();

        if (!Behaviors[1].IsModOf(Parameters.TurretMovementTimer)) return;

        var dirVec = Position - AimTarget;
        TargetTurretRotation = -dirVec.ToRotation() - MathHelper.PiOver2 + Client.ClientRandom.NextFloat(-Parameters.AimOffset, Parameters.AimOffset);
    }
    /// <summary>Attempts to lay a mine based on various conditions and environmental factors.</summary>
    /// <remarks>This method evaluates multiple conditions to determine whether a mine can be laid, including:
    /// <list type="bullet"> <item>Whether the tank is capable of laying mines.</item> <item>Whether the mine limit has
    /// been reached.</item> <item>Proximity to friendly tanks to avoid friendly fire.</item> <item>Presence of
    /// destructible obstacles nearby, which may influence the chance of laying a mine.</item> <item>Availability of
    /// safe directions to lay a mine, avoiding existing mines and obstacles.</item> </list> If all conditions are met,
    /// a mine is laid, and the tank selects a direction to flee.</remarks>
    public void TryMineLay() {
        // don't even bother if the tank can't lay mines
        if (!Behaviors[3].IsModOf(CurrentRandomMineLay)) return;

        // set our new random window, this gets set
        Behaviors[3].Value = 0;

        if (Properties.MineLimit <= 0) return;
        if (IsSurviving) return;

        CurrentRandomMineLay = Client.ClientRandom.Next(Parameters.RandomTimerMinMine, Parameters.RandomTimerMaxMine);

        // check for friendly tanks nearby, if there are any, don't even attempt to lay a mine
        if (TanksNearMineAwareness.Any(x => x.Team == Team && x.Team != TeamID.NoTeam)) return;

        bool nearDestructible = false;

        // call me the wizard of oz
        ObstacleAwarenessMineReal = 3 * Parameters.ObstacleAwarenessMine;

        var dist = ObstacleAwarenessMineReal / 2;

        var dirs = RayCastCardinals(dist, (fixture, point, normal, fraction) => {
            if (fixture.Body.Tag is Block b) {
                if (!nearDestructible)
                    nearDestructible = b.Properties.IsDestructible;

                // b.Stack = (byte)Client.ClientRandom.Next(1, 8);
            }
            return fraction;

            // this wizardry goes beyond me
        }, ChassisRotation - MathHelper.Pi);

        var goodDirs = dirs.Where(x => x.Direction != CollisionDirection.None).ToArray();

        for (int i = 0; i < goodDirs.Length; i++) {
            var pos = goodDirs[i].Vec * dist;
            for (int j = 0; j < Mine.AllMines.Length; j++) {
                var mine = Mine.AllMines[j];
                if (mine is null) continue;

                // check against radius. ensures the tank doesn't move towards an already laid mine
                // 70 is the magic number for the default mine radius, multiplied by the scalar
                if (GameUtils.Distance_WiiTanksUnits(pos, mine.Position) <= mine.ExplosionRadius * GameUtils.Value_WiiTanksUnits(70)) {
                    // Console.WriteLine("Direction " + dirs[i].Direction + " is contaminated");
                    goodDirs[i].Direction = CollisionDirection.None;
                }
            }
        }

        bool isMineLayOk = goodDirs.Length != 0;
        if (!isMineLayOk) return;

        // attempt via an opportunity to lay a mine
        var random = Client.ClientRandom.NextFloat(0, 1);
        // change chance based on whether or not the tank is near a destructible obstacle
        var randomSuccess = random <= (nearDestructible ? Parameters.ChanceMineLayNearBreakables : Parameters.ChanceMineLay);
        if (!randomSuccess) return;

        // 1.9.1: pick the way out with the most room first (no good way out: no mine), then run that way until clear
        if (!CanEscapeOwnMine()) return;
        LayMine();
    }
    // TODO: make view distance, and make tanks in path public
    /// <summary>Updates meta-data related to aiming and shooting. The tank will not fire if <see cref="DoAttack"/> is false, but meta-data will still update.</summary>
    public void UpdateAim() {
        _predicts = false;
        SeesTarget = false;

        bool tooCloseToExplosiveShell = false;
        
        var friendliesNearby = TanksNearShootAwareness.Any(x => IsOnSameTeamAs(x.Team));
        // stop doing expensive checks if the tank can't even shoot anyway
        if (friendliesNearby) return;

        List<Tank> tanksDef;

        if (Properties.ShellType == ShellID.Explosive) {
            tanksDef = GetTanksInPath(Vector2.UnitY.Rotate(TurretRotation - MathHelper.Pi), out var ricP, out var tnkCol, offset: Vector2.UnitY * 20, pattern: ExplosivePathPattern, missDist: Parameters.DetectionForgivenessHostile, doBounceReset: Parameters.BounceReset);
            if (GameUtils.Distance_WiiTanksUnits(ricP[^1], Position) < 150f) // TODO: change from hardcode to normalcode :YES:
                tooCloseToExplosiveShell = true;
        }
        else {
            tanksDef = GetTanksInPath(
                Vector2.UnitY.Rotate(TurretRotation - MathHelper.Pi),
                out var ricP, out var tnkCol, offset: Vector2.UnitY * 20,
                missDist: Parameters.DetectionForgivenessHostile, doBounceReset: Parameters.BounceReset);

            TanksSpotted = [.. tanksDef];

            ShotPathRicochetPoints = ricP;
            ShotPathTankCollPoints = tnkCol;
        }
        if (Parameters.PredictsPositions) {
            if (TargetTank is not null) {
                var calculation = Position.Distance(TargetTank.Position) / (float)(Properties.ShellSpeed * 1.2f);
                float rot = -Position.DirectionTo(GeometryUtils.PredictFuturePosition(TargetTank.Position, TargetTank.Velocity, calculation))
                    .ToRotation() - MathHelper.PiOver2;

                tanksDef = GetTanksInPath(
                Vector2.UnitY.Rotate(-Position.DirectionTo(TargetTank.Position).ToRotation() - MathHelper.PiOver2),
                out var ricP, out var tnkCol, offset: Parameters.PredictsPositions ? Vector2.Zero : Vector2.UnitY * 20,
                missDist: Parameters.DetectionForgivenessHostile, doBounceReset: Parameters.BounceReset);

                var targ = GeometryUtils.PredictFuturePosition(TargetTank.Position, TargetTank.Velocity, calculation);
                var posPredict = GetTanksInPath(Vector2.UnitY.Rotate(rot),
                    out var ricP1, out var tnkCol2, offset: Vector2.UnitY * 20, missDist: Parameters.DetectionForgivenessHostile, doBounceReset: Parameters.BounceReset);

                if (tanksDef.Contains(TargetTank)) {
                    _predicts = true;
                    TargetTurretRotation = rot + MathHelper.Pi;
                }
            }
        }

        // TODO: is findsSelf even necessary? findsEnemy is only true if findsSelf is false. eh, whatever. my brain is fucked.
        var findsEnemy = tanksDef.Any(tnk => tnk is not null && (tnk.Team != Team || tnk.Team == TeamID.NoTeam) && tnk != this);
        var findsSelf = tanksDef.Any(tnk => tnk is not null && tnk == this);
        var findsFriendly = tanksDef.Any(tnk => tnk is not null && tnk.Team == Team && tnk.Team != TeamID.NoTeam);

        if (findsEnemy && !tooCloseToExplosiveShell)
            SeesTarget = true;

        if (Parameters.SmartRicochets) {
            //if (!seeks)
            _seekRotation += Parameters.TurretSpeed * 0.25f;
            var canShoot = !(CurShootCooldown > 0 || OwnedShellCount >= Properties.ShellLimit);
            if (canShoot) {
                var tanks = GetTanksInPath(Vector2.UnitY.Rotate(_seekRotation), out var ricP, out var tnkCol, false, default, Parameters.DetectionForgivenessHostile, doBounceReset: Parameters.BounceReset);

                var findsEnemy2 = tanks.Any(tnk => tnk is not null && !tnk.IsOnSameTeamAs(Team) && tnk != this);
                // var findsSelf2 = tanks.Any(tnk => tnk is not null && tnk == this);
                // var findsFriendly2 = tanks.Any(tnk => tnk is not null && (tnk.Team == Team && tnk.Team != TeamID.NoTeam));
                // ChatSystem.SendMessage($"{findsEnemy2} {findsFriendly2} | seek: {seeks}", Color.White);
                if (findsEnemy2/* && !findsFriendly2*/) {
                    _isSeeking = true;
                    TurretRotationMultiplier = 3f;
                    TargetTurretRotation = _seekRotation - MathHelper.Pi;
                }
            }

            if (TurretRotation == TargetTurretRotation || !canShoot)
                _isSeeking = false;
        }
        // tanks that can't shoot while fleeing don't, while they dodge
        if (AttackPrevented || ClosestDanger is Mine && Parameters.CantShootWhileFleeing)
            return;

        if (!DoAttack) return;
        if (!Behaviors[2].IsModOf(CurrentRandomShoot)) return;
        CurrentRandomShoot = Client.ClientRandom.Next(Parameters.RandomTimerMinShoot, Parameters.RandomTimerMaxShoot);
        Behaviors[2].Value = 0;
        // Console.WriteLine(TanksSpotted.Length);

        // no need to check friendliesNearby because we return earlier in this method if there are any
        if (Parameters.PredictsPositions) {
            if (SeesTarget)
                if (CurShootCooldown <= 0)
                    Shoot(false);
        }
        else {
            if (SeesTarget && !findsSelf && !findsFriendly)
                if (CurShootCooldown <= 0)
                    Shoot(false);
        }
    }
    /// <summary>Gets the cloesest <see cref="Tank"/> that is hostile and is targetable.</summary>
    public Tank? GetClosestTarget() {
        Tank? target = null;
        var targetPosition = new Vector2(float.MaxValue);
        foreach (var tank in GameHandler.AllTanks) {
            if (tank is null || tank.IsDestroyed) continue;
            if (tank == this) continue; // don't target self
            if (tank.IsOnSameTeamAs(Team)) continue; // no friendly fire
            if (IsHidden?.Invoke(tank) == true) continue; // Tank Troves Smoke Bomb

            // no need for WiiTanksUnits here since we are simply looking for the closest tank
            if (Vector2.Distance(tank.Position, Position) < Vector2.Distance(targetPosition, Position)) {
                // if the tank is invisible, we only target it if it has acted in the last 60 ticks
                if (tank.Properties.Invisible && tank.TimeSinceLastAction < Parameters.Rememberance || !tank.Properties.Invisible) {
                    target = tank;
                    targetPosition = tank.Position;
                }
            }
        }
        return target;
    }
    /// <summary>A method that simply changes an AI </summary>
    /// <summary>Set by Tank Troves: a tank this AI must aim at instead (a Decoy).</summary>
    public static Func<AITank, Tank?>? TargetOverride;
    /// <summary>Set by Tank Troves: true for tanks AI can't see right now (Smoke Bomb).</summary>
    public static Func<Tank, bool>? IsHidden;
    public Tank? TryOverrideTarget() {
        Tank? target = TargetTank;
        if (TargetOverride?.Invoke(this) is { } forced)
            return forced;
        if (GameHandler.AllPlayerTanks.Any(x => x is not null && x.IsOnSameTeamAs(Team))) {
            foreach (var ping in IngamePing.AllIngamePings) {
                if (ping is null) break;
                if (ping.TrackedTank is null) break;
                if (ping.TrackedTank == this) continue; // no self-targeting
                if (ping.TrackedTank.IsDestroyed) continue; // no dead tanks
                if (ping.TrackedTank.Team == Team) break; // no friendly fire
                target = ping.TrackedTank;
            }
        }
        return target;
    }
    /// <summary>Makes this <see cref="AITank"/> attempt to shoot to destroy the given <see cref="Shell"/>.</summary>
    public void DoDeflection(Shell shell) {
        var calculation = (Position.Distance(shell.Position) - 20f) / (float)(Properties.ShellSpeed * 1.2f);
        float rot = -Position.DirectionTo(GeometryUtils.PredictFuturePosition(shell.Position, shell.Velocity, calculation))
            .ToRotation() + MathHelper.PiOver2;

        TargetTurretRotation = rot;

        TurretRotationMultiplier = 4f;

        // used to be rot %=... was it necessary?
        //TargetTurretRotation %= MathHelper.Tau;

        //if ((-TurretRotation + MathHelper.PiOver2).IsInRangeOf(TargetTurretRotation, 0.15f))

        Shoot(false);
    }

    // TODO: literally fix everything about these turret rotation values.
    private List<Tank> GetTanksInPath(Vector2 pathDir, out Vector2[] ricochetPoints, out Vector2[] tankCollPoints,
        bool draw = false, Vector2 offset = default, float missDist = 0f, Func<Block, bool>? pattern = null, bool doBounceReset = true) {
        // multi-core: the shot path may already have been traced on a worker thread this frame
        if (!draw) {
            for (int c = 0; c < _aimCacheCount; c++) {
                ref var e = ref _aimCache[c];
                if (e.Dir == pathDir && e.Offset == offset && e.TurretRot == TurretRotation && e.Pos == Position
                    && e.Miss == missDist && ReferenceEquals(e.Pattern, pattern) && e.Bounce == doBounceReset) {
                    ricochetPoints = e.Ric;
                    tankCollPoints = e.Col;
                    AimCacheHits++;
                    return e.Tanks;
                }
            }
            AimCacheMisses++;
        }
        return TracePath(pathDir, out ricochetPoints, out tankCollPoints, TurretRotation, draw, offset, missDist, pattern, doBounceReset);
    }

    struct AimCacheEntry {
        public Vector2 Dir, Offset, Pos;
        public float TurretRot, Miss;
        public Func<Block, bool>? Pattern;
        public bool Bounce;
        public List<Tank> Tanks;
        public Vector2[] Ric, Col;
    }
    readonly AimCacheEntry[] _aimCache = new AimCacheEntry[4];
    /// <summary>Shot paths reused from the worker threads vs. traced on the main thread (main thread only).</summary>
    internal static long AimCacheHits, AimCacheMisses;
    int _aimCacheCount;
    /// <summary>Block filter used for explosive-shell shot paths (one shared instance, so cached paths can be matched).</summary>
    static readonly Func<Block, bool> ExplosivePathPattern = x => !x.Properties.IsDestructible && x.Properties.IsSolid || x.Type == BlockID.Teleporter;

    void CacheAim(Vector2 dir, Vector2 offset, float turretRot, float miss, Func<Block, bool>? pattern, bool bounce) {
        if (_aimCacheCount >= _aimCache.Length) return;
        var tanks = TracePath(dir, out var ric, out var col, turretRot, false, offset, miss, pattern, bounce);
        _aimCache[_aimCacheCount++] = new AimCacheEntry {
            Dir = dir, Offset = offset, Pos = Position, TurretRot = turretRot, Miss = miss,
            Pattern = pattern, Bounce = bounce, Tanks = tanks, Ric = ric, Col = col
        };
    }
    internal void ClearAimCache() => _aimCacheCount = 0;

    /// <summary>Multi-core: traces the shot paths <see cref="UpdateAim"/> is about to ask for, using exactly the same inputs.
    /// Safe on a worker thread: it only reads the world and writes this tank's own cache. If anything differs by the
    /// time the main thread aims (a deflection turned the turret, say), the lookup misses and the path is traced there as before.</summary>
    internal void PrecomputeAim() {
        _aimCacheCount = 0;
        if (TargetTank is null) return;
        if (TanksNearShootAwareness.Any(x => IsOnSameTeamAs(x.Team))) return; // UpdateAim bails out before tracing

        // the turret step HandleTurret takes (DoAI resets the multiplier to 1 first)
        var tgt = TargetTurretRotation % MathHelper.TwoPi;
        var cur = TurretRotation % MathHelper.TwoPi;
        var diff = tgt - cur;
        if (diff > MathHelper.Pi) tgt -= MathHelper.TwoPi;
        else if (diff < -MathHelper.Pi) tgt += MathHelper.TwoPi;
        var tr = MathUtils.RoughStep(cur, tgt, Parameters.TurretSpeed * 1f * RuntimeData.DeltaTime);

        var miss = Parameters.DetectionForgivenessHostile;
        var bounce = Parameters.BounceReset;
        CacheAim(Vector2.UnitY.Rotate(tr - MathHelper.Pi), Vector2.UnitY * 20, tr, miss,
            Properties.ShellType == ShellID.Explosive ? ExplosivePathPattern : null, bounce);

        if (Parameters.PredictsPositions) {
            var calculation = Position.Distance(TargetTank.Position) / (float)(Properties.ShellSpeed * 1.2f);
            float rot = -Position.DirectionTo(GeometryUtils.PredictFuturePosition(TargetTank.Position, TargetTank.Velocity, calculation))
                .ToRotation() - MathHelper.PiOver2;
            CacheAim(Vector2.UnitY.Rotate(-Position.DirectionTo(TargetTank.Position).ToRotation() - MathHelper.PiOver2),
                Vector2.Zero, tr, miss, null, bounce);
            CacheAim(Vector2.UnitY.Rotate(rot), Vector2.UnitY * 20, tr, miss, null, bounce);
        }
        if (Parameters.SmartRicochets && !(CurShootCooldown > 0 || OwnedShellCount >= Properties.ShellLimit))
            CacheAim(Vector2.UnitY.Rotate(_seekRotation + Parameters.TurretSpeed * 0.25f), default, tr, miss, null, bounce);
    }

    /// <summary>1.9.1: traces where a shot would go by stepping a <see cref="ShellSim"/> (the same physics as a real shell),
    /// noting the tanks it would hit or pass close to. Safe on worker threads (no shared buffers).</summary>
    private List<Tank> TracePath(Vector2 pathDir, out Vector2[] ricochetPoints, out Vector2[] tankCollPoints, float turretRot,
        bool draw = false, Vector2 offset = default, float missDist = 0f, Func<Block, bool>? pattern = null, bool doBounceReset = true) {
        // upper bound on ticks simulated
        const int MAX_PATH_FRAMES = 1000;
        // a step used to be 8 units: kept, so DetectionForgiveness still means the same thing
        const float FORGIVENESS_UNIT = 8f;

        List<Tank> tanks = [];
        List<Vector2> ricoPoints = [];
        List<Vector2> tnkPoints = [];

        pattern ??= c => c.Properties.IsSolid || c.Type == BlockID.Teleporter;
        var whitePixel = draw ? TextureGlobals.Pixels[Color.White] : null!; // debug drawing only (keeps worker threads off shared lookups)

        pathDir.Y *= -1;
        var sim = ShellSim.From(this, Position + offset.Rotate(-turretRot), pathDir);

        bool teleported = false;
        Vector2? teleportTo = null;

        for (int i = 0; i < MAX_PATH_FRAMES; i++) {
            // teleport on the tick after touching the pad
            if (teleportTo is { } to) {
                sim.Position = to;
                teleportTo = null;
            }

            // check tanks BEFORE moving
            float realMiss = 1f + missDist * 2 * (sim.Distance / FORGIVENESS_UNIT);
            foreach (var enemy in GameHandler.AllTanks) {
                if (enemy is null || enemy.IsDestroyed || tanks.Contains(enemy)) continue;

                if (i > 5 && GameUtils.Distance_WiiTanksUnits(enemy.Position, sim.Position) <= realMiss) {
                    var pathAngle = sim.Velocity.ToRotation();
                    var toEnemy = sim.Position.DirectionTo(enemy.Position).ToRotation();
                    if (MathUtils.AbsoluteAngleBetween(pathAngle, toEnemy) >= MathHelper.PiOver2)
                        tanks.Add(enemy);
                }

                var pathCircle = new Circle { Center = sim.Position, Radius = 4 };
                if (enemy.CollisionCircle.Intersects(pathCircle)) {
                    tnkPoints.Add(sim.Position);
                    tanks.Add(enemy);
                }
            }

            if (draw) {
                var screenPos = MatrixUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(sim.Position.X, 11, sim.Position.Y),
                    CameraGlobals.GameView, CameraGlobals.GameProjection);
                TankGame.SpriteRenderer.Draw(whitePixel, screenPos, null, Color.White * 0.5f, 0, whitePixel.Size() / 2, realMiss, default, default);
            }

            var result = sim.Step(pattern, out var block);

            if (block is { Type: BlockID.Teleporter } && !teleported) {
                var dest = Block.AllBlocks.FirstOrDefault(bl => bl != null && bl != block && bl.Type == BlockID.Teleporter && bl.TpLink == block.TpLink);
                if (dest is not null) {
                    teleported = true;
                    teleportTo = dest.Position;
                }
            }

            if (result == ShellSimResult.Destroyed) break;
            if (result == ShellSimResult.Ricochet) {
                ricoPoints.Add(sim.Position);
                if (doBounceReset) sim.Distance = 0f;
            }
        }

        tankCollPoints = [.. tnkPoints];
        ricochetPoints = [.. ricoPoints];
        return tanks;
    }
}
