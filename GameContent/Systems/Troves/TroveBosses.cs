using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.Coordinates;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Graphics;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.Troves;

public enum BossKind {
    CaptainMarine, MamaBrown, Burrower, TwinAsh,
    VioletViper, JellyRoller, PepperPot,
    GreenGoliath, DrillSergeant, MagnetMaster, LaserLathe,
    BlackBaron, ArtilleryMajor, ShieldCommander,
    ObsidianOverlord, VaultKeeper, MirrorTank, GoldHoarder,
}

public sealed record BossDef(BossKind Kind, int Floor, string Name, int Tier, string Blurb);

/// <summary>
/// Tank Troves bosses: each floor picks one of its bosses (the same on every phone, from the run's seed), and each has
/// its own attacks on top of its tank's normal ones. Mini-bosses get one small trick.
/// The host (or a solo game) decides what a boss does; shells, mines, blasts and summons it makes are sent to the other
/// phones as Troves events so everyone sees the same fight. Positions come over the normal AI-tank sync.
/// </summary>
public static class TroveBosses {
    public static readonly BossDef[] All = [
        new(BossKind.CaptainMarine, 1, "Captain Marine", TankID.Marine, "3-shell spreads; dashes between corners when hurt"),
        new(BossKind.MamaBrown, 1, "Mama Brown", TankID.Brown, "Spawns little tanks and fires shell rings"),
        new(BossKind.Burrower, 1, "The Burrower", TankID.Ash, "Digs underground and pops up somewhere else"),
        new(BossKind.TwinAsh, 1, "Twin Ash", TankID.Ash, "Two of them, one health bar"),
        new(BossKind.VioletViper, 2, "The Violet Viper", TankID.Violet, "Mine rings, then charges at you"),
        new(BossKind.JellyRoller, 2, "Jelly Roller", TankID.Pink, "Bounces around the room, firing on every bounce"),
        new(BossKind.PepperPot, 2, "Pepper Pot", TankID.Yellow, "Slow homing shells and spinning sprays"),
        new(BossKind.GreenGoliath, 3, "Green Goliath", TankID.Green, "Rocket bursts; builds a second turret when hurt"),
        new(BossKind.DrillSergeant, 3, "Drill Sergeant", TankID.Pink, "Drills through blocks and rams you"),
        new(BossKind.MagnetMaster, 3, "Magnet Master", TankID.Violet, "Pulls your shells in and fires them back"),
        new(BossKind.LaserLathe, 3, "Laser Lathe", TankID.Bronze, "Aims a red line, then fires a piercing laser"),
        new(BossKind.BlackBaron, 4, "The Black Baron", TankID.Black, "Fast, leaves mines; fires double rockets when hurt"),
        new(BossKind.ArtilleryMajor, 4, "Artillery Major", TankID.Ash, "Shells land where you stand (watch for red targets)"),
        new(BossKind.ShieldCommander, 4, "Shield Commander", TankID.Violet, "Shield on its front: hit it from the side or bounce shots"),
        new(BossKind.ObsidianOverlord, 5, "Obsidian Overlord", TankID.Obsidian, "Three phases: spreads, mines and summons, spinning rings"),
        new(BossKind.VaultKeeper, 5, "The Vault Keeper", TankID.Amethyst, "Teleports, leaves mines, and copies one of your item tricks"),
        new(BossKind.MirrorTank, 5, "Mirror Tank", TankID.Black, "A dark copy of you with your shell items"),
        new(BossKind.GoldHoarder, 5, "Gold Hoarder", TankID.Gold, "Gold shells split when they bounce; drops an item when hurt"),
    ];
    public static BossDef Def(BossKind k) => All[(int)k];

    /// <summary>The boss this floor has in this run (the same on every phone).</summary>
    /// <summary>For automated testing: always this boss (when it's on the floor being built).</summary>
    public static BossKind? TestKind;
    public static BossDef PickFor(int floor, int seed) {
        if (TestKind is { } tk && Def(tk).Floor == floor)
            return Def(tk);
        var options = All.Where(b => b.Floor == floor).ToArray();
        var rng = new Random(unchecked(seed * 7349 + floor * 151));
        return options[rng.Next(options.Length)];
    }

    // ------------------------------------------------------------------ the fight in progress

    public static BossDef? Current { get; private set; }
    public static AITank? Boss { get; private set; }
    public static AITank? Twin { get; private set; }
    private static bool _mini;
    private static int _floor, _seed;
    private static readonly Dictionary<string, double> _timers = [];
    private static readonly List<AITank> _minions = [];
    private static readonly List<(double At, float Angle, int Code)> _scheduledShots = [];
    private static bool _enraged, _halfDone, _burrowed;
    private static int _trick; // Vault Keeper's copied item trick
    private static Vector2 _dashFrom, _dashTo;
    private static double _dashStart = -1, _dashEnd = -1;
    private static Vector2 _bounceVel;

