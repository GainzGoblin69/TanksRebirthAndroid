using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

public enum EnemyVariant : byte { None, Scout, Brute, Splitter, Kamikaze, Gunner, Ghost, Medic, Sniper, Turret, Bunker, Nest, Crawler }
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
    public static readonly string[] Names = ["", "Scout", "Brute", "Splitter", "Kamikaze", "Gunner", "Ghost", "Medic", "Sniper", "Wall Turret", "Mortar Bunker", "Crawler Nest", "Mine Crawler"];
    private static readonly Color[] GunnerColors = [new(255, 220, 60), new(80, 220, 255), new(255, 120, 30), new(110, 255, 120)];

    private sealed class Info {
        public EnemyVariant Variant;
        public GunnerKind Gunner;
        public bool Handled; // (splits and blasts happen once)
        public double FuseAt = -1;
        public double NextAct;
        public Tank? Parent; // a crawler's nest
    }

    /// <summary>Mortar shells in the air: where from, where they land, when they were fired and when they land.</summary>
    public static readonly List<(Vector2 From, Vector2 To, double FiredAt, double LandAt, Tank Bunker)> Mortars = [];
    public const double MORTAR_FLIGHT = 1.7;
    // textures painted for this room's special enemies (thrown away with the room)
    private static readonly List<Texture2D> _painted = [];

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

        // now and then (not often) a room also has a non-tank enemy: wall turrets, a mortar bunker or a crawler nest
        if (room.BossIndex >= 0 || rng.NextDouble() >= 0.08 + 0.015 * f.Number)
            return;
        var kinds = new List<EnemyVariant> { EnemyVariant.Turret, EnemyVariant.Turret };
        if (f.Number >= 2) kinds.Add(EnemyVariant.Bunker);
        if (f.Number >= 2) kinds.Add(EnemyVariant.Nest);
        var kind = kinds[rng.Next(kinds.Count)];
        bool Free(int x, int y) => x >= 2 && y >= 2 && x <= TroveGen.GW - 3 && y <= TroveGen.GH - 3 && !blocked.Contains(new Point(x, y))
            && !room.Enemies.Any(o => Math.Abs(o.X - x) + Math.Abs(o.Y - y) < 2);
        bool NextToWall(int x, int y) => blocked.Contains(new Point(x + 1, y)) || blocked.Contains(new Point(x - 1, y))
            || blocked.Contains(new Point(x, y + 1)) || blocked.Contains(new Point(x, y - 1));
        var cells = new List<Point>();
        for (int x = 2; x <= TroveGen.GW - 3; x++)
            for (int y = 2; y <= TroveGen.GH - 3; y++)
                if (Free(x, y) && (kind != EnemyVariant.Turret || NextToWall(x, y)))
                    cells.Add(new Point(x, y));
        // away from the doors (the middle of each side), so nothing is waiting right where you walk in
        cells.RemoveAll(c => Math.Abs(c.X - TroveGen.GW / 2) <= 2 && (c.Y <= 3 || c.Y >= TroveGen.GH - 4)
                          || Math.Abs(c.Y - TroveGen.GH / 2) <= 2 && (c.X <= 3 || c.X >= TroveGen.GW - 4));
        int count = kind == EnemyVariant.Turret ? 2 : 1;
        for (int k = 0; k < count && cells.Count > 0; k++) {
            var c = cells[rng.Next(cells.Count)];
            cells.RemoveAll(o => Math.Abs(o.X - c.X) + Math.Abs(o.Y - c.Y) < 5);
            room.Enemies.Add((c.X, c.Y, kind == EnemyVariant.Turret ? TankID.Marine : TankID.Brown, 0f));
            room.Variants[room.Enemies.Count - 1] = kind;
            blocked.Add(c);
        }
    }

    // ------------------------------------------------------------------ a room's tanks just appeared

    public static void Clear() {
        Mortars.Clear();
        foreach (var tex in _painted)
            if (!GameHandler.AllAITanks.Any(t => t is { IsDestroyed: false } && t.TankTexture == tex))
                tex.Dispose();
        _painted.RemoveAll(tx => tx.IsDisposed);
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
            case EnemyVariant.Turret:
                // a squat steel gun on a wall: it turns and fires, but never moves
                t.Scaling = new Vector3(1.15f, 0.7f, 1.15f);
                p.Stationary = true;
                p.MineLimit = 0;
                p.ShellLimit = 2;
                p.ShellCooldown = 70;
                p.Armor = new TankArmor(t, 1);
                Paint(t, new Color(150, 156, 165));
                break;
            case EnemyVariant.Bunker:
                // a low concrete bunker that lobs shells over walls
                t.Scaling = new Vector3(1.35f, 0.55f, 1.35f);
                p.Stationary = true;
                p.HasTurret = false;
                p.ShellLimit = 0;
                p.MineLimit = 0;
                p.Armor = new TankArmor(t, 2);
                info.NextAct = Now + 2.5 + salt % 2;
                Paint(t, new Color(128, 126, 112));
                break;
            case EnemyVariant.Nest:
                // a crawler nest: more crawlers keep coming out until it's destroyed
                t.Scaling = new Vector3(1.25f, 0.5f, 1.25f);
                p.Stationary = true;
                p.HasTurret = false;
                p.ShellLimit = 0;
                p.MineLimit = 0;
                p.Armor = new TankArmor(t, 2);
                info.NextAct = Now + 1.5;
                Paint(t, new Color(120, 52, 78));
                break;
            case EnemyVariant.Crawler:
                // a tiny bug that scurries at you and blows up
                t.Scaling = new Vector3(0.5f);
                p.Stationary = true; // it moves the way this file says
                p.HasTurret = false;
                p.ShellLimit = 0;
                p.MineLimit = 0;
                p.Armor = null;
                Paint(t, new Color(240, 200, 40));
                break;
        }
    }

    /// <summary>Repaints a special enemy (its own copy of the texture, in the given color).</summary>
    private static void Paint(AITank t, Color c) {
        if (t.TankTexture is not { IsDisposed: false } tex || tex.Width * tex.Height > 64 * 64)
            return;
        var data = new Color[tex.Width * tex.Height];
        tex.GetData(data);
        float avg = 0;
        foreach (var d in data)
            avg += (d.R * 0.299f + d.G * 0.587f + d.B * 0.114f) / 255f;
        avg = Math.Max(0.05f, avg / data.Length);
        for (int i = 0; i < data.Length; i++) {
            float l = (data[i].R * 0.299f + data[i].G * 0.587f + data[i].B * 0.114f) / 255f / avg;
            float f = MathHelper.Clamp(0.3f + 0.7f * l, 0f, 1.5f);
            data[i] = new Color(Math.Clamp(c.R * f / 255f, 0f, 1f), Math.Clamp(c.G * f / 255f, 0f, 1f), Math.Clamp(c.B * f / 255f, 0f, 1f));
        }
        var own = new Texture2D(tex.GraphicsDevice, tex.Width, tex.Height);
        own.SetData(data);
        _painted.Add(own);
        t.SwapTankTexture(own);
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
                case EnemyVariant.Crawler:
                    if (!frozen)
                        Kamikaze(t, info);
                    break;
                case EnemyVariant.Bunker:
                    if (!frozen && Authority && Now >= info.NextAct && NearestPlayer(t.Position) is { } mt
                        && Vector2.Distance(mt.Position, t.Position) < 520f) {
                        info.NextAct = Now + 4.2;
                        // aimed a little ahead of where you're driving
                        var lead = new Vector2(mt.Velocity.X, mt.Velocity.Y) * 22f;
                        var to = mt.Position + (lead.Length() > 70f ? Vector2.Normalize(lead) * 70f : lead);
                        Send(ACT_MORTAR, TroveItems.EncodePos(to));
                    }
                    break;
                case EnemyVariant.Nest:
                    if (Random.Shared.NextDouble() < 0.12 * Dt)
                        GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(Random.Shared.NextSingle() * 16 - 8, 6, Random.Shared.NextSingle() * 16 - 8),
                            new Color(200, 90, 140), 0.45f);
                    if (!frozen && Authority && Now >= info.NextAct) {
                        info.NextAct = Now + 5;
                        int alive = _info.Count(k => k.Value.Parent == t && !k.Key.IsDestroyed);
                        if (alive < 3)
                            Send(ACT_HATCH, _order.IndexOf(t));
                    }
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
        // mortar shells land (every phone makes the blast)
        for (int i = Mortars.Count - 1; i >= 0; i--) {
            var m = Mortars[i];
            if (Now < m.LandAt)
                continue;
            Mortars.RemoveAt(i);
            new Explosion(m.To, 2.6f, m.Bunker.IsDestroyed ? null : m.Bunker, 0.4f);
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
        bool crawler = info.Variant == EnemyVariant.Crawler;
        if (info.FuseAt >= 0) {
            if ((int)((Now - info.FuseAt) * 10) % 2 == 0)
                GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, crawler ? 8 : 14, 0), Color.Red, crawler ? 0.35f : 0.5f);
            if (Authority && Now - info.FuseAt > (crawler ? 0.45 : 0.9) && !info.Handled) {
                info.Handled = true;
                Send(ACT_BOOM, _order.IndexOf(t));
            }
            return;
        }
        if (!Authority || NearestPlayer(t.Position) is not { } target)
            return;
        var to = target.Position - t.Position;
        float d = to.Length();
        if (d < (crawler ? 30f : 46f)) {
            Send(ACT_FUSE, _order.IndexOf(t));
            return;
        }
        var dir = to / d;
        // crawlers scuttle (fast, with a wobble)
        if (crawler)
            dir = Vector2.Normalize(dir + new Vector2(-dir.Y, dir.X) * MathF.Sin((float)Now * 9f + _order.IndexOf(t)) * 0.35f);
        float speed = (crawler ? 2.3f : 1.7f) * Dt * 0.62f;
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

    private const int ACT_SPLIT = 1, ACT_SHIELD = 2, ACT_AIM = 3, ACT_FUSE = 4, ACT_BOOM = 5, ACT_MORTAR = 6, ACT_HATCH = 7;

    private static void Send(int act, int a) {
        Do(act, a);
        if (Client.IsConnected())
            Client.SendTroveEvent(TroveRun.EV_ENEMY, act * 1_000_000 + a);
    }

    public static void OnEvent(int a) => Do(a / 1_000_000, a % 1_000_000);

    private static Tank? ByIndex(int i) => i >= 0 && i < _order.Count ? _order[i] : null;

    private static void Do(int act, int a) {
        switch (act) {
            case ACT_MORTAR: {
                // the bunker nearest the target fires (the same one on every phone)
                var to = TroveItems.DecodePos(a);
                var bunker = _info.Where(k => k.Value.Variant == EnemyVariant.Bunker && !k.Key.IsDestroyed)
                    .Select(k => k.Key).OrderBy(b => Vector2.Distance(b.Position, to)).FirstOrDefault();
                if (bunker is null)
                    break;
                Mortars.Add((bunker.Position, to, Now, Now + MORTAR_FLIGHT, bunker));
                SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_rocket.ogg", SoundContext.Effect, 0.5f, pitchOverride: -0.7f);
                GameHandler.Particles.MakeSmallExplosion(bunker.Position3D + new Vector3(0, 8, 0), 4, 3, 0.6f, 4);
                break;
            }
            case ACT_HATCH: {
                if (ByIndex(a) is not { IsDestroyed: false } nest)
                    break;
                int n = _info.Count(k => k.Value.Parent == nest);
                var off = new Vector2(MathF.Cos(n * 2.4f), MathF.Sin(n * 2.4f)) * 22f;
                var p = Solid(nest.Position + off, 8f) ? nest.Position : nest.Position + off;
                var t = new TankTemplate { IsPlayer = false, AiTier = TankID.Brown, Position = p, Team = TeamID.Blue }.GetAiTank();
                t.Team = TeamID.Blue;
                Make(t, EnemyVariant.Crawler, _order.Count);
                _info[t].Parent = nest;
                GameHandler.Particles.MakeShineSpot(nest.Position3D + new Vector3(0, 8, 0), new Color(255, 210, 60), 0.9f);
                SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.5f, pitchOverride: -0.8f);
                break;
            }
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
                    new Explosion(b.Position, bi?.Variant == EnemyVariant.Crawler ? 2.2f : 3.2f, b, 0.4f);
                    b.Destroy(new TankHurtContextOther(null, TankHurtContextOther.HurtContext.FromIngame, "Kamikaze"), false);
                }
                break;
        }
    }
}
