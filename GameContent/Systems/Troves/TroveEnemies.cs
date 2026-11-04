using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using tainicom.Aether.Physics2D.Dynamics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.Coordinates;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.Troves;

public enum EnemyVariant : byte { None, Scout, Brute, Splitter, Kamikaze, Gunner, Ghost, Medic, Sniper }
public enum GunnerKind { Triple, Homing, Explosive, Bouncy }

/// <summary>
/// Special versions of Tank Troves' enemies, on top of their normal tank type: tiny fast Scouts (in pairs), big armored
/// Brutes, Splitters that burst into two Scouts, Kamikazes that rush you and blow up, Gunners carrying an item (Triple
/// Shot, Homing, Explosive or Bouncy shells, glowing that colour), Ghosts that fade in and out and drive through
/// blocks, Medics that shield their friends, and Snipers that aim a red laser before a fast piercing shot.
/// Which enemy is which comes from the run's seed (the same on every phone); the host decides the moments that matter
/// (splits, explosions, shields, shots) and tells the other phones.
/// </summary>
public static class TroveEnemies {
    public static readonly string[] Names = ["", "Scout", "Brute", "Splitter", "Kamikaze", "Gunner", "Ghost", "Medic", "Sniper"];
    private static readonly Color[] GunnerColors = [new(255, 220, 60), new(80, 220, 255), new(255, 120, 30), new(110, 255, 120)];

    private sealed class Info {
        public EnemyVariant Variant;
        public GunnerKind Gunner;
        public bool Handled; // (splits and blasts happen once)
        public double FuseAt = -1;
        public double NextAct;
    }

    private static readonly Dictionary<Tank, Info> _info = [];
    /// <summary>The variant tanks in the order they were set up (the same on every phone), for events that name one.</summary>
    private static readonly List<Tank> _order = [];
    /// <summary>Sniper warning lines: from, angle, when it fires, who fires.</summary>
    public static readonly List<(Tank Sniper, float Angle, double FireAt)> SniperLines = [];
    private static double _lastSplitDeath = -10;

    private static double Now => TroveRun.PlayTime;
    private static float Dt => RuntimeData.DeltaTime;
    private static bool Authority => !Client.IsConnected() || Client.IsHost();

    public static EnemyVariant VariantOf(Tank t) => _info.TryGetValue(t, out var i) ? i.Variant : EnemyVariant.None;
    public static GunnerKind GunnerOf(Tank t) => _info.TryGetValue(t, out var i) ? i.Gunner : GunnerKind.Triple;
    public static IEnumerable<(Tank Tank, EnemyVariant Variant)> All => _info.Where(k => !k.Key.IsDestroyed).Select(k => (k.Key, k.Value.Variant));

    /// <summary>A splitter just went down: hold the room open a moment, so it isn't "cleared" before the little ones appear.</summary>
    public static bool HoldsRoom => Now - _lastSplitDeath < 1.5;

    // ------------------------------------------------------------------ which enemies are special (when a floor is made)