    /// <summary>Red warning lines (Laser Lathe): from, angle, when it fires.</summary>
    public static readonly List<(Vector2 From, float Angle, double FireAt)> Lines = [];
    /// <summary>Red target circles (Artillery Major): where, when it lands.</summary>
    public static readonly List<(Vector2 At, double LandAt)> Targets = [];

    private static double Now => TroveRun.PlayTime;
    private static float Dt => TanksRebirth.RuntimeData.DeltaTime;
    private static bool Authority => !Client.IsConnected() || Client.IsHost();

    public static void Clear() {
        Current = null;
        Boss = Twin = null;
        _mini = false;
        _timers.Clear();
        _minions.Clear();
        _scheduledShots.Clear();
        Lines.Clear();
        Targets.Clear();
        _enraged = _halfDone = _burrowed = false;
        _dashStart = _dashEnd = -1;
    }

    /// <summary>Is this tank (one of) the boss(es) of this room?</summary>
    public static bool IsBoss(Tank t) => t is not null && (ReferenceEquals(t, Boss) || ReferenceEquals(t, Twin));

    /// <summary>Hits left across the boss (and its twin), for the bar at the top.</summary>
    public static int HitsLeft() {
        int h = 0;
        foreach (var t in new[] { Boss, Twin })
            if (t is { IsDestroyed: false })
                h += (t.Properties.Armor?.HitPoints ?? 0) + 1;
        return h;
    }
    public static int MaxHits { get; private set; }

    private static float Fraction => MaxHits <= 0 ? 1f : HitsLeft() / (float)MaxHits;

    /// <summary>A floor's boss room just loaded (on every phone): give the boss its stats and set its fight up.</summary>
    public static void Setup(AITank boss, int floor, int seed, bool mini) {
        Clear();
        _floor = floor;
        _seed = seed;
        _mini = mini;
        Boss = boss;
        if (mini) {
            MaxHits = HitsLeft();
            return;
        }
        var def = PickFor(floor, seed);
        Current = def;
        var rng = new Random(unchecked(seed * 31 + floor));
        var props = boss.Properties;
        switch (def.Kind) {
            case BossKind.MamaBrown:
            case BossKind.GreenGoliath:
            case BossKind.LaserLathe:
            case BossKind.ArtilleryMajor:
                props.Stationary = true;
                if (def.Kind is BossKind.LaserLathe or BossKind.ArtilleryMajor)
                    props.ShellLimit = def.Kind == BossKind.ArtilleryMajor ? 2 : 0;
                break;
            case BossKind.JellyRoller:
            case BossKind.DrillSergeant:
            case BossKind.ShieldCommander:
            case BossKind.MirrorTank:
                props.Stationary = true; // these move the way their fight says, not by the usual AI
                _bounceVel = new Vector2(1.6f, 1.2f);
                break;
            case BossKind.TwinAsh: {
                // a second, equal tank next to it; they share the health bar
                int armor = Math.Max(1, (props.Armor?.HitPoints ?? 4) / 2);
                props.Armor = new TankArmor(boss, armor);
                boss.Scaling = new Vector3(1.2f);
                var at = FreeSpotNear(boss.Position, 60f);
                var t = new TankTemplate { IsPlayer = false, AiTier = TankID.Ash, Position = at, Team = TeamID.Blue }.GetAiTank();
                t.Team = TeamID.Blue;
                t.Scaling = new Vector3(1.2f);
                t.Properties.Armor = new TankArmor(t, armor);
                t.Properties.ShellLimit += 1;
                Twin = t;
                break;
            }
            case BossKind.VaultKeeper:
                _trick = rng.Next(3);
                if (_trick == 0) {
                    props.ShellShootCount = 3;
                    props.ShellSpread = 0.22f;
                    props.ShellLimit *= 3;
                }
                break;
        }
        MaxHits = HitsLeft();
    }

    private static bool Every(string key, double seconds) {
        if (!_timers.TryGetValue(key, out var at)) {
            _timers[key] = Now + seconds * 0.6; // the first one comes a little sooner
            return false;
        }
        if (Now < at)
            return false;
        _timers[key] = Now + seconds;
        return true;
    }

    // ------------------------------------------------------------------ every update

