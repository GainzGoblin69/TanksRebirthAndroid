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
    // batch 2
    Hive, SplitterKing, BigBertha, PyroBaron, ChronoTank, Juggernaut, Phantom, ShadowClones,
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
        new(BossKind.Hive, 1, "The Hive", TankID.Ash, "Keeps releasing swarms of tiny, fast scouts"),
        new(BossKind.SplitterKing, 1, "Splitter King", TankID.Pink, "Splits into two when destroyed, and those split again"),
        new(BossKind.BigBertha, 2, "Big Bertha", TankID.Yellow, "Leaps across the room and lands with a shockwave and a ring of shells"),
        new(BossKind.PyroBaron, 2, "Pyro Baron", TankID.Ruby, "Leaves burning trails and sweeps flame bursts across the room"),
        new(BossKind.ChronoTank, 3, "Chrono Tank", TankID.Sapphire, "Drops time bubbles that slow you and your shells"),
        new(BossKind.Juggernaut, 3, "The Juggernaut", TankID.Silver, "Charges through blocks; dazed when it hits a wall"),
        new(BossKind.Phantom, 4, "The Phantom", TankID.White, "Turns invisible; only its laser sight gives it away"),
        new(BossKind.ShadowClones, 5, "Shadow Master", TankID.Emerald, "Makes shadow copies of itself; only the real one can be hurt"),
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
    // Jelly Roller: shots in the current volley, and when its rest ends
    private static int _jellyShots;
    private static double _jellyRestUntil;
    // batch 2 bosses
    /// <summary>Pyro Baron's burning trail and Chrono Tank's time bubbles (every phone).</summary>
    public static readonly List<(Vector2 At, double Until)> Fires = [], Bubbles = [];
    public const float FIRE_RADIUS = 14f, BUBBLE_RADIUS = 66f;
    private static readonly Dictionary<Tank, double> _burned = [];
    private static readonly HashSet<Shell> _slowed = [];
    private static readonly Dictionary<AITank, int> _splits = [];
    private static readonly HashSet<AITank> _splitDone = [];
    private static double _lastSplitDeath = -10;
    private static Vector2 _leapFrom, _leapTo;
    private static double _leapStart = -10;
    private const double LEAP_TIME = 1.1;
    private static float _baseScale = 1f;
    private static bool _charging;
    private static Vector2 _chargeDir;
    private static double _windupUntil = -1, _stunUntil = -1;
    /// <summary>The Phantom is invisible right now (its laser sight shows where it aims).</summary>
    public static bool PhantomHidden { get; private set; }
    private static readonly Dictionary<AITank, double> _decoys = [];

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
        _magnetReadyAt = 0;
        _minions.Clear();
        _scheduledShots.Clear();
        Lines.Clear();
        Targets.Clear();
        _enraged = _halfDone = _burrowed = false;
        _dashStart = _dashEnd = -1;
        Fires.Clear();
        Bubbles.Clear();
        _burned.Clear();
        _slowed.Clear();
        _splits.Clear();
        _splitDone.Clear();
        _lastSplitDeath = -10;
        _leapStart = -10;
        _charging = false;
        _windupUntil = _stunUntil = -1;
        PhantomHidden = false;
        _decoys.Clear();
        _reinforced = false;
    }

    /// <summary>The Splitter King just split: keep the room from counting as cleared until its pieces appear.</summary>
    public static bool HoldsRoom => Current?.Kind == BossKind.SplitterKing && Now - _lastSplitDeath < 1.5;

    /// <summary>A boss's headlights: its own colour, flashing red while it winds up an attack (orange when enraged).</summary>
    public static Color HeadlightColor(Tank t) {
        bool attacking = Lines.Count > 0 || Targets.Count > 0 || _scheduledShots.Count > 0 || (_dashStart >= 0 && Now < _dashEnd) || MagnetCharged || _burrowed || BigMove;
        if (attacking) {
            float f = 0.5f + 0.5f * MathF.Sin((float)Now * 16f);
            return Color.Lerp(new Color(120, 10, 10), new Color(255, 40, 30), f);
        }
        if (_enraged)
            return new Color(255, 110, 40);
        var baseCol = Current?.Kind switch {
            BossKind.CaptainMarine => new Color(80, 220, 255),
            BossKind.VioletViper or BossKind.ShieldCommander or BossKind.MagnetMaster => new Color(200, 110, 255),
            BossKind.GreenGoliath => new Color(120, 255, 120),
            BossKind.BlackBaron or BossKind.MirrorTank => new Color(255, 60, 60),
            BossKind.ObsidianOverlord or BossKind.VaultKeeper => new Color(180, 80, 255),
            BossKind.GoldHoarder => new Color(255, 215, 80),
            BossKind.PepperPot => new Color(255, 240, 90),
            BossKind.JellyRoller or BossKind.DrillSergeant => new Color(255, 140, 200),
            BossKind.LaserLathe => new Color(255, 90, 60),
            BossKind.Hive => new Color(255, 200, 60),
            BossKind.SplitterKing => new Color(255, 150, 220),
            BossKind.BigBertha => new Color(255, 235, 120),
            BossKind.PyroBaron => new Color(255, 110, 30),
            BossKind.ChronoTank => new Color(110, 170, 255),
            BossKind.Juggernaut => new Color(220, 225, 235),
            BossKind.Phantom => new Color(235, 240, 255),
            BossKind.ShadowClones => new Color(90, 255, 150),
            _ => (Color?)null,
        };
        if (baseCol is { } bc)
            return bc;
        return t is AITank ai && AITank.TankDestructionColors.TryGetValue(ai.AiTankType, out var dc) ? Color.Lerp(dc, Color.White, 0.35f) : new Color(255, 200, 150);
    }

    /// <summary>Is this tank (one of) the boss(es) of this room?</summary>
    public static bool IsBoss(Tank t) => t is not null && (ReferenceEquals(t, Boss) || ReferenceEquals(t, Twin));

    /// <summary>Hits left across the boss (and its twin), for the bar at the top.</summary>
    public static int HitsLeft() {
        int h = 0;
        if (Current?.Kind == BossKind.SplitterKing) {
            // every piece still around, plus the pieces it will still split into
            foreach (var (t, gen) in _splits)
                if (!t.IsDestroyed)
                    h += (t.Properties.Armor?.HitPoints ?? 0) + 1 + (gen == 0 ? 10 : gen == 1 ? 2 : 0);
            return h;
        }
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
        Dress(boss, def.Kind);
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
                _jellyShots = 0;
                _jellyRestUntil = 0;
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
                Dress(t, def.Kind);
                break;
            }
            case BossKind.Hive:
                boss.Scaling = new Vector3(1.6f);
                props.MaxSpeed *= 0.6f;
                break;
            case BossKind.SplitterKing:
                boss.Scaling = new Vector3(1.6f);
                _splits[boss] = 0;
                break;
            case BossKind.BigBertha:
                boss.Scaling = new Vector3(2.0f);
                props.Stationary = true; // it gets around by leaping
                break;
            case BossKind.Juggernaut:
                boss.Scaling = new Vector3(1.55f);
                props.Stationary = true; // it moves the way its fight says
                props.ShellLimit = 2;
                break;
            case BossKind.Phantom:
                props.Invisible = false; // (white tanks start invisible: this one fades in and out)
                break;
            case BossKind.VaultKeeper:
                _trick = rng.Next(3);
                if (_trick == 0) {
                    props.ShellShootCount = 3;
                    props.ShellSpread = 0.22f;
                    props.ShellLimit *= 3;
                }
                break;
        }
        _baseScale = boss.Scaling.X;
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

    // each boss's paint job, made the first time it's met
    private static readonly Dictionary<BossKind, Microsoft.Xna.Framework.Graphics.Texture2D> _skins = [];

    /// <summary>Gives a boss its own paint job (stripes, scales, flames, a skull on the turret...).</summary>
    private static void Dress(AITank boss, BossKind kind) {
        try {
            if (TankDesigns.ForBoss(kind) is not { } design || !TankSkinMesh.Enemy.Usable(boss.Model))
                return;
            if (!_skins.TryGetValue(kind, out var tex) || tex.IsDisposed)
                _skins[kind] = tex = TankPaint.Make(TankGame.Instance.GraphicsDevice, false, design);
            boss.SwapTankTexture(tex);
        }
        catch (Exception e) {
            TankGame.ClientLog?.Write($"Boss paint failed (it keeps its tank's colors): {e.Message}", Internals.LogType.Warn);
        }
    }

    public static void Update() {
        // paint that moves (glowing cracks, circuits, flames)
        if (Boss is { IsDestroyed: false } dressed && dressed.TankTexture is { } skin && TankPaint.IsPainted(skin))
            TankPaint.Tick(skin);
        if (!TroveRun.Active || !CampaignGlobals.InMission || Boss is null)
            return;
        EveryPhone();
        // the boss is gone: its helpers go with it (the host clears them out so the room can finish)
        bool bossDown = Boss.IsDestroyed && Twin is not { IsDestroyed: false };
        if (Current?.Kind == BossKind.SplitterKing)
            bossDown = !_splits.Any(k => !k.Key.IsDestroyed) && Now - _lastSplitDeath > 1.0;
        if (bossDown) {
            if (Authority)
                foreach (var m in _minions)
                    if (!m.IsDestroyed)
                        m.Destroy(new TankHurtContextOther(null, TankHurtContextOther.HurtContext.FromIngame, "Boss defeated"), true);
            _minions.Clear();
            Lines.Clear();
            Targets.Clear();
            TroveDrops.Clear();
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
        else if (Current is { } def && !Boss.IsDestroyed) {
            Brain(def.Kind);
            // bosses with helpers call in more by parachute when they're hurt
            if (!_reinforced && Fraction <= 0.5f && def.Kind is BossKind.MamaBrown or BossKind.Hive or BossKind.GreenGoliath or BossKind.ObsidianOverlord or BossKind.ShadowClones) {
                _reinforced = true;
                var tiers = ReinforceTiers[Math.Clamp(_floor - 1, 0, ReinforceTiers.Length - 1)];
                var target = NearestPlayer(Boss.Position);
                for (int i = 0; i < 2; i++)
                    SendAct(ACT_REINFORCE, RandomFreeSpot(target?.Position ?? Vector2.Zero, 100f), tiers[i % tiers.Length]);
            }
        }
    }

    /// <summary>What every phone does the same way for the newer bosses: splits, leaps, fire, time bubbles, the charge, decoys.</summary>
    private static void EveryPhone() {
        // Splitter King: a piece that's down splits in two (the host says so)
        if (Current?.Kind == BossKind.SplitterKing)
            foreach (var (t, gen) in _splits.ToArray())
                if (t.IsDestroyed && _splitDone.Add(t)) {
                    _lastSplitDeath = Now;
                    if (Authority && gen < 2)
                        SendAct(ACT_SPLITK, t.Position, gen + 1);
                }
        // Big Bertha in the air: up and over to where it lands (bigger while high up, can't be hurt)
        if (Current?.Kind == BossKind.BigBertha && Boss is { IsDestroyed: false } bb) {
            double lt = (Now - _leapStart) / LEAP_TIME;
            if (lt >= 0 && lt < 1) {
                bb.Position = Vector2.Lerp(_leapFrom, _leapTo, (float)lt);
                bb.Scaling = new Vector3(_baseScale * (1f + 0.55f * MathF.Sin(MathF.PI * (float)lt)));
                bb.Properties.Immortal = true;
            }
            else if (lt >= 1 && bb.Properties.Immortal) {
                bb.Position = _leapTo;
                bb.Scaling = new Vector3(_baseScale);
                bb.Properties.Immortal = false;
                SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_destroy.ogg", SoundContext.Effect, 0.5f, pitchOverride: -0.8f);
                if (Authority)
                    Ring(bb, Fraction <= 0.5f ? 12 : 8, (float)Now, 5);
            }
        }
        // Pyro Baron's fire burns your tank (each phone burns its own players)
        Fires.RemoveAll(f => Now > f.Until);
        if (Fires.Count > 0) {
            if (Random.Shared.NextDouble() < 0.5 * Dt) {
                var f = Fires[Random.Shared.Next(Fires.Count)];
                GameHandler.Particles.MakeShineSpot(new Vector3(f.At.X, 6, f.At.Y), new Color(255, 120, 30), 0.5f);
            }
            foreach (var pl in GameHandler.AllPlayerTanks) {
                if (pl is not { IsDestroyed: false } || !TroveItems.IsLocal(pl.PlayerId) || pl.Properties.Immortal)
                    continue;
                if (!Fires.Any(f => Vector2.Distance(f.At, pl.Position) < FIRE_RADIUS + 8f))
                    continue;
                if (_burned.TryGetValue(pl, out var last) && Now - last < 1.5)
                    continue;
                _burned[pl] = Now;
                pl.Damage(new TankHurtContextOther(Boss, TankHurtContextOther.HurtContext.FromIngame, "Burned"), true);
            }
        }
        // Chrono Tank's time bubbles slow shells inside them (tanks are slowed by the velocity filter)
        Bubbles.RemoveAll(bu => Now > bu.Until);
        if (Bubbles.Count > 0 || _slowed.Count > 0) {
            foreach (var sh in Shell.AllShells) {
                if (sh is null)
                    continue;
                bool inside = Bubbles.Any(bu => Vector2.Distance(bu.At, sh.Position) < BUBBLE_RADIUS);
                if (inside && _slowed.Add(sh))
                    sh.Velocity *= 0.4f;
                else if (!inside && _slowed.Remove(sh))
                    sh.Velocity /= 0.4f;
            }
            _slowed.RemoveWhere(sh => Array.IndexOf(Shell.AllShells, sh) < 0); // (gone)
        }
        // the Juggernaut smashes breakable blocks while it charges
        if (Current?.Kind == BossKind.Juggernaut && _charging && Boss is { IsDestroyed: false } jg)
            foreach (var bl in Block.AllBlocks)
                if (bl is not null && bl.Properties.IsDestructible && Vector2.Distance(bl.Position, jg.Position) < 28f * jg.Scaling.X / 1.55f)
                    bl.Destroy();
        if (Now < _stunUntil && Boss is { IsDestroyed: false } st && Random.Shared.NextDouble() < 0.4 * Dt)
            GameHandler.Particles.MakeShineSpot(st.Position3D + new Vector3(0, 24, 0), Color.Yellow, 0.5f);
        // shadow decoys fade away after a while
        foreach (var (d, until) in _decoys.ToArray())
            if (d.IsDestroyed)
                _decoys.Remove(d);
            else if (Now > until) {
                _decoys.Remove(d);
                d.Properties.Immortal = false;
                GameHandler.Particles.MakeSmallExplosion(d.Position3D, 6, 6, 0.8f, 6);
                d.Remove(true);
            }
    }

    /// <summary>Chrono Tank: players in a time bubble move slowly.</summary>
    public static Vector2 SlowFilter(Tank t, Vector2 v) =>
        Bubbles.Count > 0 && t is PlayerTank && Bubbles.Any(bu => Vector2.Distance(bu.At, t.Position) < BUBBLE_RADIUS) ? v * 0.45f : v;

    /// <summary>The Phantom's laser sight: from the hidden Phantom toward the player it aims at.</summary>
    public static (Vector2 From, Vector2 To)? PhantomSight =>
        Current?.Kind == BossKind.Phantom && PhantomHidden && Boss is { IsDestroyed: false } ph && NearestPlayer(ph.Position) is { } pt ? (ph.Position, pt.Position) : null;

    /// <summary>The boss is winding up or in the middle of a big move (headlights flash red).</summary>
    private static bool BigMove => _charging || Now < _windupUntil || Now - _leapStart < LEAP_TIME + 0.2;

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
                // after a volley of bounce shots it stops to rest (wobbling), then rolls on
                if (Now < _jellyRestUntil) {
                    float wob = MathF.Sin((float)Now * 14f) * 0.12f;
                    b.ChassisRotation += wob * Dt * 0.3f;
                    if (Random.Shared.NextDouble() < 0.15 * Dt)
                        GameHandler.Particles.MakeShineSpot(b.Position3D + new Vector3(0, 18, 0), new Color(255, 160, 210), 0.5f);
                    break;
                }
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
                if (bounced && Every("bounceShot", 0.3)) {
                    Fire(b, aim, 7);
                    if (++_jellyShots >= (hurt ? 8 : 6)) {
                        _jellyShots = 0;
                        _jellyRestUntil = Now + (hurt ? 2.2 : 3.0);
                    }
                }
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
                        for (int i = 0; i < 2 && _minions.Count + TroveDrops.Pending.Count + i < 3; i++)
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
            case BossKind.Hive:
                if (Every("swarm", hurt ? 3.5 : 5) && _minions.Count(m => !m.IsDestroyed) < 6) {
                    SendAct(ACT_SPAWN_SCOUT, FreeSpotNear(b.Position, 45f));
                    SendAct(ACT_SPAWN_SCOUT, FreeSpotNear(b.Position + new Vector2(0, 20), 50f));
                    if (hurt)
                        SendAct(ACT_SPAWN_SCOUT, FreeSpotNear(b.Position - new Vector2(0, 20), 55f));
                }
                if (Every("hivespread", 4))
                    Spread(b, aim, 3, 0.25f, 5);
                break;
            case BossKind.SplitterKing:
                if (Every("kspread", hurt ? 3 : 4))
                    Spread(b, aim, 3, 0.2f, 6);
                break;
            case BossKind.BigBertha:
                if (Now - _leapStart > LEAP_TIME && Every("leap", hurt ? 3.2 : 4.2)) {
                    var land = FreeSpotNear(target.Position + target.Velocity * 20f, 0f);
                    if (Solid(land, 18f))
                        land = FreeSpotNear(target.Position, 50f);
                    SendAct(ACT_LEAP, land);
                }
                break;
            case BossKind.PyroBaron:
                if (b.Velocity.LengthSquared() > 0.15f && Every("fire", 0.35) && Fires.Count < 45)
                    SendAct(ACT_FIRE, b.Position, hurt ? 2 : 1);
                if (Every("sweep", hurt ? 4 : 6))
                    for (int i = 0; i < 12; i++)
                        _scheduledShots.Add((Now + i * 0.08, aim - 0.6f + i * 0.11f, SHOT_FLAME));
                break;
            case BossKind.ChronoTank:
                if (Every("bubble", hurt ? 3.5 : 5.5)) {
                    SendAct(ACT_BUBBLE, target.Position);
                    if (hurt)
                        SendAct(ACT_BUBBLE, b.Position + (target.Position - b.Position) * 0.5f);
                }
                break;
            case BossKind.Juggernaut: {
                if (_charging) {
                    float speed = (hurt ? 5.2f : 4.4f) * Dt * 0.62f;
                    var next = b.Position + _chargeDir * speed;
                    bool edge = next.X < GameScene.TANKS_MIN_X + 14 || next.X > GameScene.TANKS_MAX_X - 14 || next.Y < GameScene.TANKS_MIN_Y + 14 || next.Y > GameScene.TANKS_MAX_Y - 14;
                    if (edge || SolidHard(next, 14f)) {
                        SendAct(ACT_STUN, b.Position);
                        break;
                    }
                    b.Position = next;
                    b.ChassisRotation = MathF.Atan2(_chargeDir.Y, _chargeDir.X) - MathHelper.PiOver2;
                    foreach (var pl in GameHandler.AllPlayerTanks)
                        if (pl is { IsDestroyed: false } && Vector2.Distance(pl.Position, b.Position) < 30f * b.Scaling.X / 1.55f && Every("jram" + pl.PlayerId, 1.0))
                            pl.Damage(new TankHurtContextOther(b, TankHurtContextOther.HurtContext.FromIngame, "Run over by the Juggernaut"), true);
                    break;
                }
                if (Now < _stunUntil)
                    break; // dazed
                if (_windupUntil > 0 && Now >= _windupUntil) {
                    _windupUntil = -1;
                    SendAct(ACT_CHARGE, b.Position, AngleCode(aim) + 1);
                    break;
                }
                if (_windupUntil < 0 && Every("charge", hurt ? 3.5 : 5)) {
                    _windupUntil = Now + 0.9;
                    SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.9f, pitchOverride: -0.8f);
                    break;
                }
                // slowly turns and rolls toward you between charges
                var dir = Vector2.Normalize(target.Position - b.Position);
                if (Vector2.Distance(b.Position, target.Position) > 60f) {
                    var step = b.Position + dir * 0.5f * Dt * 0.62f;
                    if (!Solid(step, 16f))
                        b.Position = step;
                }
                b.ChassisRotation = MathF.Atan2(dir.Y, dir.X) - MathHelper.PiOver2;
                if (Every("jshot", 2.5))
                    Fire(b, aim, 6);
                break;
            }
            case BossKind.Phantom:
                if (Every("phase", PhantomHidden ? (hurt ? 2.5 : 2.2) : (hurt ? 3 : 4)))
                    SendAct(ACT_PHANTOM, b.Position, PhantomHidden ? 2 : 1);
                if (!PhantomHidden && Every("pspread", hurt ? 0.9 : 1.3))
                    Spread(b, aim, 3, 0.18f, 7);
                break;
            case BossKind.ShadowClones:
                if (Every("clones", hurt ? 6 : 8) && _decoys.Count == 0) {
                    SendAct(ACT_CLONES, b.Position, hurt ? 4 : 3);
                    // the real one ends up at one of the spots, so you lose track of it
                    var spots = CloneSpots(b.Position, hurt ? 4 : 3);
                    var pick = spots[Random.Shared.Next(spots.Count)];
                    if (Random.Shared.Next(spots.Count + 1) > 0)
                        b.Position = pick;
                }
                if (Every("sspread", hurt ? 2 : 3))
                    Spread(b, aim, 2, 0.2f, 6);
                break;
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
    private static double _magnetReadyAt;
    /// <summary>Magnet Master can catch a shell right now (the HUD shows a glow).</summary>
    public static bool MagnetCharged => Current?.Kind == BossKind.MagnetMaster && Now >= _magnetReadyAt;
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
            // it catches one shell, then has to recharge: shells that arrive in between hit it
            // (it used to catch every shell that got close, which made it impossible to damage)
            if (d < 34f && Now >= _magnetReadyAt) {
                _magnetReadyAt = Now + (Fraction <= 0.5f ? 1.2 : 2.0);
                s.Destroy(Shell.DestructionContext.WithShell);
                if (Authority && NearestPlayer(b.Position) is { } p)
                    Fire(b, AngleTo(b.Position, p.Position), 8);
                continue;
            }
            if (d < 40f)
                continue; // close in: let it fly straight into the hull
            float speed = s.Velocity.Length();
            var want = Vector2.Normalize(b.Position - s.Position) * speed;
            s.Velocity = Vector2.Normalize(Vector2.Lerp(s.Velocity, want, 0.045f * Dt)) * speed;
        }
    }

    // ------------------------------------------------------------------ attacks (made on the host, sent to the others)

    // shot codes: 0..11 = speed 1 + code*0.5; special codes below
    private const int SHOT_HOMING = 12, SHOT_LASER = 13, SHOT_GOLD = 14, SHOT_FLAME = 15;

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
        float speed = code switch { SHOT_HOMING => 1.6f, SHOT_LASER => 8f, SHOT_GOLD => 3.2f, SHOT_FLAME => 2.4f, _ => 1f + code * 0.5f };
        int type = code == SHOT_LASER ? ShellID.TrailedRocket : b.Properties.ShellType;
        uint ric = code is SHOT_LASER or SHOT_FLAME ? 0u : Math.Max(1u, b.Properties.RicochetCount);
        var homing = code == SHOT_HOMING ? new Shell.HomingProperties { Radius = 260f, Speed = speed, Power = 0.06f * speed } : default;
        var s = new Shell(from, dir * speed, type, b, ric, homing);
        if (code == SHOT_LASER) {
            s.Pierce = 3;
            s.BreaksCork = true;
            s.VisualScale = 1.3f;
        }
        if (code == SHOT_GOLD)
            s.SplitsOnBounce = true;
        if (code == SHOT_FLAME) {
            s.Tint = new Color(255, 120, 30);
            s.VisualScale = 0.9f;
        }
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
        ACT_LASER_LINE = 6, ACT_ARTILLERY = 7, ACT_GOLD_DROP = 8, ACT_SPARKLE = 9,
        ACT_SPAWN_SCOUT = 10, ACT_SPLITK = 11, ACT_LEAP = 12, ACT_FIRE = 13, ACT_BUBBLE = 14, ACT_CHARGE = 15, ACT_STUN = 16,
        ACT_PHANTOM = 17, ACT_CLONES = 18, ACT_REINFORCE = 19;

    /// <summary>Tanks that parachute in to help a boss, by floor.</summary>
    private static readonly int[][] ReinforceTiers = [
        [TankID.Brown, TankID.Ash], [TankID.Marine, TankID.Yellow], [TankID.Pink, TankID.Violet], [TankID.Green, TankID.White], [TankID.Black, TankID.Violet],
    ];
    private static bool _reinforced;

    private static List<Vector2> CloneSpots(Vector2 at, int n) {
        var list = new List<Vector2>();
        for (int i = 0; i < n; i++)
            list.Add(FreeSpotNear(at + Vector2.UnitX.Rotate(i * MathHelper.TwoPi / n + 0.4f) * 80f, 20f));
        return list;
    }

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
            case ACT_SPAWN_BROWN: {
                // Mama Brown's little ones come straight out of her
                var t = new TankTemplate { IsPlayer = false, AiTier = TankID.Brown, Position = at, Team = TeamID.Blue }.GetAiTank();
                t.Team = TeamID.Blue;
                t.Scaling = new Vector3(0.7f);
                _minions.Add(t);
                GameHandler.Particles.MakeSmallExplosion(new Vector3(at.X, 5, at.Y), 6, 6, 1f, 8);
                break;
            }
            case ACT_SPAWN_TURRET:
            case ACT_SPAWN_BLACK:
            case ACT_REINFORCE: {
                // helpers parachute in: you see where they'll land first
                int tier = act == ACT_SPAWN_TURRET ? TankID.Green : act == ACT_SPAWN_BLACK ? TankID.Black : Math.Max(TankID.Brown, extra);
                bool turret = act == ACT_SPAWN_TURRET;
                TroveDrops.Add(at, tier, t => {
                    if (turret)
                        t.Properties.Armor = new TankArmor(t, 2);
                    if (Boss is { IsDestroyed: false })
                        _minions.Add(t);
                    else
                        t.Remove(true); // the fight is already over
                });
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
            case ACT_SPAWN_SCOUT: {
                var t = new TankTemplate { IsPlayer = false, AiTier = TankID.Brown, Position = at, Team = TeamID.Blue }.GetAiTank();
                t.Team = TeamID.Blue;
                TroveEnemies.MakeScout(t);
                _minions.Add(t);
                GameHandler.Particles.MakeShineSpot(new Vector3(at.X, 8, at.Y), Color.Gold, 0.8f);
                break;
            }
            case ACT_SPLITK: {
                _lastSplitDeath = Now;
                int gen = Math.Clamp(extra, 1, 2);
                int tier = Current?.Tier ?? TankID.Pink;
                for (int k = 0; k < 2; k++) {
                    var p = FreeSpotNear(at + new Vector2(k == 0 ? -22 : 22, 0), 0f);
                    if (Solid(p, 10f)) p = at;
                    var t = new TankTemplate { IsPlayer = false, AiTier = tier, Position = p, Team = TeamID.Blue }.GetAiTank();
                    t.Team = TeamID.Blue;
                    t.Scaling = new Vector3(gen == 1 ? 1.15f : 0.75f);
                    if (gen == 1)
                        t.Properties.Armor = new TankArmor(t, 2);
                    else
                        t.Properties.MaxSpeed *= 1.4f;
                    _splits[t] = gen;
                }
                GameHandler.Particles.MakeSmallExplosion(new Vector3(at.X, 6, at.Y), 12, 8, 1.2f, 10);
                break;
            }
            case ACT_LEAP:
                if (Boss is { } lb) {
                    _leapFrom = lb.Position;
                    _leapTo = at;
                    _leapStart = Now;
                    Targets.Add((at, Now + LEAP_TIME)); // the red circle where it lands; the shockwave goes off there
                    SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", SoundContext.Effect, 0.7f, pitchOverride: -0.6f);
                }
                break;
            case ACT_FIRE:
                Fires.Add((at, Now + (extra >= 2 ? 7 : 5)));
                break;
            case ACT_BUBBLE:
                Bubbles.Add((at, Now + 7));
                SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.8f, pitchOverride: -0.9f);
                break;
            case ACT_CHARGE: {
                float ang = (extra - 1) / 360f * MathHelper.TwoPi;
                _chargeDir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                _charging = true;
                SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_destroy.ogg", SoundContext.Effect, 0.3f, pitchOverride: -0.9f);
                break;
            }
            case ACT_STUN:
                _charging = false;
                _stunUntil = Now + 2.0;
                if (Boss is { } sb) {
                    GameHandler.Particles.MakeSmallExplosion(sb.Position3D, 10, 8, 1.2f, 10);
                    SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_destroy.ogg", SoundContext.Effect, 0.5f, pitchOverride: -0.5f);
                }
                break;
            case ACT_PHANTOM:
                PhantomHidden = extra == 1;
                if (Boss is { } pb) {
                    pb.Properties.Invisible = PhantomHidden;
                    pb.Properties.ShellLimit = PhantomHidden ? 0 : 3;
                    GameHandler.Particles.MakeShineSpot(pb.Position3D + new Vector3(0, 10, 0), Color.White, 1.0f);
                }
                break;
            case ACT_CLONES:
                if (Boss is { } cb) {
                    foreach (var p in CloneSpots(at, Math.Max(3, extra))) {
                        var t = new TankTemplate { IsPlayer = false, AiTier = cb.AiTankType, Position = p, Team = TeamID.Blue }.GetAiTank();
                        t.Team = TeamID.Blue;
                        t.Scaling = cb.Scaling;
                        t.Properties.Immortal = true; // only the real one can be hurt
                        _decoys[t] = Now + 6;
                        _minions.Add(t);
                        GameHandler.Particles.MakeSmallExplosion(new Vector3(p.X, 5, p.Y), 6, 6, 0.8f, 6);
                    }
                    GameHandler.Particles.MakeSmallExplosion(cb.Position3D, 6, 6, 0.8f, 6);
                }
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
                new Explosion(t.At, 5f, Boss, 0.4f) { SparesOwner = true };
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