    /// <summary>Gives some of a room's enemies a variant (more of them deeper down). Scouts come in pairs.</summary>
    public static void AssignVariants(TroveFloor f, TroveRoom room, int seed) {
        room.Variants.Clear();
        if (room.Enemies.Count == 0)
            return;
        var rng = new Random(unchecked(seed * 977 + f.Number * 31337 + room.Cell.X * 211 + room.Cell.Y * 4099));
        double chance = 0.2 + 0.06 * f.Number;
        var pool = new List<EnemyVariant> { EnemyVariant.Scout, EnemyVariant.Scout, EnemyVariant.Splitter, EnemyVariant.Kamikaze, EnemyVariant.Gunner, EnemyVariant.Gunner };
        if (f.Number >= 2) pool.AddRange([EnemyVariant.Brute, EnemyVariant.Ghost]);
        if (f.Number >= 3) pool.AddRange([EnemyVariant.Medic, EnemyVariant.Sniper]);
        var blocked = new HashSet<Point>(room.Blocks.Select(b => new Point(b.X, b.Y)));
        int n = room.Enemies.Count;
        for (int i = 0; i < n; i++) {
            if (i == room.BossIndex || rng.NextDouble() >= chance)
                continue;
            var v = pool[rng.Next(pool.Count)];
            room.Variants[i] = v;
            if (v != EnemyVariant.Scout)
                continue;
            // a second scout next to it
            var e = room.Enemies[i];
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) {
                int x = e.X + dx, y = e.Y + dy;
                if (x < 1 || y < 1 || x > TroveGen.GW - 2 || y > TroveGen.GH - 2 || blocked.Contains(new Point(x, y)) || room.Enemies.Any(o => o.X == x && o.Y == y))
                    continue;
                room.Enemies.Add((x, y, e.Tier, e.Rotation));
                room.Variants[room.Enemies.Count - 1] = EnemyVariant.Scout;
                break;
            }
        }
    }

    // ------------------------------------------------------------------ a room's tanks just appeared

    public static void Clear() {
        _info.Clear();
        _order.Clear();
        SniperLines.Clear();
        _lastSplitDeath = -10;
    }

    /// <summary>Finds each special enemy's tank (by where it starts) and makes it special.</summary>
    public static void SetupRoom(TroveRoom room) {
        Clear();
        if (room.Cleared || room.Variants.Count == 0)
            return;
        var taken = new HashSet<Tank>();
        foreach (var (idx, v) in room.Variants.OrderBy(k => k.Key)) {
            if (idx < 0 || idx >= room.Enemies.Count)
                continue;
            Vector2 at = new BlockMapPosition(room.Enemies[idx].X, room.Enemies[idx].Y);
            var tank = GameHandler.AllAITanks.Where(t => t is { IsDestroyed: false } && t.Team != TeamID.Red && !taken.Contains(t) && !TroveBosses.IsBoss(t))
                .OrderBy(t => Vector2.Distance(t!.Position, at)).FirstOrDefault();
            if (tank is null || Vector2.Distance(tank.Position, at) > 40f)
                continue;
            taken.Add(tank);
            Make(tank, v, idx);
        }
    }

    /// <summary>The Hive's swarm: a tiny, fast scout.</summary>
    public static void MakeScout(AITank t) => Make(t, EnemyVariant.Scout, _order.Count);

    private static void Make(AITank t, EnemyVariant v, int salt) {
        var info = new Info { Variant = v, Gunner = (GunnerKind)((salt * 7 + t.AiTankType) % 4), NextAct = Now + 2 + salt % 3 };
        _info[t] = info;
        _order.Add(t);
        var p = t.Properties;
        switch (v) {
            case EnemyVariant.Scout:
                t.Scaling = new Vector3(0.6f);
                p.MaxSpeed = Math.Max(p.MaxSpeed, 1.2f) * 1.8f;
                p.Acceleration *= 1.6f;
                p.TurningSpeed *= 1.5f;
                p.MaximalTurn *= 1.5f;
                p.Stationary = false;
                p.ShellLimit = 1;
                p.Armor = null;
                break;
            case EnemyVariant.Brute:
                t.Scaling = new Vector3(1.4f);
                p.MaxSpeed *= 0.6f;
                p.Armor = new TankArmor(t, 2);
                break;
            case EnemyVariant.Splitter:
                t.Scaling = new Vector3(1.15f);
                break;
            case EnemyVariant.Kamikaze:
                t.Scaling = new Vector3(0.85f);
                p.Stationary = true; // it moves the way this file says
                p.ShellLimit = 0;
                p.MineLimit = 0;
                break;
            case EnemyVariant.Gunner:
                switch (info.Gunner) {
                    case GunnerKind.Triple:
                        p.ShellShootCount = 3;
                        p.ShellSpread = 0.22f;
                        p.ShellLimit = Math.Max(3, p.ShellLimit * 3);
                        break;
                    case GunnerKind.Homing:
                        p.ShellHoming = new Shell.HomingProperties { Radius = 200f, Speed = Math.Max(1f, p.ShellSpeed), Power = 0.05f * Math.Max(1f, p.ShellSpeed) };
                        break;
                    case GunnerKind.Bouncy:
                        p.RicochetCount += 2;
                        break;
                }
                break;
            case EnemyVariant.Ghost:
                t.Physics?.SetCollidesWith(Category.All & ~Category.Cat1 & ~Category.Cat2);
                break;
            case EnemyVariant.Medic:
                p.ShellLimit = Math.Min(p.ShellLimit, 1);
                break;
            case EnemyVariant.Sniper:
                p.Stationary = true;
                p.ShellLimit = 0; // it only fires its aimed shots
                p.MineLimit = 0;
                break;
        }
    }

    // ------------------------------------------------------------------ every update

    public static void Update() {
        if (!TroveRun.Active || !CampaignGlobals.InMission || _info.Count == 0)
            return;
        bool frozen = TroveRun.InReady || TroveItems.Frozen;
        foreach (var (t, info) in _info.ToArray()) {
            if (t.IsDestroyed) {
                if (info.Variant == EnemyVariant.Splitter && !info.Handled) {
                    info.Handled = true;
                    _lastSplitDeath = Now;
                    if (Authority)
                        Send(ACT_SPLIT, TroveItems.EncodePos(t.Position));
                }
                continue;
            }
            switch (info.Variant) {
                case EnemyVariant.Ghost:
                    // fades in and out (worked out from the clock, so every phone agrees)
                    t.Properties.Invisible = (Now + _order.IndexOf(t) * 0.7) % 4.0 > 2.6;
                    break;
                case EnemyVariant.Kamikaze:
                    if (!frozen)
                        Kamikaze(t, info);
                    break;
                case EnemyVariant.Medic:
                    if ((int)(Now * 2) % 3 == 0 && Random.Shared.NextDouble() < 0.1 * Dt)
                        GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, 16, 0), Color.LimeGreen, 0.6f);
                    if (!frozen && Authority && Now >= info.NextAct) {
                        info.NextAct = Now + 6;
                        // shields for the two nearest friends that don't have one
                        foreach (var f in GameHandler.AllAITanks.Where(o => o is { IsDestroyed: false } && !ReferenceEquals(o, t) && o.Team != TeamID.Red
                                     && (o.Properties.Armor?.HitPoints ?? 0) < 2 && Vector2.Distance(o.Position, t.Position) < 170f)
                                     .OrderBy(o => Vector2.Distance(o!.Position, t.Position)).Take(2).ToArray())
                            Send(ACT_SHIELD, TroveItems.EncodePos(f!.Position));
                    }
                    break;
                case EnemyVariant.Sniper:
                    if (!frozen && Authority && Now >= info.NextAct && NearestPlayer(t.Position) is { } target) {
                        info.NextAct = Now + 4;
                        float ang = MathF.Atan2(target.Position.Y - t.Position.Y, target.Position.X - t.Position.X);
                        int idx = _order.IndexOf(t);
                        Send(ACT_AIM, idx * 1000 + (int)MathF.Round((ang % MathHelper.TwoPi + MathHelper.TwoPi) % MathHelper.TwoPi / MathHelper.TwoPi * 360f) % 360);
                    }
                    break;
            }
        }
        // snipers fire when their line has been up for a moment (every phone makes the shot)
        for (int i = SniperLines.Count - 1; i >= 0; i--) {
            var l = SniperLines[i];
            if (l.Sniper.IsDestroyed) {
                SniperLines.RemoveAt(i);
                continue;
            }
            if (Now < l.FireAt)
                continue;
            SniperLines.RemoveAt(i);
            var dir = new Vector2(MathF.Cos(l.Angle), MathF.Sin(l.Angle));
            var s = new Shell(l.Sniper.Position + dir * 20f, dir * 7f, ShellID.TrailedRocket, l.Sniper, 0);
            s.Pierce = 1;
            s.BreaksCork = true;
            s.Tint = new Color(255, 60, 60);
            SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_regular_1.ogg", SoundContext.Effect, 0.5f, pitchOverride: 0.6f);
        }
    }

    private static PlayerTank? NearestPlayer(Vector2 from) =>
        GameHandler.AllPlayerTanks.Where(p => p is { IsDestroyed: false }).OrderBy(p => Vector2.Distance(p!.Position, from)).FirstOrDefault();

    private static bool Solid(Vector2 p, float r) =>
        Block.AllBlocks.Any(bl => bl is not null && bl.Properties.IsSolid && MathF.Abs(bl.Position.X - p.X) < Block.SIDE_LENGTH / 2 + r && MathF.Abs(bl.Position.Y - p.Y) < Block.SIDE_LENGTH / 2 + r);

    /// <summary>Rushes the nearest player; close up, a short fuse, then it blows up.</summary>
    private static void Kamikaze(Tank t, Info info) {
        if (info.FuseAt >= 0) {
            if ((int)((Now - info.FuseAt) * 10) % 2 == 0)
                GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, 14, 0), Color.Red, 0.5f);
            if (Authority && Now - info.FuseAt > 0.9 && !info.Handled) {
                info.Handled = true;
                Send(ACT_BOOM, _order.IndexOf(t));
            }
            return;
        }
        if (!Authority || NearestPlayer(t.Position) is not { } target)
            return;
        var to = target.Position - t.Position;
        float d = to.Length();
        if (d < 46f) {
            Send(ACT_FUSE, _order.IndexOf(t));
            return;
        }
        var dir = to / d;
        float speed = 1.7f * Dt * 0.62f;
        var nx = t.Position + new Vector2(dir.X * speed, 0);
        if (!Solid(nx, 12f)) t.Position = nx;
        var ny = t.Position + new Vector2(0, dir.Y * speed);
        if (!Solid(ny, 12f)) t.Position = ny;
        t.ChassisRotation = MathF.Atan2(dir.Y, dir.X) - MathHelper.PiOver2;
    }

    /// <summary>Gunners' shells: their item, and its colour.</summary>
    public static void OnShoot(Tank tank, Shell shell) {
        if (!TroveRun.Active || !_info.TryGetValue(tank, out var info))
            return;
        if (info.Variant == EnemyVariant.Brute) {
            shell.BreaksCork = true;
            shell.VisualScale = 1.4f;
            shell.HitRadius = 9f;
            return;
        }
        if (info.Variant != EnemyVariant.Gunner)
            return;
        shell.Tint = GunnerColors[(int)info.Gunner];
        if (info.Gunner == GunnerKind.Explosive)
            shell.ExplodeScale = 2.4f;
    }

    // ------------------------------------------------------------------ the moments everyone sees together

    private const int ACT_SPLIT = 1, ACT_SHIELD = 2, ACT_AIM = 3, ACT_FUSE = 4, ACT_BOOM = 5;

    private static void Send(int act, int a) {
        Do(act, a);
        if (Client.IsConnected())
            Client.SendTroveEvent(TroveRun.EV_ENEMY, act * 1_000_000 + a);
    }

    public static void OnEvent(int a) => Do(a / 1_000_000, a % 1_000_000);

    private static Tank? ByIndex(int i) => i >= 0 && i < _order.Count ? _order[i] : null;

    private static void Do(int act, int a) {
        switch (act) {
            case ACT_SPLIT: {
                // two little scouts burst out
                var at = TroveItems.DecodePos(a);
                _lastSplitDeath = Now;
                for (int n = 0; n < 2; n++) {
                    var off = new Vector2(n == 0 ? -16 : 16, 0);
                    var p = Solid(at + off, 8f) ? at : at + off;
                    var t = new TankTemplate { IsPlayer = false, AiTier = TankID.Brown, Position = p, Team = TeamID.Blue }.GetAiTank();
                    t.Team = TeamID.Blue;
                    Make(t, EnemyVariant.Scout, _order.Count);
                }
                GameHandler.Particles.MakeSmallExplosion(new Vector3(at.X, 5, at.Y), 8, 6, 1f, 8);
                break;
            }
            case ACT_SHIELD: {
                var at = TroveItems.DecodePos(a);
                var t = GameHandler.AllAITanks.Where(o => o is { IsDestroyed: false } && o.Team != TeamID.Red).OrderBy(o => Vector2.Distance(o!.Position, at)).FirstOrDefault();
                if (t is null)
                    break;
                t.Properties.Armor = new TankArmor(t, (t.Properties.Armor?.HitPoints ?? 0) + 1);
                GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, 12, 0), Color.LimeGreen, 1.2f);
                break;
            }
            case ACT_AIM:
                if (ByIndex(a / 1000) is { IsDestroyed: false } sn) {
                    SniperLines.Add((sn, a % 1000 / 360f * MathHelper.TwoPi, Now + 1.1));
                    SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.6f, pitchOverride: 0.9f);
                }
                break;
            case ACT_FUSE:
                if (ByIndex(a) is { } k && _info.TryGetValue(k, out var ki) && ki.FuseAt < 0) {
                    ki.FuseAt = Now;
                    SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.9f, pitchOverride: 1f);
                }
                break;
            case ACT_BOOM:
                if (ByIndex(a) is { IsDestroyed: false } b) {
                    if (_info.TryGetValue(b, out var bi))
                        bi.Handled = true;
                    new Explosion(b.Position, 3.2f, b, 0.4f);
                    b.Destroy(new TankHurtContextOther(null, TankHurtContextOther.HurtContext.FromIngame, "Kamikaze"), false);
                }
                break;
        }
    }
}