    public static void Update() {
        if (!TroveRun.Active || !CampaignGlobals.InMission || Boss is null)
            return;
        // the boss is gone: its helpers go with it (the host clears them out so the room can finish)
        bool bossDown = Boss.IsDestroyed && Twin is not { IsDestroyed: false };
        if (bossDown) {
            if (Authority)
                foreach (var m in _minions)
                    if (!m.IsDestroyed)
                        m.Destroy(new TankHurtContextOther(null, TankHurtContextOther.HurtContext.FromIngame, "Boss defeated"), true);
            _minions.Clear();
            Lines.Clear();
            Targets.Clear();
            return;
        }
        _minions.RemoveAll(m => m.IsDestroyed);

        // things every phone does the same way
        RunScheduled();
        if (Current?.Kind == BossKind.MagnetMaster)
            PullShells();
        if (Current?.Kind == BossKind.DrillSergeant && !Boss.IsDestroyed)
            DrillBlocks();
        if (Current?.Kind == BossKind.TwinAsh && !_enraged && (Boss.IsDestroyed || Twin is { IsDestroyed: true })) {
            // one twin down: the other gets angry
            _enraged = true;
            var left = Boss.IsDestroyed ? Twin : Boss;
            if (left is not null) {
                left.Properties.MaxSpeed *= 1.6f;
                left.Properties.ShellCooldown = Math.Max(1, left.Properties.ShellCooldown / 2);
                left.Properties.ShellLimit += 2;
                GameHandler.Particles.MakeShineSpot(left.Position3D + new Vector3(0, 12, 0), Color.Red, 1.6f);
            }
        }
        if (Current?.Kind == BossKind.BlackBaron && !_halfDone && Fraction <= 0.5f) {
            _halfDone = true;
            Boss.Properties.ShellShootCount = 2;
            Boss.Properties.ShellSpread = 0.16f;
            Boss.Properties.ShellLimit += 3;
        }

        if (!Authority || Frozen || TroveRun.InReady)
            return;
        if (_mini)
            MiniTrick();
        else if (Current is { } def)
            Brain(def.Kind);
    }

    private static bool Frozen => TroveItems.Frozen;

    private static PlayerTank? NearestPlayer(Vector2 from) =>
        GameHandler.AllPlayerTanks.Where(p => p is { IsDestroyed: false }).OrderBy(p => Vector2.Distance(p!.Position, from)).FirstOrDefault();

    private static float AngleTo(Vector2 from, Vector2 to) => MathF.Atan2(to.Y - from.Y, to.X - from.X);

    private static void Brain(BossKind kind) {
        var b = Boss!;
        var target = NearestPlayer(b.Position);
        if (target is null)
            return;
        float aim = AngleTo(b.Position, target.Position);
        bool hurt = Fraction <= 0.5f;
        switch (kind) {
            case BossKind.CaptainMarine:
                if (Every("spread", hurt ? 2.2 : 3))
                    Spread(b, aim, 3, 0.22f, 6);
                if (hurt && Every("dash", 4)) {
                    var corners = new[] { new Vector2(-170, -120), new Vector2(170, -120), new Vector2(-170, 120), new Vector2(170, 120) };
                    var to = corners.OrderByDescending(c => Vector2.Distance(c, b.Position)).ElementAt(Random.Shared.Next(2));
                    StartDash(b, FreeSpotNear(to, 40f), 0.7);
                }
                DoDash(b);
                break;
            case BossKind.MamaBrown:
                if (Every("ring", 4))
                    Ring(b, 6, (float)Now, 5);
                if (Every("spawn", 8) && _minions.Count < 3)
                    SendAct(ACT_SPAWN_BROWN, FreeSpotNear(b.Position, 55f));
                break;
            case BossKind.Burrower:
                if (!_burrowed && Every("burrow", 5)) {
                    SendAct(ACT_BURROW, b.Position);
                    _timers["popup"] = Now + 1.8;
                }
                if (_burrowed && _timers.TryGetValue("popup", out var pop) && Now >= pop) {
                    var spot = RandomFreeSpot(target.Position, 90f);
                    b.Position = spot;
                    SendAct(ACT_POPUP, spot);
                    for (int i = 0; i < 4; i++)
                        Fire(b, i * MathHelper.PiOver2, 6);
                }
                break;
            case BossKind.TwinAsh:
                // two tanks with the normal AI; the angry one is handled above
                break;
            case BossKind.VioletViper:
                if (Every("mines", 6)) {
                    for (int i = 0; i < 6; i++)
                        PlaceMine(b.Position + Vector2.UnitX.Rotate(i * MathHelper.TwoPi / 6f) * 45f);
                    _timers["charge"] = Now + 0.7;
                }
                if (_timers.TryGetValue("charge", out var ch) && Now >= ch) {
                    _timers.Remove("charge");
                    var to = b.Position + Vector2.Normalize(target.Position - b.Position) * Math.Min(220f, Vector2.Distance(b.Position, target.Position));
                    StartDash(b, to, 0.9);
                }
                if (hurt && _dashEnd > Now && Every("trail", 0.35) && Mine.AllMines.Count(m => m is not null && ReferenceEquals(m.Owner, b)) < 10)
                    PlaceMine(b.Position);
                DoDash(b);
                break;
            case BossKind.JellyRoller: {
                float speed = hurt ? 3f : 2.2f;
                if (_bounceVel.LengthSquared() < 0.1f)
                    _bounceVel = new Vector2(1, 0.8f);
                _bounceVel = Vector2.Normalize(_bounceVel) * speed;
                var next = b.Position + _bounceVel * Dt * 0.62f;
                bool bounced = false;
                if (next.X < GameScene.TANKS_MIN_X + 10 || next.X > GameScene.TANKS_MAX_X - 10 || Solid(new Vector2(next.X, b.Position.Y), 16f)) {
                    _bounceVel.X = -_bounceVel.X;
                    bounced = true;
                }
                if (next.Y < GameScene.TANKS_MIN_Y + 10 || next.Y > GameScene.TANKS_MAX_Y - 10 || Solid(new Vector2(b.Position.X, next.Y), 16f)) {
                    _bounceVel.Y = -_bounceVel.Y;
                    bounced = true;
                }
                if (!bounced)
                    b.Position = next;
                b.ChassisRotation = MathF.Atan2(_bounceVel.Y, _bounceVel.X) - MathHelper.PiOver2;
                if (bounced && Every("bounceShot", 0.25))
                    Fire(b, aim, 7);
                break;
            }
            case BossKind.PepperPot:
                if (Every("homing", hurt ? 1.6 : 2.2))
                    Fire(b, aim, SHOT_HOMING);
                if (Every("spray", 10)) {
                    for (int i = 0; i < 16; i++)
                        _scheduledShots.Add((Now + i * 0.1, aim + i * MathHelper.TwoPi / 16f * 1.5f, 5));
                }
                break;
            case BossKind.GreenGoliath:
                if (Every("burst", hurt ? 3 : 4))
                    Spread(b, aim, 3, 0.25f, 9);
                if (hurt && !_halfDone) {
                    _halfDone = true;
                    SendAct(ACT_SPAWN_TURRET, RandomFreeSpot(target.Position, 110f));
                }
                break;
            case BossKind.DrillSergeant: {
                var dir = Vector2.Normalize(target.Position - b.Position);
                float speed = (hurt ? 1.8f : 1.3f) * Dt * 0.62f;
                var nx = b.Position + new Vector2(dir.X * speed, 0);
                if (!SolidHard(nx, 16f)) b.Position = nx;
                var ny = b.Position + new Vector2(0, dir.Y * speed);
                if (!SolidHard(ny, 16f)) b.Position = ny;
                b.ChassisRotation = MathF.Atan2(dir.Y, dir.X) - MathHelper.PiOver2;
                if (Vector2.Distance(b.Position, target.Position) < 32f && Every("ram", 1.2))
                    target.Damage(new TankHurtContextOther(b, TankHurtContextOther.HurtContext.FromIngame, "Rammed by the Drill Sergeant"), true);
                break;
            }
            case BossKind.MagnetMaster:
                // the pull and catch happen on every phone (PullShells); its own shots are the usual AI
                break;
            case BossKind.LaserLathe:
                if (Every("laser", hurt ? 2.8 : 3.5)) {
                    SendAct(ACT_LASER_LINE, b.Position, AngleCode(aim));
                    if (hurt) {
                        SendAct(ACT_LASER_LINE, b.Position, AngleCode(aim + 0.3f));
                        SendAct(ACT_LASER_LINE, b.Position, AngleCode(aim - 0.3f));
                    }
                }
                break;
            case BossKind.BlackBaron:
                if (b.Velocity.LengthSquared() > 0.2f && Every("drop", 1.5) && Mine.AllMines.Count(m => m is not null && ReferenceEquals(m.Owner, b)) < 6)
                    PlaceMine(b.Position);
                break;
            case BossKind.ArtilleryMajor:
                if (Every("shell", hurt ? 2.2 : 3)) {
                    SendAct(ACT_ARTILLERY, target.Position);
                    if (hurt) {
                        var other = GameHandler.AllPlayerTanks.FirstOrDefault(p => p is { IsDestroyed: false } && !ReferenceEquals(p, target));
                        SendAct(ACT_ARTILLERY, other?.Position ?? target.Position + target.Velocity * 40f);
                    }
                }
                break;
            case BossKind.ShieldCommander: {
                // walks slowly at you, always facing you (so the shield is in the way)
                var dir = Vector2.Normalize(target.Position - b.Position);
                if (Vector2.Distance(b.Position, target.Position) > 70f) {
                    var step = b.Position + dir * (hurt ? 0.9f : 0.6f) * Dt * 0.62f;
                    if (!Solid(step, 16f))
                        b.Position = step;
                }
                b.ChassisRotation = MathF.Atan2(dir.Y, dir.X) - MathHelper.PiOver2;
                if (Every("volley", hurt ? 2.5 : 3.5))
                    Spread(b, aim, 2, 0.15f, 6);
                break;
            }
            case BossKind.ObsidianOverlord:
                if (Fraction > 0.66f) {
                    if (Every("spread", 3))
                        Spread(b, aim, 5, 0.18f, 7);
                }
                else if (Fraction > 0.33f) {
                    if (Every("spread", 4))
                        Spread(b, aim, 5, 0.18f, 7);
                    if (Every("summon", 9)) {
                        for (int i = 0; i < 8; i++)
                            PlaceMine(b.Position + Vector2.UnitX.Rotate(i * MathHelper.TwoPi / 8f) * 55f);
                        for (int i = 0; i < 2 && _minions.Count + i < 3; i++)
                            SendAct(ACT_SPAWN_BLACK, FreeSpotNear(b.Position, 70f));
                    }
                }
                else {
                    if (Every("spread", 3))
                        Spread(b, aim, 5, 0.18f, 7);
                    if (Every("spin", 6))
                        for (int i = 0; i < 24; i++)
                            _scheduledShots.Add((Now + i * 0.08, (float)Now + i * MathHelper.TwoPi / 12f, 6));
                }
                break;
            case BossKind.VaultKeeper:
                if (Every("teleport", hurt ? 3 : 4)) {
                    PlaceMine(b.Position + new Vector2(-14, 0));
                    PlaceMine(b.Position + new Vector2(14, 0));
                    b.Position = RandomFreeSpot(target.Position, 120f);
                    SendAct(ACT_SPARKLE, b.Position);
                }
                if (_trick == 1 && Every("burst", 5))
                    Ring(b, 8, 0f, 6);
                break;
            case BossKind.MirrorTank: {
                // stands where you are, mirrored across the middle of the room
                var want = new Vector2(-target.Position.X, target.Position.Y);
                var to = want - b.Position;
                float d = to.Length();
                if (d > 2f) {
                    var step = b.Position + to / d * Math.Min(d, 2.2f * Dt * 0.62f);
                    if (!Solid(step, 16f))
                        b.Position = step;
                }
                b.ChassisRotation = target.ChassisRotation;
                break;
            }
            case BossKind.GoldHoarder:
                if (hurt && !_halfDone) {
                    _halfDone = true;
                    var rng = new Random(unchecked(_seed * 17 + _floor));
                    SendAct(ACT_GOLD_DROP, Vector2.Zero, (int)TroveItems.SecretPool[rng.Next(TroveItems.SecretPool.Length)]);
                }
                if (Every("gold", 3.5))
                    Spread(b, aim, 2, 0.3f, SHOT_GOLD);
                break;
        }
    }

    /// <summary>Mini-bosses: one trick each, by floor.</summary>
    private static void MiniTrick() {
        var b = Boss!;
        var target = NearestPlayer(b.Position);
        if (target is null || !Every("trick", 5))
            return;
        float aim = AngleTo(b.Position, target.Position);
        switch (_floor) {
            case 1: Spread(b, aim, 3, 0.2f, 6); break;
            case 2:
                for (int i = 0; i < 4; i++)
                    PlaceMine(b.Position + Vector2.UnitX.Rotate(i * MathHelper.PiOver2 + 0.78f) * 45f);
                break;
            case 3: Spread(b, aim, 2, 0.15f, 9); break;
            default:
                StartDash(b, b.Position + Vector2.Normalize(target.Position - b.Position) * 150f, 0.6);
                Spread(b, aim, 3, 0.2f, 7);
                break;
        }
        DoDash(b);
    }

    // ------------------------------------------------------------------ moves

    private static void StartDash(AITank b, Vector2 to, double seconds) {
        _dashFrom = b.Position;
        _dashTo = new Vector2(Math.Clamp(to.X, GameScene.TANKS_MIN_X + 15, GameScene.TANKS_MAX_X - 15), Math.Clamp(to.Y, GameScene.TANKS_MIN_Y + 15, GameScene.TANKS_MAX_Y - 15));
        _dashStart = Now;
        _dashEnd = Now + seconds;
    }
    private static void DoDash(AITank b) {
        if (_dashEnd < 0 || Now > _dashEnd) {
            _dashEnd = -1;
            return;
        }
        float t = (float)((Now - _dashStart) / (_dashEnd - _dashStart));
        var next = Vector2.Lerp(_dashFrom, _dashTo, Math.Clamp(t, 0, 1));
        if (SolidHard(next, 14f)) {
            _dashEnd = -1; // ran into a wall: stop there
            return;
        }
        b.Position = next;
        var dir = _dashTo - _dashFrom;
        if (dir.LengthSquared() > 1)
            b.ChassisRotation = MathF.Atan2(dir.Y, dir.X) - MathHelper.PiOver2;
    }

    private static bool Solid(Vector2 p, float r) =>
        Block.AllBlocks.Any(bl => bl is not null && bl.Properties.IsSolid && MathF.Abs(bl.Position.X - p.X) < Block.SIDE_LENGTH / 2 + r && MathF.Abs(bl.Position.Y - p.Y) < Block.SIDE_LENGTH / 2 + r);
    private static bool SolidHard(Vector2 p, float r) =>
        Block.AllBlocks.Any(bl => bl is not null && bl.Properties.IsSolid && !bl.Properties.IsDestructible
            && MathF.Abs(bl.Position.X - p.X) < Block.SIDE_LENGTH / 2 + r && MathF.Abs(bl.Position.Y - p.Y) < Block.SIDE_LENGTH / 2 + r);

    private static Vector2 FreeSpotNear(Vector2 at, float dist) {
        for (int i = 0; i < 12; i++) {
            var p = at + Vector2.UnitX.Rotate(i * MathHelper.TwoPi / 12f) * dist;
            if (p.X > GameScene.TANKS_MIN_X + 15 && p.X < GameScene.TANKS_MAX_X - 15 && p.Y > GameScene.TANKS_MIN_Y + 15 && p.Y < GameScene.TANKS_MAX_Y - 15 && !Solid(p, 12f))
                return p;
        }
        return at;
    }

    /// <summary>A random open spot at least minFromPlayer away from the given player position.</summary>
    private static Vector2 RandomFreeSpot(Vector2 player, float minFromPlayer) {
        for (int i = 0; i < 60; i++) {
            var p = new BlockMapPosition(Random.Shared.Next(1, BlockMapPosition.MAP_WIDTH_169 - 1), Random.Shared.Next(1, BlockMapPosition.MAP_HEIGHT - 1));
            Vector2 v = p;
            if (!Solid(v, 12f) && Vector2.Distance(v, player) >= minFromPlayer)
                return v;
        }
        return Boss?.Position ?? Vector2.Zero;
    }

    /// <summary>Drill Sergeant: breakable blocks it touches break (on every phone, from its synced position).</summary>
    private static void DrillBlocks() {
        foreach (var bl in Block.AllBlocks)
            if (bl is not null && bl.Properties.IsDestructible && Vector2.Distance(bl.Position, Boss!.Position) < 26f)
                bl.Destroy();
    }

    /// <summary>Magnet Master: players' shells curve toward it, and the ones that get close are caught (and the host fires one back).</summary>
    private static void PullShells() {
        var b = Boss!;
        if (b.IsDestroyed)
            return;
        foreach (var s in Shell.AllShells) {
            if (s is null || s.Owner is null || s.Owner.Team != TeamID.Red)
                continue;
            float d = Vector2.Distance(s.Position, b.Position);
            if (d > 170f)
                continue;
            if (d < 34f) {
                s.Destroy(Shell.DestructionContext.WithShell);
                if (Authority && NearestPlayer(b.Position) is { } p)
                    Fire(b, AngleTo(b.Position, p.Position), 8);
                continue;
            }
            float speed = s.Velocity.Length();
            var want = Vector2.Normalize(b.Position - s.Position) * speed;
            s.Velocity = Vector2.Normalize(Vector2.Lerp(s.Velocity, want, 0.045f * Dt)) * speed;
        }
    }

    // ------------------------------------------------------------------ attacks (made on the host, sent to the others)

    // shot codes: 0..11 = speed 1 + code*0.5; special codes below
    private const int SHOT_HOMING = 12, SHOT_LASER = 13, SHOT_GOLD = 14;

    private static void Spread(AITank b, float aim, int n, float spread, int code) {
        for (int i = 0; i < n; i++)
            Fire(b, aim + (i - (n - 1) / 2f) * spread, code);
    }
    private static void Ring(AITank b, int n, float offset, int code) {
        for (int i = 0; i < n; i++)
            Fire(b, offset + i * MathHelper.TwoPi / n, code);
    }

    private static void Fire(AITank b, float angle, int code) {
        var from = b.Position + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 22f * b.Scaling.X;
        MakeShot(from, angle, code);
        if (Client.IsConnected())
            Client.SendTroveEvent(TroveRun.EV_BOSS_SHOT, PackShot(from, angle, code));
    }

    private static void MakeShot(Vector2 from, float angle, int code) {
        var b = Boss;
        if (b is null)
            return;
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        float speed = code switch { SHOT_HOMING => 1.6f, SHOT_LASER => 8f, SHOT_GOLD => 3.2f, _ => 1f + code * 0.5f };
        int type = code == SHOT_LASER ? ShellID.TrailedRocket : b.Properties.ShellType;
        uint ric = code == SHOT_LASER ? 0u : Math.Max(1u, b.Properties.RicochetCount);
        var homing = code == SHOT_HOMING ? new Shell.HomingProperties { Radius = 260f, Speed = speed, Power = 0.06f * speed } : default;
        var s = new Shell(from, dir * speed, type, b, ric, homing);
        if (code == SHOT_LASER) {
            s.Pierce = 3;
            s.BreaksCork = true;
            s.VisualScale = 1.3f;
        }
        if (code == SHOT_GOLD)
            s.SplitsOnBounce = true;
    }

    private static void PlaceMine(Vector2 at) {
        MakeMine(at);
        if (Client.IsConnected())
            Client.SendTroveEvent(TroveRun.EV_BOSS_MINE, TroveItems.EncodePos(at));
    }
    private static void MakeMine(Vector2 at) {
        if (Boss is not { } b)
            return;
        new Mine(b, at, 400);
        b.OwnedMineCount++;
    }

    private static int PackShot(Vector2 p, float angle, int code) {
        int x = Math.Clamp((int)(p.X + 512), 0, 1023), y = Math.Clamp((int)(p.Y + 512), 0, 1023);
        int a = (int)MathF.Round((angle % MathHelper.TwoPi + MathHelper.TwoPi) % MathHelper.TwoPi / MathHelper.TwoPi * 256f) & 255;
        return (int)((uint)x | (uint)y << 10 | (uint)a << 20 | (uint)(code & 15) << 28);
    }
    private static (Vector2 P, float Angle, int Code) UnpackShot(int v) {
        uint u = (uint)v;
        return (new Vector2((int)(u & 1023) - 512, (int)(u >> 10 & 1023) - 512), (u >> 20 & 255) / 256f * MathHelper.TwoPi, (int)(u >> 28 & 15));
    }

    private static int AngleCode(float a) => (int)MathF.Round((a % MathHelper.TwoPi + MathHelper.TwoPi) % MathHelper.TwoPi / MathHelper.TwoPi * 360f) % 360;

    // other things everyone must do together: act * 1,000,000 + packed position (and an extra number for some)
    private const int ACT_SPAWN_BROWN = 1, ACT_SPAWN_TURRET = 2, ACT_SPAWN_BLACK = 3, ACT_BURROW = 4, ACT_POPUP = 5,
        ACT_LASER_LINE = 6, ACT_ARTILLERY = 7, ACT_GOLD_DROP = 8, ACT_SPARKLE = 9;

    private static void SendAct(int act, Vector2 at, int extra = 0) {
        DoAct(act, at, extra);
        if (Client.IsConnected()) {
            // extra numbers (an angle or an item) ride in their own event first
            if (extra != 0)
                Client.SendTroveEvent(TroveRun.EV_BOSS_EXTRA, extra);
            Client.SendTroveEvent(TroveRun.EV_BOSS_ACT, act * 1_000_000 + TroveItems.EncodePos(at));
        }
    }
    private static int _pendingExtra;

    private static void DoAct(int act, Vector2 at, int extra) {
        switch (act) {
            case ACT_SPAWN_BROWN:
            case ACT_SPAWN_TURRET:
            case ACT_SPAWN_BLACK: {
                int tier = act == ACT_SPAWN_BROWN ? TankID.Brown : act == ACT_SPAWN_TURRET ? TankID.Green : TankID.Black;
                var t = new TankTemplate { IsPlayer = false, AiTier = tier, Position = at, Team = TeamID.Blue }.GetAiTank();
                t.Team = TeamID.Blue;
                if (act == ACT_SPAWN_BROWN)
                    t.Scaling = new Vector3(0.7f);
                if (act == ACT_SPAWN_TURRET)
                    t.Properties.Armor = new TankArmor(t, 2);
                _minions.Add(t);
                GameHandler.Particles.MakeSmallExplosion(new Vector3(at.X, 5, at.Y), 6, 6, 1f, 8);
                break;
            }
            case ACT_BURROW:
                if (Boss is { } b) {
                    _burrowed = true;
                    b.Properties.Invisible = true;
                    b.Properties.Immortal = true;
                    b.Properties.ShellLimit = 0;
                    GameHandler.Particles.MakeSmallExplosion(b.Position3D, 10, 8, 1.2f, 10);
                    SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_destroy.ogg", SoundContext.Effect, 0.4f, pitchOverride: -0.6f);
                }
                break;
            case ACT_POPUP:
                if (Boss is { } b2) {
                    _burrowed = false;
                    b2.Position = at;
                    b2.Properties.Invisible = false;
                    b2.Properties.Immortal = false;
                    b2.Properties.ShellLimit = 3;
                    GameHandler.Particles.MakeSmallExplosion(new Vector3(at.X, 5, at.Y), 10, 8, 1.2f, 10);
                }
                break;
            case ACT_LASER_LINE: {
                float angle = extra / 360f * MathHelper.TwoPi;
                Lines.Add((at, angle, Now + 1.0));
                SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.8f, pitchOverride: 0.9f);
                break;
            }
            case ACT_ARTILLERY:
                Targets.Add((at, Now + 1.3));
                break;
            case ACT_GOLD_DROP:
                if (TroveRun.Room is { } room) {
                    TroveRun.SetPickup((TroveItemId)extra);
                    room.ForcePickup = true;
                    ChatSystem.SendMessage("The Gold Hoarder dropped something!", Color.Gold);
                }
                break;
            case ACT_SPARKLE:
                GameHandler.Particles.MakeShineSpot(new Vector3(at.X, 12, at.Y), Color.MediumPurple, 1.5f);
                break;
        }
    }

    /// <summary>Lasers fire when their warning line is up; artillery lands when its target time comes (every phone).</summary>
    private static void RunScheduled() {
        for (int i = Lines.Count - 1; i >= 0; i--)
            if (Now >= Lines[i].FireAt) {
                var l = Lines[i];
                Lines.RemoveAt(i);
                if (Authority)
                    Fire(Boss!, l.Angle, SHOT_LASER);
            }
        for (int i = Targets.Count - 1; i >= 0; i--)
            if (Now >= Targets[i].LandAt) {
                var t = Targets[i];
                Targets.RemoveAt(i);
                new Explosion(t.At, 5f, Boss, 0.4f);
            }
        if (!Authority)
            return;
        for (int i = _scheduledShots.Count - 1; i >= 0; i--)
            if (Now >= _scheduledShots[i].At) {
                var s = _scheduledShots[i];
                _scheduledShots.RemoveAt(i);
                if (Boss is { IsDestroyed: false } b)
                    Fire(b, s.Angle, s.Code);
            }
    }

    /// <summary>Boss events from the host.</summary>
    public static void OnEvent(int ev, int a) {
        if (ev == TroveRun.EV_BOSS_SHOT) {
            var (p, angle, code) = UnpackShot(a);
            MakeShot(p, angle, code);
        }
        else if (ev == TroveRun.EV_BOSS_MINE)
            MakeMine(TroveItems.DecodePos(a));
        else if (ev == TroveRun.EV_BOSS_EXTRA)
            _pendingExtra = a;
        else if (ev == TroveRun.EV_BOSS_ACT) {
            DoAct(a / 1_000_000, TroveItems.DecodePos(a % 1_000_000), _pendingExtra);
            _pendingExtra = 0;
        }
    }

    // ------------------------------------------------------------------ hooks

    /// <summary>Shield Commander's front shield: shells from the players hitting its front just stop.</summary>
    public static bool ShieldBlocks(Shell s, Tank t) {
        if (Current?.Kind != BossKind.ShieldCommander || !ReferenceEquals(t, Boss) || s.Owner is null || s.Owner.Team != TeamID.Red)
            return false;
        var forward = Vector2.UnitY.Rotate(t.ChassisRotation);
        var toShell = s.Position - t.Position;
        if (toShell.LengthSquared() < 0.01f)
            return false;
        return Vector2.Dot(Vector2.Normalize(toShell), forward) > 0.3f;
    }

    /// <summary>Shells the bosses fire get their special touches (Gold Hoarder's split, Mirror Tank's copies of your items).</summary>
    public static void OnBossShoot(Tank tank, Shell shell) {
        if (!TroveRun.Active || !ReferenceEquals(tank, Boss) || Current is null)
            return;
        if (Current.Kind == BossKind.GoldHoarder)
            shell.SplitsOnBounce = true;
        if (Current.Kind == BossKind.MirrorTank)
            TroveItems.ModifyShell(0, shell);
    }

    /// <summary>Vault Keeper's copied Orbit Guard: two balls around it that stop your shells.</summary>
    public static IEnumerable<Vector2> BossOrbiters() {
        if (Current?.Kind != BossKind.VaultKeeper || _trick != 2 || Boss is not { IsDestroyed: false } b)
            yield break;
        for (int i = 0; i < 2; i++) {
            float ang = (float)Now * 2.5f + i * MathHelper.Pi;
            yield return b.Position + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 36f;
        }
    }
    public static void UpdateBossOrbiters() {
        foreach (var o in BossOrbiters().ToArray())
            foreach (var s in Shell.AllShells)
                if (s is not null && s.Owner is not null && s.Owner.Team == TeamID.Red && Vector2.Distance(s.Position, o) < 14f)
                    s.Destroy(Shell.DestructionContext.WithShell);
    }
}
