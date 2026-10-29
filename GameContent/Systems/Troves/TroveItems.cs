using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using tainicom.Aether.Physics2D.Dynamics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.Framework.Input;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.Troves;

public enum TroveItemId {
    Rocket, Homing, SliderBomb, ExtraLife, Shield, SpeedTreads, BouncyShells, ExtraShell, MinePack, Detonator, BlockBust,
    BigMines, TripleShot, QuickReload, OrbitGuard, TreasureMap, BuddyTank, HeavyShells, ExplosiveShells, GhostTreads, ShellShield, BossHunter,
    Stopwatch, ShellBurst, Nuke, Decoy, Invincibility, Reroll,
    SplitShot, SniperShells, ChainLightning, RegenPlating, ClusterMines, HomingMines, TinyTank, VampireTreads, RicochetMaster, GiantTank, EchoShot,
    Airstrike, WallBuilder, Dash, MineStorm, Convert, ShrinkRay, RepairKit, LightningStorm,
    GhostShells, LastStand, MagnetShells, Afterburner, RicochetShield, SecondWind,
    Clone, EMP, Teleport, TurretDrop, SupplyDrop, MirrorShield,
}

/// <summary>How an item works: used with the ITEM button, takes effect when picked up, or lasts the whole run.</summary>
public enum TroveItemKind { Active, Instant, Passive }

/// <param name="Recharge">Active items: rooms to clear before it can be used again (0 = used up).</param>
/// <param name="RechargeSeconds">Active items that recharge over time instead of by rooms.</param>
public sealed record TroveItemDef(TroveItemId Id, string Name, string Description, TroveItemKind Kind, Color Color, int Recharge = 0, float RechargeSeconds = 0);

/// <summary>Two items that do something extra together.</summary>
public sealed record TroveSynergy(string Name, string Description, TroveItemId A, TroveItemId B);

/// <summary>
/// Tank Troves items. Each floor has an item room (an item on a pedestal), and cleared rooms sometimes leave
/// something behind. Active items sit in your ITEM slot (one at a time) and are used with the ITEM button (or Y);
/// some are used up, others recharge as you clear rooms. Instant ones apply right away; passive ones last the run.
/// Everything stacks with everything: every shell you fire (normal shots, Triple Shot, Shell Burst, your buddy's)
/// gets all your shell items, and some pairs unlock a named synergy on top.
/// Every phone knows every player's items (pickups are synced); each phone only uses its own players' actives.
/// </summary>
public static class TroveItems {
    public static readonly TroveItemDef[] All = [
        new(TroveItemId.Rocket, "Rockets", "Your next 5 shots are rockets", TroveItemKind.Active, new Color(240, 120, 30)),
        new(TroveItemId.Homing, "Homing Shells", "Your shots home in, for the rest of the floor", TroveItemKind.Active, new Color(60, 140, 230)),
        new(TroveItemId.SliderBomb, "Slider Bombs", "Your next 5 mines slide forward and explode", TroveItemKind.Active, new Color(60, 60, 70)),
        new(TroveItemId.ExtraLife, "Extra Life", "+1 life", TroveItemKind.Instant, new Color(220, 50, 60)),
        new(TroveItemId.Shield, "Shield", "Blocks the next hit", TroveItemKind.Instant, new Color(150, 170, 200)),
        new(TroveItemId.SpeedTreads, "Speed Treads", "Drive faster", TroveItemKind.Passive, new Color(240, 200, 40)),
        new(TroveItemId.BouncyShells, "Bouncy Shells", "+1 bounce on every shot", TroveItemKind.Passive, new Color(60, 200, 210)),
        new(TroveItemId.ExtraShell, "Extra Shell", "+1 shell on screen at once", TroveItemKind.Passive, new Color(150, 110, 200)),
        new(TroveItemId.MinePack, "Mine Pack", "+1 mine at once", TroveItemKind.Passive, new Color(230, 190, 40)),
        new(TroveItemId.Detonator, "Detonator", "Press the mine button again to blow up your mines", TroveItemKind.Passive, new Color(200, 50, 40)),
        new(TroveItemId.BlockBust, "Block Bust", "Breaks every breakable block in the room", TroveItemKind.Active, new Color(205, 140, 80)),
        new(TroveItemId.BigMines, "Big Mines", "Your mines blast twice as far (other blasts 1.5x)", TroveItemKind.Passive, new Color(250, 150, 40)),
        new(TroveItemId.TripleShot, "Triple Shot", "Every shot fires 3 shells in a spread", TroveItemKind.Passive, new Color(230, 90, 120)),
        new(TroveItemId.QuickReload, "Quick Reload", "Shorter wait between shots", TroveItemKind.Passive, new Color(110, 200, 90)),
        new(TroveItemId.OrbitGuard, "Orbit Guard", "A ball circles you and blocks enemy shells", TroveItemKind.Passive, new Color(170, 180, 200)),
        new(TroveItemId.TreasureMap, "Treasure Map", "Shows every room on the map, secret ones too", TroveItemKind.Passive, new Color(200, 160, 90)),
        new(TroveItemId.BuddyTank, "Buddy Tank", "A little tank follows you and shoots enemies", TroveItemKind.Passive, new Color(80, 160, 240)),
        new(TroveItemId.HeavyShells, "Heavy Shells", "Big, slower shells that go through an enemy and smash cork", TroveItemKind.Passive, new Color(110, 100, 90)),
        new(TroveItemId.ExplosiveShells, "Explosive Shells", "Your shells blow up where they land (not on you)", TroveItemKind.Passive, new Color(240, 70, 40)),
        new(TroveItemId.GhostTreads, "Ghost Treads", "Drive straight through breakable blocks", TroveItemKind.Passive, new Color(190, 200, 230)),
        new(TroveItemId.ShellShield, "Shell Shield", "Your shells knock enemy shells out of the air more easily", TroveItemKind.Passive, new Color(120, 150, 220)),
        new(TroveItemId.BossHunter, "Boss Hunter", "Your hits do double damage to bosses", TroveItemKind.Passive, new Color(190, 30, 40)),
        new(TroveItemId.Stopwatch, "Stopwatch", "Freezes every enemy and their shells for 5 seconds", TroveItemKind.Active, new Color(90, 190, 220), Recharge: 2),
        new(TroveItemId.ShellBurst, "Shell Burst", "Fires 8 shells in a ring around you", TroveItemKind.Active, new Color(240, 170, 60), Recharge: 1),
        new(TroveItemId.Nuke, "Nuke", "A huge explosion around you that you survive", TroveItemKind.Active, new Color(250, 220, 40)),
        new(TroveItemId.Decoy, "Decoy", "Drops a fake tank that enemies aim at for 6 seconds", TroveItemKind.Active, new Color(160, 120, 80), Recharge: 1),
        new(TroveItemId.Invincibility, "Invincibility", "5 seconds where nothing hurts you; ram enemies to destroy them", TroveItemKind.Active, new Color(255, 200, 60), Recharge: 3),
        new(TroveItemId.Reroll, "Reroll", "Swaps the item on the floor for a different one", TroveItemKind.Active, new Color(230, 230, 240), Recharge: 2),
        new(TroveItemId.SplitShot, "Split Shot", "Shells split in two the first time they bounce", TroveItemKind.Passive, new Color(240, 140, 200)),
        new(TroveItemId.SniperShells, "Sniper Shells", "Much faster shells, +1 bounce", TroveItemKind.Passive, new Color(90, 110, 80)),
        new(TroveItemId.ChainLightning, "Chain Lightning", "Hitting an enemy zaps the nearest other enemy", TroveItemKind.Passive, new Color(120, 200, 255)),
        new(TroveItemId.RegenPlating, "Regen Plating", "+1 shield every 3 rooms you clear", TroveItemKind.Passive, new Color(100, 210, 160)),
        new(TroveItemId.ClusterMines, "Cluster Mines", "Your mines burst into 4 more explosions", TroveItemKind.Passive, new Color(230, 120, 60)),
        new(TroveItemId.HomingMines, "Homing Mines", "Your mines crawl toward the nearest enemy", TroveItemKind.Passive, new Color(200, 170, 60)),
        new(TroveItemId.TinyTank, "Tiny Tank", "You're smaller and harder to hit, but a bit slower", TroveItemKind.Passive, new Color(150, 220, 120)),
        new(TroveItemId.VampireTreads, "Vampire Treads", "Every 6 enemies you destroy gives you a shield", TroveItemKind.Passive, new Color(150, 20, 40)),
        new(TroveItemId.RicochetMaster, "Ricochet Master", "Shells get faster and bigger after every bounce", TroveItemKind.Passive, new Color(255, 160, 60)),
        new(TroveItemId.GiantTank, "Giant Tank", "You're bigger and slower, with +1 shield every floor", TroveItemKind.Passive, new Color(120, 90, 160)),
        new(TroveItemId.EchoShot, "Echo Shot", "Every shot fires again a moment later", TroveItemKind.Passive, new Color(170, 150, 230)),
        new(TroveItemId.Airstrike, "Airstrike", "5 explosions rain down on enemies", TroveItemKind.Active, new Color(200, 70, 50), Recharge: 3),
        new(TroveItemId.WallBuilder, "Wall Builder", "Drops 3 cork blocks in front of you for cover", TroveItemKind.Active, new Color(225, 185, 130), Recharge: 1),
        new(TroveItemId.Dash, "Dash", "A quick burst of speed the way you're driving", TroveItemKind.Active, new Color(80, 220, 230), RechargeSeconds: 3),
        new(TroveItemId.MineStorm, "Mine Storm", "Drops 6 mines around you at once", TroveItemKind.Active, new Color(240, 200, 40)),
        new(TroveItemId.Convert, "Convert", "The nearest enemy fights for you for the rest of the room", TroveItemKind.Active, new Color(220, 120, 220), Recharge: 4),
        new(TroveItemId.ShrinkRay, "Shrink Ray", "Every enemy turns tiny and slow for 6 seconds", TroveItemKind.Active, new Color(120, 230, 150), Recharge: 2),
        new(TroveItemId.RepairKit, "Repair Kit", "+2 shields right now", TroveItemKind.Active, new Color(200, 210, 220)),
        new(TroveItemId.LightningStorm, "Lightning Storm", "3 bolts each zap a random enemy", TroveItemKind.Active, new Color(110, 160, 255), Recharge: 2),
        new(TroveItemId.GhostShells, "Ghost Shells", "Your shells pass through one wall instead of bouncing", TroveItemKind.Passive, new Color(200, 210, 255)),
        new(TroveItemId.LastStand, "Last Stand", "With no shields and nobody else left, you shoot twice as fast", TroveItemKind.Passive, new Color(200, 60, 40)),
        new(TroveItemId.MagnetShells, "Magnet Shells", "Your shells curve toward enemy shells to shoot them down", TroveItemKind.Passive, new Color(200, 60, 70)),
        new(TroveItemId.Afterburner, "Afterburner", "You leave a fire trail that burns enemies", TroveItemKind.Passive, new Color(255, 120, 30)),
        new(TroveItemId.RicochetShield, "Ricochet Shield", "Enemy shells that have bounced break when they get near you", TroveItemKind.Passive, new Color(140, 190, 230)),
        new(TroveItemId.SecondWind, "Second Wind", "Once per floor, survive a hit that would destroy you", TroveItemKind.Passive, new Color(250, 240, 200)),
        new(TroveItemId.Clone, "Clone", "A copy of you on the other side of the room copies your shots for 8 seconds", TroveItemKind.Active, new Color(90, 150, 240), Recharge: 3),
        new(TroveItemId.EMP, "EMP", "Breaks every enemy shell; enemies can't shoot for 3 seconds", TroveItemKind.Active, new Color(80, 230, 255), Recharge: 2),
        new(TroveItemId.Teleport, "Teleport", "Warp to a safe spot in the room", TroveItemKind.Active, new Color(170, 90, 230), Recharge: 1),
        new(TroveItemId.TurretDrop, "Turret Drop", "Places a turret that shoots enemies for the rest of the room", TroveItemKind.Active, new Color(150, 150, 120), Recharge: 2),
        new(TroveItemId.SupplyDrop, "Supply Drop", "Drops a random item", TroveItemKind.Active, new Color(120, 170, 90)),
        new(TroveItemId.MirrorShield, "Mirror Shield", "For 4 seconds, enemy shells that hit you bounce back at them", TroveItemKind.Active, new Color(210, 230, 250), Recharge: 3),
    ];
    public static TroveItemDef Def(TroveItemId id) => All[(int)id];

    public static readonly TroveSynergy[] Synergies = [
        new("Wrecking Ball", "Heavy shells blow up every time they punch through", TroveItemId.HeavyShells, TroveItemId.ExplosiveShells),
        new("Blast Proof", "Your own mines and bombs can't hurt you", TroveItemId.BigMines, TroveItemId.Detonator),
        new("Bulwark", "+1 orbiter, and orbiters are bigger", TroveItemId.OrbitGuard, TroveItemId.ShellShield),
        new("Ricochet Storm", "Wider spread and +1 more bounce", TroveItemId.TripleShot, TroveItemId.BouncyShells),
        new("Machine Gun", "+2 more shells on screen", TroveItemId.QuickReload, TroveItemId.ExtraShell),
        new("Pack Hunter", "Your buddy shoots twice as fast and hurts bosses double", TroveItemId.BuddyTank, TroveItemId.BossHunter),
        new("Fireworks", "Shell explosions are bigger", TroveItemId.ExplosiveShells, TroveItemId.TripleShot),
        new("Minefield", "+1 more mine", TroveItemId.BigMines, TroveItemId.MinePack),
        new("Phantom", "Even faster treads", TroveItemId.GhostTreads, TroveItemId.SpeedTreads),
        new("Shrapnel", "Shells split into three instead of two", TroveItemId.SplitShot, TroveItemId.RicochetMaster),
        new("Carpet Bomb", "Cluster mines burst into 6 bigger explosions", TroveItemId.ClusterMines, TroveItemId.BigMines),
        new("Guided Missile", "Homing mines go off as soon as they reach an enemy", TroveItemId.HomingMines, TroveItemId.Detonator),
        new("Thunderclap", "Every lightning zap explodes", TroveItemId.ChainLightning, TroveItemId.ExplosiveShells),
        new("Zippy", "Tiny and fast: no slowdown, extra speed", TroveItemId.TinyTank, TroveItemId.SpeedTreads),
        new("Fortress", "Regen Plating every 2 rooms", TroveItemId.GiantTank, TroveItemId.RegenPlating),
        new("Bloodbank", "Vampire Treads every 4 kills", TroveItemId.VampireTreads, TroveItemId.RegenPlating),
        new("Railgun", "Sniper shells go through one more enemy", TroveItemId.SniperShells, TroveItemId.HeavyShells),
        new("Echo Chamber", "Echo shells get +1 bounce", TroveItemId.EchoShot, TroveItemId.BouncyShells),
        // with the item in your ITEM slot
        new("Echo Burst", "Shell Burst fires twice", TroveItemId.ShellBurst, TroveItemId.EchoShot),
        new("Booby Trap", "Your decoy blows up when it disappears", TroveItemId.Decoy, TroveItemId.BigMines),
        new("Time Lord", "Stopwatch lasts 8 seconds", TroveItemId.Stopwatch, TroveItemId.QuickReload),
        new("Juggernaut", "Invincibility lasts 8 seconds and rams from further away", TroveItemId.Invincibility, TroveItemId.GiantTank),
        new("Storm Caller", "Lightning Storm throws 5 bolts", TroveItemId.LightningStorm, TroveItemId.ChainLightning),
        new("Battering Ram", "Dashing into enemies damages them", TroveItemId.Dash, TroveItemId.HeavyShells),
        new("Great Wall", "Wall Builder makes walls 5 blocks wide", TroveItemId.WallBuilder, TroveItemId.GiantTank),
        new("Recruiter", "Converted tanks can't be destroyed", TroveItemId.Convert, TroveItemId.BuddyTank),
        new("Saturation", "Airstrike drops 8 bombs, and each one clusters", TroveItemId.Airstrike, TroveItemId.ClusterMines),
        new("Swarm", "Mine Storm drops 10 mines", TroveItemId.MineStorm, TroveItemId.HomingMines),
        new("Stomp", "Drive into shrunken enemies to crush them", TroveItemId.ShrinkRay, TroveItemId.GiantTank),
        new("Fortune Teller", "Reroll recharges twice as fast", TroveItemId.Reroll, TroveItemId.TreasureMap),
        new("Overhaul", "Repair Kit gives 3 shields", TroveItemId.RepairKit, TroveItemId.RegenPlating),
        new("Tesla Coil", "Your orbiters zap nearby enemies", TroveItemId.OrbitGuard, TroveItemId.ChainLightning),
        new("Nuclear Option", "The Nuke is twice as big", TroveItemId.Nuke, TroveItemId.BigMines),
        new("Bunker Buster", "Rockets go through 2 enemies", TroveItemId.Rocket, TroveItemId.HeavyShells),
        new("Smart Bomb", "Slider bombs steer toward enemies", TroveItemId.SliderBomb, TroveItemId.Homing),
        new("Seeker Swarm", "Homing shells find targets from much further away", TroveItemId.Homing, TroveItemId.TripleShot),
        new("Demolition", "Every block Block Bust breaks explodes", TroveItemId.BlockBust, TroveItemId.ExplosiveShells),
        new("Phase Rifle", "Ghost shells pass through 2 walls", TroveItemId.GhostShells, TroveItemId.SniperShells),
        new("Not Today", "Second Wind leaves you with 2 shields", TroveItemId.SecondWind, TroveItemId.LastStand),
        new("Interceptor", "Magnet shells pull harder from further away", TroveItemId.MagnetShells, TroveItemId.ShellShield),
        new("Fire Walker", "Your fire trail lasts longer and burns wider", TroveItemId.Afterburner, TroveItemId.SpeedTreads),
        new("Deflector", "Ricochet Shield works twice as far away", TroveItemId.RicochetShield, TroveItemId.OrbitGuard),
        new("Double Trouble", "Your clone lasts 12 seconds", TroveItemId.Clone, TroveItemId.EchoShot),
        new("Overload", "EMP also zaps every enemy", TroveItemId.EMP, TroveItemId.ChainLightning),
        new("Blink Strike", "Teleporting leaves an explosion where you were", TroveItemId.Teleport, TroveItemId.ExplosiveShells),
        new("Squad", "Turret Drop places 2 turrets", TroveItemId.TurretDrop, TroveItemId.BuddyTank),
        new("Prospector", "Supply Drop drops a secret-room item", TroveItemId.SupplyDrop, TroveItemId.TreasureMap),
        new("Spiked Mirror", "Bounced-back shells get all your shell items", TroveItemId.MirrorShield, TroveItemId.HeavyShells),
        new("Inferno", "Afterburner fire explodes when it burns an enemy", TroveItemId.Afterburner, TroveItemId.ExplosiveShells),
    ];

    // item rooms draw from nearly everything; challenge and secret rooms have stronger pools; rooms mostly drop small stuff
    public static readonly TroveItemId[] PedestalPool = [
        TroveItemId.Rocket, TroveItemId.Homing, TroveItemId.SliderBomb, TroveItemId.BlockBust, TroveItemId.Stopwatch, TroveItemId.ShellBurst,
        TroveItemId.Decoy, TroveItemId.Reroll, TroveItemId.SpeedTreads, TroveItemId.BouncyShells, TroveItemId.ExtraShell, TroveItemId.MinePack,
        TroveItemId.Detonator, TroveItemId.BigMines, TroveItemId.TripleShot, TroveItemId.QuickReload, TroveItemId.OrbitGuard, TroveItemId.TreasureMap,
        TroveItemId.HeavyShells, TroveItemId.ExplosiveShells, TroveItemId.GhostTreads, TroveItemId.ShellShield, TroveItemId.BossHunter,
        TroveItemId.BuddyTank, TroveItemId.ExtraLife, TroveItemId.Shield,
        TroveItemId.SplitShot, TroveItemId.SniperShells, TroveItemId.ChainLightning, TroveItemId.RegenPlating, TroveItemId.ClusterMines,
        TroveItemId.HomingMines, TroveItemId.TinyTank, TroveItemId.VampireTreads, TroveItemId.RicochetMaster, TroveItemId.GiantTank,
        TroveItemId.EchoShot, TroveItemId.WallBuilder, TroveItemId.Dash, TroveItemId.MineStorm, TroveItemId.ShrinkRay, TroveItemId.RepairKit,
        TroveItemId.LightningStorm, TroveItemId.GhostShells, TroveItemId.LastStand, TroveItemId.MagnetShells, TroveItemId.Afterburner,
        TroveItemId.RicochetShield, TroveItemId.SecondWind, TroveItemId.Clone, TroveItemId.EMP, TroveItemId.Teleport, TroveItemId.TurretDrop,
        TroveItemId.SupplyDrop, TroveItemId.MirrorShield,
    ];
    public static readonly TroveItemId[] ChallengePool = [
        TroveItemId.TripleShot, TroveItemId.QuickReload, TroveItemId.HeavyShells, TroveItemId.ExplosiveShells, TroveItemId.BigMines,
        TroveItemId.OrbitGuard, TroveItemId.ShellBurst, TroveItemId.Stopwatch, TroveItemId.BossHunter, TroveItemId.Invincibility, TroveItemId.Nuke,
        TroveItemId.ChainLightning, TroveItemId.EchoShot, TroveItemId.Airstrike, TroveItemId.Convert, TroveItemId.SplitShot, TroveItemId.RicochetMaster,
        TroveItemId.Clone, TroveItemId.MirrorShield, TroveItemId.TurretDrop, TroveItemId.SecondWind,
    ];
    public static readonly TroveItemId[] SecretPool = [
        TroveItemId.Nuke, TroveItemId.Invincibility, TroveItemId.BuddyTank, TroveItemId.TripleShot, TroveItemId.OrbitGuard,
        TroveItemId.BossHunter, TroveItemId.TreasureMap, TroveItemId.ExplosiveShells, TroveItemId.HeavyShells,
        TroveItemId.Airstrike, TroveItemId.Convert, TroveItemId.EchoShot, TroveItemId.VampireTreads,
        TroveItemId.SecondWind, TroveItemId.Clone, TroveItemId.GhostShells,
    ];
    /// <summary>Items that stack: every extra one adds more.</summary>
    public static bool Stacks(TroveItemId id) => id is TroveItemId.BouncyShells or TroveItemId.ExtraShell or TroveItemId.MinePack
        or TroveItemId.SpeedTreads or TroveItemId.QuickReload or TroveItemId.OrbitGuard or TroveItemId.HeavyShells or TroveItemId.TripleShot
        or TroveItemId.SniperShells or TroveItemId.RegenPlating or TroveItemId.VampireTreads;
    // cleared rooms mostly leave hearts and shields, sometimes a stacking item or a Block Bust
    public static readonly TroveItemId[] DropPool = [
        TroveItemId.ExtraLife, TroveItemId.ExtraLife, TroveItemId.Shield, TroveItemId.Shield, TroveItemId.Shield,
        TroveItemId.BouncyShells, TroveItemId.ExtraShell, TroveItemId.MinePack, TroveItemId.BlockBust, TroveItemId.RepairKit,
    ];

    /// <summary>How many ITEM-button items one player can carry (switch between them with the swap button or X).</summary>
    public const int MAX_ACTIVES = 3;

    public sealed class PlayerItems {
        /// <summary>The ITEM-button items being carried, and which one the button uses.</summary>
        public readonly List<TroveItemId> Actives = [];
        public int Selected;
        /// <summary>Rooms cleared since each carried item was last used (it's ready at its Recharge).</summary>
        public readonly Dictionary<TroveItemId, int> Charges = [];
        /// <summary>When the selection last changed (the description shows up bigger for a moment).</summary>
        public double SelectedAt = -10;
        public TroveItemId? Active => Actives.Count > 0 ? Actives[Math.Clamp(Selected, 0, Actives.Count - 1)] : null;
        public int Charge {
            get => Active is { } a && Charges.TryGetValue(a, out var c) ? c : 0;
            set { if (Active is { } a) Charges[a] = value; }
        }
        public int ChargeOf(TroveItemId id) => Charges.TryGetValue(id, out var c) ? c : 0;
        public bool IsReady(TroveItemId id) => ChargeOf(id) >= Def(id).Recharge && (Def(id).RechargeSeconds <= 0 || TroveRun.PlayTime >= ReadyAt);
        /// <summary>Picks up an ITEM-button item: a free slot if there is one, otherwise it replaces the selected one.</summary>
        public void AddActive(TroveItemId id) {
            Charges[id] = Def(id).Recharge; // comes charged
            int have = Actives.IndexOf(id);
            if (have >= 0)
                Selected = have; // already carrying it: just recharged
            else if (Actives.Count < MAX_ACTIVES) {
                Actives.Add(id);
                Selected = Actives.Count - 1;
            }
            else
                Actives[Selected] = id;
            SelectedAt = TroveRun.PlayTime;
        }
        /// <summary>The selected item is used up.</summary>
        public void RemoveActive() {
            if (Active is not { } a)
                return;
            Actives.RemoveAt(Math.Clamp(Selected, 0, Actives.Count - 1));
            Charges.Remove(a);
            Selected = Actives.Count == 0 ? 0 : Math.Min(Selected, Actives.Count - 1);
            SelectedAt = TroveRun.PlayTime;
        }
        /// <summary>Selects the next carried item.</summary>
        public bool Cycle() {
            if (Actives.Count < 2)
                return false;
            Selected = (Selected + 1) % Actives.Count;
            SelectedAt = TroveRun.PlayTime;
            return true;
        }
        public bool HomingOn;        // until the floor ends
        public int SliderBombs;
        public int Shields;
        public double InvincibleUntil = -1;
        public double ReadyAt = -1;     // time-based actives (Dash)
        public int RoomsForRegen, KillsForVampire;
        public double DashUntil = -1, TeslaAt;
        public double MirrorUntil = -1, CloneUntil = -1, LastFireDrop;
        public bool SecondWindUsed, LastStandOn;
        public AITank? CloneTank;
        public readonly HashSet<Tank> Rammed = [];
        public bool WasInvincible;
        public readonly Dictionary<TroveItemId, int> Passives = [];
        public int Count(TroveItemId id) => Passives.TryGetValue(id, out var n) ? n : 0;
        /// <summary>Has it: a whole-run item, or the item in the ITEM slot (so actives can be part of synergies).</summary>
        public int Id;
        public bool Has(TroveItemId id) => Count(id) > 0 || Actives.Contains(id)
            // one-use items count while they're working
            || id == TroveItemId.Homing && HomingOn || id == TroveItemId.SliderBomb && SliderBombs > 0
            || id == TroveItemId.Rocket && MissileUpgrade.ShotsLeft(Id) > 0;
        public bool Has(TroveSynergy s) => Has(s.A) && Has(s.B);
        public bool ActiveReady => Active is { } a && IsReady(a);
    }

    public static readonly PlayerItems[] Players = [new() { Id = 0 }, new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 }];

    /// <summary>The last pickup, for the banner (name, description, how many you have now, when).</summary>
    public static (TroveItemDef Def, double Time, int Stack)? LastPickup;
    /// <summary>A synergy that just switched on, for its banner.</summary>
    public static (TroveSynergy Synergy, double Time)? LastSynergy;

    // run-wide effects
    public static double FrozenUntil = -1;
    public static AITank? DecoyTank;
    private static double _decoyUntil;
    private static int _decoyOwner;
    public static readonly AITank?[] Buddies = new AITank?[4];
    /// <summary>Lightning to draw: from, to, when.</summary>
    public static readonly List<(Vector2 From, Vector2 To, double Time)> Bolts = [];
    private static readonly List<(AITank Tank, Vector3 Scale, float Speed)> _shrunk = [];
    private static double _shrinkUntil = -1;
    private static readonly List<(int Player, double Due, List<(Vector2 Vel, int Type, uint Ricochets)> Shots)> _echoes = [];
    private static readonly List<(Vector2 At, double Due, int Player)> _airstrikes = [];
    private static bool _echoing;
    public static double EmpUntil = -1;
    /// <summary>Afterburner fire: where, until when, whose.</summary>
    public static readonly List<(Vector2 At, double Until, int Player)> Fires = [];
    private static readonly Dictionary<Tank, double> _burnedAt = [];
    /// <summary>Turrets (Turret Drop) and whose they are.</summary>
    public static readonly List<(AITank Tank, int Player)> Turrets = [];

    private static bool _hooked;
    private static PlayerItems P(int id) => Players[Math.Clamp(id, 0, 3)];
    public static bool Has(int playerId, TroveSynergy s) => playerId is >= 0 and < 4 && Players[playerId].Has(s);
    private static TroveSynergy Syn(string name) => Synergies.First(s => s.Name == name);
    private static double Now => TroveRun.PlayTime;
    public static bool Frozen => TroveRun.Active && Now < FrozenUntil;

    public static void Reset() {
        for (int i = 0; i < 4; i++)
            Players[i] = new PlayerItems { Id = i };
        LastPickup = null;
        LastSynergy = null;
        FrozenUntil = -1;
        DecoyTank = null;
        Array.Clear(Buddies);
        Bolts.Clear();
        _shrunk.Clear();
        _echoes.Clear();
        _airstrikes.Clear();
        Fires.Clear();
        Turrets.Clear();
        EmpUntil = -1;
        InstallHooks();
    }

    public static void NewFloor() {
        foreach (var p in Players)
            p.HomingOn = false;
    }

    /// <summary>Players whose items this phone looks after.</summary>
    public static bool IsLocal(int playerId) =>
        LocalCoop.Active ? playerId < LocalCoop.PlayerCount : !Client.IsConnected() ? playerId == 0 : NetPlay.IsClientMatched(playerId);

    private static bool IsEnemy(Tank t) => t.Team != TeamID.Red;

    /// <summary>The ways Tank Troves reaches into the rest of the game (once).</summary>
    private static void InstallHooks() {
        if (_hooked)
            return;
        _hooked = true;
        AITank.IsFrozen = t => Frozen && IsEnemy(t) || TroveRun.InReady;
        Tank.MovementLocked = _ => TroveRun.InReady;
        Shell.IsFrozen = s => Frozen && s.Owner is AITank ai && IsEnemy(ai);
        AITank.TargetOverride = ai => TroveRun.Active && DecoyTank is { IsDestroyed: false } d && IsEnemy(ai) ? d : null;
        Tank.ExtraArmorDamage = (tank, ctx) => {
            if (!TroveRun.Active || !(ReferenceEquals(tank, TroveRun.BossTank) || TroveBosses.IsBoss(tank)))
                return 0;
            if (ctx.Source is PlayerTank pt && P(pt.PlayerId).Has(TroveItemId.BossHunter))
                return 1;
            if (ctx.Source is AITank ai && OwnerOf(ai) is int owner and >= 0 && Has(owner, Syn("Pack Hunter")))
                return 1;
            return 0;
        };
        // chain lightning: a hit zaps the nearest other enemy
        Shell.OnHitTank = (shell, hit) => {
            if (!TroveRun.Active || !IsEnemy(hit) || shell.Owner is null)
                return;
            // whose shell: a player's (on their phone), or a buddy's (on the host, which runs the AI)
            int pid = shell.Owner is PlayerTank sp ? sp.PlayerId : Array.IndexOf(Buddies, shell.Owner as AITank);
            if (pid < 0 || !P(pid).Has(TroveItemId.ChainLightning))
                return;
            bool runsHere = shell.Owner is PlayerTank ? IsLocal(pid) : !Client.IsConnected() || Client.IsHost();
            if (runsHere)
                Chain(shell.Owner, pid, hit, Has(pid, Syn("Thunderclap")) ? 2 : 1);
        };
        // vampire treads: count kills
        Tank.OnDestroyed = (victim, ctx) => {
            if (!TroveRun.Active || victim is not AITank || !IsEnemy(victim) || ctx.Source is not PlayerTank pt || !IsLocal(pt.PlayerId))
                return;
            var p = P(pt.PlayerId);
            int n = p.Count(TroveItemId.VampireTreads);
            if (n == 0)
                return;
            int need = Math.Max(2, (p.Has(Syn("Bloodbank")) ? 4 : 6) - (n - 1));
            if (++p.KillsForVampire >= need) {
                p.KillsForVampire = 0;
                p.Shields++;
                ApplyTo(pt);
                SoundPlayer.PlaySoundInstance("Assets/sounds/armor_ding_1.ogg", SoundContext.Effect, 0.8f);
            }
        };
        // cluster mines
        Mine.OnExplode += mine => {
            if (TroveRun.Active && mine.Owner is PlayerTank pt && P(pt.PlayerId).Has(TroveItemId.ClusterMines))
                ClusterBurst(pt, mine.Position, mine.SparesOwner);
        };
        // slider bombs are mines too: they cluster
        Shell.OnDestroy += (shell, _) => {
            if (TroveRun.Active && shell.IsSliderBomb && shell.Owner is PlayerTank pt && P(pt.PlayerId).Has(TroveItemId.ClusterMines))
                ClusterBurst(pt, shell.Position, shell.SparesOwner);
        };
        // bosses: Shield Commander's shield, and boss shells' special touches
        Shell.Blocks = TroveBosses.ShieldBlocks;
        Tank.OnShoot += TroveBosses.OnBossShoot;
        // EMP: enemies can't shoot
        Tank.ShootBlocked = t => TroveRun.Active && (Now < EmpUntil && t is AITank && IsEnemy(t) || TroveRun.InReady);
        // Second Wind: once a floor, a hit that would destroy you doesn't
        Tank.PreventDestroy = t => {
            if (!TroveRun.Active || t is not PlayerTank pt || pt.PlayerId is < 0 or >= 4)
                return false;
            var p = P(pt.PlayerId);
            if (!p.Has(TroveItemId.SecondWind) || p.SecondWindUsed)
                return false;
            p.SecondWindUsed = true;
            int shields = p.Has(Syn("Not Today")) ? 2 : 1;
            if (IsLocal(pt.PlayerId)) {
                p.Shields += shields;
                ApplyTo(pt);
            }
            else
                pt.Properties.Armor = new TankArmor(pt, shields);
            GameHandler.Particles.MakeShineSpot(pt.Position3D + new Vector3(0, 12, 0), Color.White, 2f);
            SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.7f, pitchOverride: 0.2f);
            return true;
        };
        // Mirror Shield: enemy shells bounce back
        Shell.Reflects = (shell, tank) => TroveRun.Active && tank is PlayerTank pt && pt.PlayerId is >= 0 and < 4 && Now < P(pt.PlayerId).MirrorUntil
            && shell.Owner is not null && IsEnemy(shell.Owner);
        Shell.OnReflected = (shell, tank) => {
            if (tank is PlayerTank pt && Has(pt.PlayerId, Syn("Spiked Mirror")))
                ModifyShell(pt.PlayerId, shell);
        };
        // buddy tanks fire shells with their owner's items too
        Tank.OnShoot += (tank, shell) => {
            if (TroveRun.Active && tank is AITank ai && OwnerOf(ai) is int owner and >= 0)
                ModifyShell(owner, shell);
        };
    }

    /// <summary>Cluster Mines: a ring of smaller explosions (Carpet Bomb: more and bigger).</summary>
    private static void ClusterBurst(PlayerTank pt, Vector2 at, bool sparesOwner) {
        bool carpet = Has(pt.PlayerId, Syn("Carpet Bomb"));
        int n = carpet ? 6 : 4;
        for (int i = 0; i < n; i++) {
            var dir = Vector2.UnitX.Rotate(i * MathHelper.TwoPi / n + 0.4f);
            new Explosion(at + dir * (carpet ? 60f : 45f), (carpet ? 5f : 3.5f) * BlastSize(pt.PlayerId) / (carpet ? 1.5f : 1f), pt, 0.3f, 0.3f) { SparesOwner = sparesOwner };
        }
    }

    /// <summary>Chain Lightning: from a hit tank to the nearest other enemy (and on, with Thunderclap).</summary>
    private static void Chain(Tank source, int pid, Tank from, int jumps) {
        var hitSet = new HashSet<Tank> { from };
        var at = from;
        for (int j = 0; j < jumps; j++) {
            var next = GameHandler.AllAITanks.Where(t => t is { IsDestroyed: false } && IsEnemy(t) && !hitSet.Contains(t) && Vector2.Distance(t.Position, at.Position) < 170f)
                .OrderBy(t => Vector2.Distance(t!.Position, at.Position)).FirstOrDefault();
            if (next is null)
                return;
            Zap(source, pid, at.Position, next);
            hitSet.Add(next);
            at = next;
        }
    }

    // ------------------------------------------------------------------ picking up

    public static void Give(int playerId, TroveItemId id) {
        if (playerId is < 0 or >= 4)
            return;
        var def = Def(id);
        var p = Players[playerId];
        bool local = IsLocal(playerId);
        var synergiesBefore = Synergies.Where(p.Has).ToList();
        int stack = def.Kind == TroveItemKind.Passive ? p.Count(id) + 1 : 1;
        LastPickup = (def, Now, stack);
        SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.4f);
        switch (def.Kind) {
            case TroveItemKind.Active:
                p.AddActive(id);
                break;
            case TroveItemKind.Instant:
                if (id == TroveItemId.ExtraLife && local) {
                    PlayerTank.Lives[playerId]++;
                    if (Client.IsConnected())
                        Client.SendLives();
                }
                else if (id == TroveItemId.Shield && local)
                    p.Shields++;
                break;
            case TroveItemKind.Passive:
                p.Passives[id] = p.Count(id) + 1;
                break;
        }
        if (Synergies.FirstOrDefault(s => p.Has(s) && !synergiesBefore.Contains(s)) is { } fresh) {
            LastSynergy = (fresh, Now);
            SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.5f, pitchOverride: 0.4f);
        }
        if (id == TroveItemId.TreasureMap)
            TroveRun.RevealMap();
        if (id == TroveItemId.GiantTank && local)
            p.Shields++;
        if (id == TroveItemId.BuddyTank && Buddies[playerId] is null && CampaignGlobals.InMission)
            SpawnBuddy(playerId);
        if (local && GameHandler.AllPlayerTanks[playerId] is { IsDestroyed: false } tank)
            ApplyTo(tank);
    }

    /// <summary>The synergy this item would switch on for this player, if any (shown above items on the floor).</summary>
    public static TroveSynergy? SynergyHint(int playerId, TroveItemId id) {
        var p = P(playerId);
        return Synergies.FirstOrDefault(s => !p.Has(s) && (s.A == id && p.Has(s.B) || s.B == id && p.Has(s.A)));
    }

    // ------------------------------------------------------------------ the ITEM slot

    /// <summary>True when this player has an item in the ITEM slot (whether or not it's charged).</summary>
    public static bool HasActive(int playerId) => TroveRun.Active && playerId is >= 0 and < 4 && Players[playerId].Active is not null;

    /// <summary>The swap button / X: the ITEM button now uses the next carried item.</summary>
    public static void CycleActive(int playerId) {
        if (!TroveRun.Active || playerId is < 0 or >= 4)
            return;
        if (Players[playerId].Cycle())
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.8f);
    }
    /// <summary>Selects a carried item directly (tapping its icon).</summary>
    public static void SelectActive(int playerId, int index) {
        if (!TroveRun.Active || playerId is < 0 or >= 4)
            return;
        var p = Players[playerId];
        if (index < 0 || index >= p.Actives.Count || index == p.Selected)
            return;
        p.Selected = index;
        p.SelectedAt = Now;
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.8f);
    }

    /// <summary>A room was cleared: active items recharge.</summary>
    public static void OnRoomCleared() {
        for (int i = 0; i < 4; i++) {
            var p = Players[i];
            // every carried item recharges, not just the selected one
            foreach (var a in p.Actives)
                if (p.ChargeOf(a) < Def(a).Recharge)
                    p.Charges[a] = Math.Min(Def(a).Recharge, p.ChargeOf(a) + (a == TroveItemId.Reroll && p.Has(Syn("Fortune Teller")) ? 2 : 1));
            // regen plating: a shield every few rooms
            int n = p.Count(TroveItemId.RegenPlating);
            if (n > 0 && IsLocal(i)) {
                int need = Math.Max(1, (p.Has(Syn("Fortress")) ? 2 : 3) - (n - 1));
                if (++p.RoomsForRegen >= need) {
                    p.RoomsForRegen = 0;
                    p.Shields++;
                    if (GameHandler.AllPlayerTanks[i] is { IsDestroyed: false } t)
                        ApplyTo(t);
                }
            }
        }
        RestoreShrunk();
    }

    /// <summary>A new floor: Giant Tank gives a shield.</summary>
    public static void OnNewFloor() {
        for (int i = 0; i < 4; i++) {
            Players[i].SecondWindUsed = false;
            if (IsLocal(i) && Players[i].Has(TroveItemId.GiantTank))
                Players[i].Shields += Players[i].Count(TroveItemId.GiantTank);
        }
    }

    /// <summary>The player a helper tank (buddy or turret) belongs to, or -1.</summary>
    public static int OwnerOf(AITank t) {
        int b = Array.IndexOf(Buddies, t);
        if (b >= 0)
            return b;
        foreach (var (tt, pl) in Turrets)
            if (ReferenceEquals(tt, t))
                return pl;
        return -1;
    }

    public static void UseActive(int playerId) {
        if (!HasActive(playerId))
            return;
        var p = Players[playerId];
        if (!p.ActiveReady || !CampaignGlobals.InMission) {
            SoundPlayer.SoundError();
            return;
        }
        var id = p.Active!.Value;
        var tank = GameHandler.AllPlayerTanks[playerId];
        switch (id) {
            case TroveItemId.Rocket:
                MissileUpgrade.StartShots(playerId, 5);
                break;
            case TroveItemId.Homing:
                p.HomingOn = true;
                break;
            case TroveItemId.SliderBomb:
                p.SliderBombs += 5;
                break;
            case TroveItemId.BlockBust:
                BustBlocks(playerId);
                SendAny(TroveRun.EV_BLOCK_BUST, playerId);
                break;
            case TroveItemId.Stopwatch:
                int secs = Has(playerId, Syn("Time Lord")) ? 8 : 5;
                StartFreeze(secs);
                SendAny(TroveRun.EV_STOPWATCH, secs);
                break;
            case TroveItemId.ShellBurst:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                ShellBurst(tank);
                break;
            case TroveItemId.Nuke:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                Nuke(playerId);
                SendAny(TroveRun.EV_NUKE, playerId);
                break;
            case TroveItemId.Decoy:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                SpawnDecoy(playerId);
                SendAny(TroveRun.EV_DECOY, playerId);
                break;
            case TroveItemId.Invincibility:
                StartInvincible(playerId);
                SendAny(TroveRun.EV_INVINCIBLE, playerId);
                break;
            case TroveItemId.Airstrike: {
                var targets = GameHandler.AllAITanks.Where(t => t is { IsDestroyed: false } && IsEnemy(t)).ToList();
                if (targets.Count == 0) { SoundPlayer.SoundError(); return; }
                int bombs = Has(playerId, Syn("Saturation")) ? 8 : 5;
                for (int i = 0; i < bombs; i++) {
                    var t = targets[i % targets.Count]!;
                    var at = t.Position + new Vector2(Random.Shared.NextSingle() * 30 - 15, Random.Shared.NextSingle() * 30 - 15);
                    _airstrikes.Add((at, Now + 0.4 + i * 0.25, playerId));
                }
                break;
            }
            case TroveItemId.WallBuilder:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                BuildWall(playerId);
                SendAny(TroveRun.EV_WALL, playerId);
                break;
            case TroveItemId.Dash: {
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                var dir = tank.Velocity.LengthSquared() > 0.01f ? Vector2.Normalize(tank.Velocity) : Vector2.UnitY.Rotate(tank.ChassisRotation);
                tank.KnockbackVelocity = dir * 7f;
                p.DashUntil = Now + 0.5;
                p.Rammed.Clear();
                for (int i = 0; i < 6; i++)
                    GameHandler.Particles.MakeShineSpot(tank.Position3D - new Vector3(dir.X, 0, dir.Y) * i * 6, new Color(120, 230, 255), 0.5f);
                break;
            }
            case TroveItemId.MineStorm:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                int count = Has(playerId, Syn("Swarm")) ? 10 : 6;
                for (int i = 0; i < count; i++) {
                    var pos = tank.Position + Vector2.UnitX.Rotate(i * MathHelper.TwoPi / count) * (count > 6 ? 50f : 40f);
                    var m = new Mine(tank, pos, 600);
                    tank.OwnedMineCount++;
                    AfterMine(tank, m);
                    if (Client.IsConnected())
                        Client.SyncMinePlace(m.Position, m.DetonateTime, tank.WorldId);
                }
                break;
            case TroveItemId.Convert: {
                if (tank is null) { SoundPlayer.SoundError(); return; }
                var target = GameHandler.AllAITanks.Where(t => t is { IsDestroyed: false } && IsEnemy(t) && !ReferenceEquals(t, TroveRun.BossTank) && !TroveBosses.IsBoss(t))
                    .OrderBy(t => Vector2.Distance(t!.Position, tank.Position)).FirstOrDefault();
                if (target is null) { SoundPlayer.SoundError(); return; }
                ConvertTank(target.WorldId * 4 + playerId);
                SendAny(TroveRun.EV_CONVERT, target.WorldId * 4 + playerId);
                break;
            }
            case TroveItemId.ShrinkRay:
                Shrink();
                SendAny(TroveRun.EV_SHRINK, 0);
                break;
            case TroveItemId.RepairKit:
                p.Shields += Has(playerId, Syn("Overhaul")) ? 3 : 2;
                break;
            case TroveItemId.LightningStorm: {
                int bolts = Has(playerId, Syn("Storm Caller")) ? 5 : 3;
                var targets = GameHandler.AllAITanks.Where(t => t is { IsDestroyed: false } && IsEnemy(t)).OrderBy(_ => Random.Shared.Next()).ToList();
                if (targets.Count == 0 || tank is null) { SoundPlayer.SoundError(); return; }
                for (int i = 0; i < bolts; i++) {
                    var t = targets[i % targets.Count]!;
                    Zap(tank, playerId, t.Position + new Vector2(0, -400), t);
                    if (p.Has(TroveItemId.ChainLightning))
                        Chain(tank, playerId, t, 1); // each bolt chains on
                }
                break;
            }
            case TroveItemId.Clone:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                SpawnClone(playerId);
                SendAny(TroveRun.EV_CLONE, playerId);
                break;
            case TroveItemId.EMP:
                StartEmp(playerId);
                SendAny(TroveRun.EV_EMP, playerId);
                break;
            case TroveItemId.Teleport: {
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                var spot = SafeSpot(tank);
                if (spot is null) { SoundPlayer.SoundError(); return; }
                var from = tank.Position;
                GameHandler.Particles.MakeShineSpot(tank.Position3D + new Vector3(0, 10, 0), new Color(190, 120, 255), 1.5f);
                tank.Position = spot.Value;
                tank.Physics.Position = spot.Value / Tank.UNITS_PER_METER;
                GameHandler.Particles.MakeShineSpot(tank.Position3D + new Vector3(0, 10, 0), new Color(190, 120, 255), 1.5f);
                if (Has(playerId, Syn("Blink Strike"))) {
                    new Explosion(from, 7f * BlastSize(playerId), tank, 0.5f) { SparesOwner = true };
                    SendAny(TroveRun.EV_BLAST, EncodePos(from) * 4 + playerId);
                }
                break;
            }
            case TroveItemId.TurretDrop:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                DropTurrets(playerId);
                SendAny(TroveRun.EV_TURRET, playerId);
                break;
            case TroveItemId.SupplyDrop: {
                var pool = Has(playerId, Syn("Prospector")) ? SecretPool : PedestalPool;
                var options = pool.Where(i => i != TroveItemId.SupplyDrop).ToArray();
                var drop = options[Random.Shared.Next(options.Length)];
                p.RemoveActive(); // used up before the new item arrives (it may be another active)
                if (TroveRun.Room is { PickupVisible: false }) {
                    TroveRun.SetPickup(drop);
                    SendAny(TroveRun.EV_REROLL, (int)drop);
                }
                else {
                    Give(playerId, drop);
                    SendAny(TroveRun.EV_GIVE, playerId * 100 + (int)drop);
                }
                SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_rocket.ogg", SoundContext.Effect, 0.7f);
                return;
            }
            case TroveItemId.MirrorShield:
                StartMirror(playerId);
                SendAny(TroveRun.EV_MIRROR, playerId);
                break;
            case TroveItemId.Reroll: {
                if (TroveRun.Room is not { PickupVisible: true } room) {
                    SoundPlayer.SoundError(); // nothing to reroll here: keep it
                    return;
                }
                var options = PedestalPool.Where(i => i != room.Pickup).ToArray();
                var next = options[Random.Shared.Next(options.Length)];
                TroveRun.SetPickup(next);
                SendAny(TroveRun.EV_REROLL, (int)next);
                break;
            }
        }
        // used up, or recharging
        if (Def(id).RechargeSeconds > 0)
            p.ReadyAt = Now + Def(id).RechargeSeconds;
        else if (Def(id).Recharge > 0)
            p.Charge = 0;
        else
            p.RemoveActive();
        SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_rocket.ogg", SoundContext.Effect, 0.7f);
        if (tank is { IsDestroyed: false })
            ApplyTo(tank);
    }

    private static void SendAny(int ev, int a) {
        if (Client.IsConnected())
            Client.SendTroveEvent(ev, a);
    }

    /// <summary>Block Bust: every breakable block in the room breaks (and stays broken if you come back).</summary>
    public static void BustBlocks(int playerId) {
        int n = 0;
        // Demolition: they blow up as they go (a dozen at most)
        bool boom = Has(playerId, Syn("Demolition"));
        var owner = GameHandler.AllPlayerTanks[Math.Clamp(playerId, 0, 3)];
        foreach (var b in Block.AllBlocks.ToArray())
            if (b is not null && b.Properties.IsDestructible) {
                var at = b.Position;
                b.Destroy();
                if (boom && n < 12)
                    new Explosion(at, 3f * BlastSize(playerId), owner, 0.4f, 0.4f) { SparesOwner = true };
                n++;
            }
        TroveRun.Room?.Blocks.RemoveAll(b => b.Type == BlockID.Cork);
        SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_destroy.ogg", SoundContext.Effect, 0.8f);
        TankGame.ClientLog.Write($"Troves: Block Bust broke {n} blocks", Internals.LogType.Info);
    }

    public static void StartFreeze(int seconds) {
        FrozenUntil = Now + seconds;
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 1f, pitchOverride: -0.6f);
    }

    public static void StartInvincible(int playerId) {
        P(playerId).InvincibleUntil = Now + (Has(playerId, Syn("Juggernaut")) ? 8 : 5);
        SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.5f, pitchOverride: 0.6f);
    }

    /// <summary>8 shells in a ring, each with all of this player's shell items.</summary>
    private static void ShellBurst(PlayerTank tank) {
        var props = tank.Properties;
        var shots = new List<(Vector2, int, uint)>();
        for (int i = 0; i < 8; i++) {
            var dir = Vector2.UnitY.Rotate(i * MathHelper.TwoPi / 8f);
            var shell = new Shell(tank.Position + dir * 18f, dir * props.ShellSpeed, props.ShellType, tank, props.RicochetCount, homing: props.ShellHoming);
            shots.Add((shell.Velocity, shell.Type, shell.RicochetsRemaining));
            ModifyShell(tank.PlayerId, shell);
            if (Client.IsConnected())
                Client.SyncShellFire(shell);
        }
        if (P(tank.PlayerId).Has(TroveItemId.EchoShot))
            _echoes.Add((tank.PlayerId, Now + 0.3, shots)); // Echo Burst
    }

    public static void Nuke(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        new Explosion(tank.Position, 15f * BlastSize(playerId) * (Has(playerId, Syn("Nuclear Option")) ? 1.35f : 1f), tank, 0.6f, -0.3f) { SparesOwner = true };
    }

    public static void SpawnDecoy(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        if (DecoyTank is { IsDestroyed: false } old)
            old.Remove(true);
        var d = SpawnAlly(TankID.Brown, tank.Position, 1f);
        d.Properties.Stationary = true;
        d.Properties.ShellLimit = 0;
        d.Properties.MineLimit = 0;
        DecoyTank = d;
        _decoyOwner = playerId;
        _decoyUntil = Now + 6;
    }

    public static void StartMirror(int playerId) {
        P(playerId).MirrorUntil = Now + 4;
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 1f, pitchOverride: 0.7f);
    }

    /// <summary>EMP: every enemy shell breaks, and enemies can't shoot for 3 seconds (Overload: and get zapped).</summary>
    public static void StartEmp(int playerId) {
        EmpUntil = Now + 3;
        foreach (var s in Shell.AllShells.ToArray())
            if (s is not null && s.Owner is not null && IsEnemy(s.Owner))
                s.Destroy(Shell.DestructionContext.WithExplosion, playSound: false);
        SoundPlayer.PlaySoundInstance("Assets/sounds/bullet_ricochet.ogg", SoundContext.Effect, 1f, pitchOverride: -0.8f);
        if (IsLocal(playerId) && Has(playerId, Syn("Overload")) && GameHandler.AllPlayerTanks[playerId] is { } src)
            foreach (var t in GameHandler.AllAITanks.ToArray())
                if (t is { IsDestroyed: false } && IsEnemy(t))
                    Zap(src, playerId, src.Position, t);
    }

    /// <summary>A free spot away from enemies (Teleport).</summary>
    private static Vector2? SafeSpot(Tank tank) {
        Vector2 origin = new Coordinates.BlockMapPosition(0, 0);
        var spots = new List<Vector2>();
        for (int x = 1; x < Coordinates.BlockMapPosition.MAP_WIDTH_169 - 1; x++)
            for (int y = 1; y < Coordinates.BlockMapPosition.MAP_HEIGHT - 1; y++) {
                Vector2 pos = new Coordinates.BlockMapPosition(x, y);
                if (Block.AllBlocks.Any(b => b is not null && Vector2.Distance(b.Position, pos) < Block.SIDE_LENGTH))
                    continue;
                if (GameHandler.AllAITanks.Any(t => t is { IsDestroyed: false } && IsEnemy(t) && Vector2.Distance(t.Position, pos) < 110f))
                    continue;
                if (Mine.AllMines.Any(m => m is not null && Vector2.Distance(m.Position, pos) < 60f))
                    continue;
                if (Vector2.Distance(pos, tank.Position) < 60f)
                    continue;
                spots.Add(pos);
            }
        return spots.Count == 0 ? null : spots[Random.Shared.Next(spots.Count)];
    }

    /// <summary>Clone: a stand-in on the mirrored side of the room that copies your shots (also a target for enemies).</summary>
    public static void SpawnClone(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        var p = P(playerId);
        if (p.CloneTank is { IsDestroyed: false } old)
            old.Remove(true);
        var at = new Vector2(-tank.Position.X, tank.Position.Y);
        if (Block.AllBlocks.Any(b => b is not null && b.Properties.IsSolid && Vector2.Distance(b.Position, at) < Block.SIDE_LENGTH))
            at = new Vector2(tank.Position.X, -tank.Position.Y);
        var c = SpawnAlly(TankID.Marine, at, 1f);
        c.Properties.Stationary = true;
        c.Properties.ShellLimit = 0;
        c.Properties.MineLimit = 0;
        p.CloneTank = c;
        p.CloneUntil = Now + (Has(playerId, Syn("Double Trouble")) ? 12 : 8);
    }

    /// <summary>Turret Drop: a turret (two with Squad) next to the player, for the rest of the room.</summary>
    public static void DropTurrets(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        int n = Has(playerId, Syn("Squad")) ? 2 : 1;
        for (int i = 0; i < n; i++) {
            var at = tank.Position + new Vector2(i == 0 ? -26 : 26, 22);
            var t = SpawnAlly(TankID.Brown, at, 0.85f);
            t.Properties.Stationary = true;
            Turrets.Add((t, playerId));
        }
    }

    /// <summary>Each player's buddy tank (if they have one), next to them; call when a room is ready.</summary>
    public static void SpawnBuddies() {
        Array.Clear(Buddies);
        DecoyTank = null;
        Turrets.Clear();
        Fires.Clear();
        _burnedAt.Clear();
        foreach (var p in Players)
            p.CloneTank = null;
        for (int i = 0; i < 4; i++)
            if (Players[i].Has(TroveItemId.BuddyTank))
                SpawnBuddy(i);
    }

    private static void SpawnBuddy(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        // a free spot next to its player
        Vector2 at = tank.Position;
        foreach (var off in new[] { new Vector2(30, 0), new Vector2(-30, 0), new Vector2(0, 30), new Vector2(0, -30) }) {
            var c = tank.Position + off;
            if (Block.AllBlocks.All(b => b is null || !b.Properties.IsSolid || Vector2.Distance(b.Position, c) > Block.SIDE_LENGTH))
                { at = c; break; }
        }
        var b = SpawnAlly(TankID.Ash, at, 0.75f);
        if (Has(playerId, Syn("Pack Hunter")))
            b.Properties.ShellCooldown = Math.Max(1, b.Properties.ShellCooldown / 2);
        Buddies[playerId] = b;
    }

    /// <summary>A lightning zap from a point to an enemy (Chain Lightning, Lightning Storm).</summary>
    private static void Zap(Tank source, int pid, Vector2 from, Tank target) {
        Bolts.Add((from, target.Position, Now));
        SoundPlayer.PlaySoundInstance("Assets/sounds/bullet_ricochet.ogg", SoundContext.Effect, 0.8f, pitchOverride: 0.8f);
        target.Damage(new TankHurtContextOther(source, TankHurtContextOther.HurtContext.FromIngame, "Lightning"), true);
        // with Explosive Shells every zap blows up too
        if (P(pid).Has(TroveItemId.ExplosiveShells))
            new Explosion(target.Position, 3f * BlastSize(pid), source, 0.3f, 0.5f) { SparesOwner = true };
    }

    /// <summary>Wall Builder: 3 cork blocks across the way this player's turret points, 2 blocks out.</summary>
    public static void BuildWall(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        var aim = Vector2.UnitY.Rotate(tank.TurretRotation);
        aim = new Vector2(-aim.X, aim.Y);
        var side = new Vector2(-aim.Y, aim.X);
        Vector2 origin = new Coordinates.BlockMapPosition(0, 0);
        int half = Has(playerId, Syn("Great Wall")) ? 2 : 1;
        for (int i = -half; i <= half; i++) {
            var at = tank.Position + aim * Block.SIDE_LENGTH * 2f + side * Block.SIDE_LENGTH * i;
            // snap to the block grid
            var gx = (int)MathF.Round((at.X - origin.X) / Block.SIDE_LENGTH);
            var gy = (int)MathF.Round((at.Y - origin.Y) / Block.SIDE_LENGTH);
            if (gx < 0 || gy < 0 || gx >= Coordinates.BlockMapPosition.MAP_WIDTH_169 || gy >= Coordinates.BlockMapPosition.MAP_HEIGHT)
                continue;
            Vector2 pos = new Coordinates.BlockMapPosition(gx, gy);
            if (Block.AllBlocks.Any(b => b is not null && Vector2.Distance(b.Position, pos) < 4f))
                continue;
            if (GameHandler.AllTanks.Any(t => t is { IsDestroyed: false } && Vector2.Distance(t.Position, pos) < Block.SIDE_LENGTH))
                continue;
            new Block(BlockID.Cork, 1, pos);
        }
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 1f, pitchOverride: -0.3f);
    }

    /// <param name="packed">The tank's WorldId * 4 + the player who converted it.</param>
    public static void ConvertTank(int packed) {
        int worldId = packed / 4, playerId = packed % 4;
        if (worldId < 0 || worldId >= GameHandler.AllTanks.Length || GameHandler.AllTanks[worldId] is not AITank { IsDestroyed: false } t)
            return;
        t.Team = TeamID.Red;
        if (Has(playerId, Syn("Recruiter")))
            t.Properties.Immortal = true;
        GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, 10, 0), new Color(230, 140, 230), 1.4f);
        SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.4f, pitchOverride: 0.3f);
    }

    /// <summary>Shrink Ray: every enemy tiny and slow for 6 seconds.</summary>
    public static void Shrink() {
        RestoreShrunk();
        foreach (var t in GameHandler.AllAITanks)
            if (t is { IsDestroyed: false } && IsEnemy(t)) {
                _shrunk.Add((t, t.Scaling, t.Properties.MaxSpeed));
                t.Scaling *= 0.55f;
                t.Properties.MaxSpeed *= 0.5f;
            }
        _shrinkUntil = Now + 6;
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 1f, pitchOverride: 0.9f);
    }
    private static void RestoreShrunk() {
        foreach (var (t, sc, sp) in _shrunk)
            if (!t.IsDestroyed) {
                t.Scaling = sc;
                t.Properties.MaxSpeed = sp;
            }
        _shrunk.Clear();
        _shrinkUntil = -1;
    }

    /// <summary>A tank on the players' side that can't be destroyed.</summary>
    private static AITank SpawnAlly(int tier, Vector2 at, float scale) {
        var t = new TankTemplate { IsPlayer = false, AiTier = tier, Position = at, Rotation = 0, Team = TeamID.Red }.GetAiTank();
        t.Team = TeamID.Red;
        t.Scaling = new Vector3(scale);
        t.Properties.Immortal = true;
        t.Properties.InvulnerableToMines = true;
        return t;
    }

    // ------------------------------------------------------------------ shells and mines

    /// <summary>Blasts (shell explosions, slider bombs, the Nuke) are 1.5x with Big Mines.</summary>
    public static float BlastSize(int playerId) => playerId is >= 0 and < 4 && Players[playerId].Has(TroveItemId.BigMines) ? 1.5f : 1f;
    public static bool BlastProof(int playerId) => Has(playerId, Syn("Blast Proof"));

    public static HashSet<Shell> MyShells(Tank tank) => Shell.AllShells.Where(s => s is not null && ReferenceEquals(s.Owner, tank)).ToHashSet();

    /// <summary>After a player shoots: give every new shell their items (and send the extra Triple Shot shells to the other phones).</summary>
    public static void AfterShot(PlayerTank tank, HashSet<Shell> before) {
        bool sendExtras = Client.IsConnected() && NetPlay.IsClientMatched(tank.PlayerId);
        bool echo = !_echoing && IsLocal(tank.PlayerId) && P(tank.PlayerId).Has(TroveItemId.EchoShot);
        var shots = new List<(Vector2, int, uint)>();
        foreach (var s in Shell.AllShells) {
            if (s is null || !ReferenceEquals(s.Owner, tank) || before.Contains(s))
                continue;
            if (echo)
                shots.Add((s.Velocity, s.Type, s.RicochetsRemaining));
            ModifyShell(tank.PlayerId, s);
            if (sendExtras && !ReferenceEquals(s, tank.LastShotShell))
                Client.SyncShellFire(s);
        }
        if (shots.Count > 0)
            _echoes.Add((tank.PlayerId, Now + 0.3, shots));
        // Clone: the same shots, mirrored, from the clone
        if (!_echoing && IsLocal(tank.PlayerId) && P(tank.PlayerId).CloneTank is { IsDestroyed: false } clone) {
            _echoing = true;
            foreach (var s in Shell.AllShells.ToArray()) {
                if (s is null || !ReferenceEquals(s.Owner, tank) || before.Contains(s) || s.LifeTime > 1)
                    continue;
                var vel = new Vector2(-s.Velocity.X, s.Velocity.Y);
                var dir = vel.LengthSquared() > 0 ? Vector2.Normalize(vel) : Vector2.UnitY;
                var m = s.CloneItems(vel, s.RicochetsRemaining);
                m.Position = clone.Position + dir * 20f;
                m.VisualScale = s.VisualScale;
                if (Client.IsConnected())
                    Client.SyncShellFire(m);
            }
            _echoing = false;
        }
    }

    /// <summary>Echo Shot: the same shots again, from where the tank is now.</summary>
    private static void FireEcho(int playerId, List<(Vector2 Vel, int Type, uint Ricochets)> shots) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        bool chamber = Has(playerId, Syn("Echo Chamber"));
        _echoing = true;
        foreach (var (vel, type, ric) in shots) {
            var dir = vel.LengthSquared() > 0 ? Vector2.Normalize(vel) : Vector2.UnitY;
            var s = new Shell(tank.Position + dir * 20f, vel, type, tank, ric + (chamber ? 1u : 0u), tank.Properties.ShellHoming);
            ModifyShell(playerId, s);
            s.VisualScale *= 0.85f;
            if (Client.IsConnected())
                Client.SyncShellFire(s);
        }
        _echoing = false;
    }

    /// <summary>All of a player's shell items on one shell (they all combine).</summary>
    public static void ModifyShell(int playerId, Shell s) {
        if (!TroveRun.Active || playerId is < 0 or >= 4)
            return;
        var p = Players[playerId];
        if (p.Has(TroveItemId.HeavyShells)) {
            s.Pierce += p.Count(TroveItemId.HeavyShells);
            s.BreaksCork = true;
            s.VisualScale = 1.5f;
            s.HitRadius = 10f;
            s.Velocity *= 0.8f;
        }
        if (p.Has(TroveItemId.ExplosiveShells)) {
            s.ExplodeScale = 3.5f * BlastSize(playerId) * (p.Has(Syn("Fireworks")) ? 1.4f : 1f);
            s.SparesOwner = true;
            s.ExplodeOnPierce = p.Has(Syn("Wrecking Ball"));
        }
        if (p.Has(TroveItemId.ShellShield))
            s.ShellHitRadius = 16f;
        if (p.Has(Syn("Railgun")))
            s.Pierce++;
        if (s.Type == ShellID.Missile && p.Has(Syn("Bunker Buster")))
            s.Pierce += 2;
        if (p.Has(TroveItemId.SplitShot)) {
            s.SplitsOnBounce = true;
            s.Shrapnel = p.Has(Syn("Shrapnel"));
        }
        if (p.Has(TroveItemId.GhostShells))
            s.WallPasses = p.Has(Syn("Phase Rifle")) ? 2 : 1;
        if (p.Has(TroveItemId.RicochetMaster))
            s.GrowsOnBounce = true;
    }

    public static void AfterMine(PlayerTank tank, Mine m) {
        if (!TroveRun.Active || tank.PlayerId is < 0 or >= 4)
            return;
        var p = Players[tank.PlayerId];
        if (p.Has(TroveItemId.BigMines)) {
            m.ExplosionRadius *= 2f;
            m.MineScale = 1.5f;
        }
        m.SparesOwner = BlastProof(tank.PlayerId);
        if (p.Has(TroveItemId.Detonator) && !Client.IsConnected())
            m.DetonateTime *= 2.5f; // waits for you to set it off
    }

    /// <summary>Detonator: while this player has mines out, the mine button blows them all up. True if it did.</summary>
    public static bool TryDetonate(PlayerTank tank) {
        if (!TroveRun.Active || tank.PlayerId is < 0 or >= 4 || !Players[tank.PlayerId].Has(TroveItemId.Detonator))
            return false;
        var mines = Mine.AllMines.Where(m => m is not null && !m.Detonated && ReferenceEquals(m.Owner, tank)).ToArray();
        // slider bombs on their way count too: set them off where they are
        var bombs = Shell.AllShells.Where(sh => sh is not null && sh.IsSliderBomb && ReferenceEquals(sh.Owner, tank) && sh.LifeTime > 10).ToArray();
        if (mines.Length == 0 && bombs.Length == 0)
            return false;
        foreach (var m in mines)
            m!.Detonate();
        foreach (var b in bombs)
            b!.Destroy(Shell.DestructionContext.WithObstacle);
        return true;
    }
    public static bool HasDetonator(PlayerTank tank) =>
        TroveRun.Active && tank.PlayerId is >= 0 and < 4 && Players[tank.PlayerId].Has(TroveItemId.Detonator);

    /// <summary>A slider bomb was fired: Smart Bomb makes it steer, and Explosive/Heavy shell items apply.</summary>
    public static void AfterSliderBomb(PlayerTank tank, Shell bomb) {
        if (!TroveRun.Active || tank.PlayerId is < 0 or >= 4)
            return;
        if (Has(tank.PlayerId, Syn("Smart Bomb")))
            bomb.Properties.HomeProperties = new() { Radius = 300f, Speed = bomb.Velocity.Length(), Power = 0.08f * bomb.Velocity.Length() };
    }

    /// <summary>A slider bomb instead of a mine, if this player has any. True if one was used.</summary>
    public static bool TryUseSliderBomb(PlayerTank tank) {
        if (!TroveRun.Active || tank.PlayerId is < 0 or >= 4 || Players[tank.PlayerId].SliderBombs <= 0)
            return false;
        Players[tank.PlayerId].SliderBombs--;
        return true;
    }

    // ------------------------------------------------------------------ effects on the tank

    /// <summary>Puts this player's items onto their tank (call when it spawns, and when items change).</summary>
    public static void ApplyTo(PlayerTank tank) {
        if (!TroveRun.Active || tank.PlayerId is < 0 or >= 4)
            return;
        var p = Players[tank.PlayerId];
        // start from the normal player tank, then add the items
        tank.Properties.Armor?.Remove();
        tank.ApplyDefaults(ref tank.Properties);
        var props = tank.Properties;
        props.ShellShootCount = 1;
        props.ShellSpread = 0f;
        // size: Tiny Tank and Giant Tank (both: back to normal)
        bool tiny = p.Has(TroveItemId.TinyTank), giant = p.Has(TroveItemId.GiantTank);
        float size = tiny && giant ? 1f : tiny ? 0.7f : giant ? 1.35f : 1f;
        tank.Scaling = new Vector3(size);
        props.MaxSpeed *= tiny && !giant ? (p.Has(Syn("Zippy")) ? 1.15f : 0.9f) : giant && !tiny ? 0.85f : 1f;
        int sniper = p.Count(TroveItemId.SniperShells);
        props.ShellSpeed *= MathF.Pow(1.6f, Math.Min(sniper, 2));
        props.RicochetCount += (uint)sniper;
        props.MaxSpeed *= MathF.Pow(1.2f, p.Count(TroveItemId.SpeedTreads)) * (p.Has(Syn("Phantom")) ? 1.15f : 1f);
        props.RicochetCount += (uint)(p.Count(TroveItemId.BouncyShells) + (p.Has(Syn("Ricochet Storm")) ? 1 : 0));
        props.ShellLimit += p.Count(TroveItemId.ExtraShell) + (p.Has(Syn("Machine Gun")) ? 2 : 0);
        props.ShellCooldown = (uint)Math.Max(1, MathF.Round(props.ShellCooldown * MathF.Pow(0.65f, p.Count(TroveItemId.QuickReload))));
        props.MineLimit += (uint)(p.Count(TroveItemId.MinePack) + (p.Has(Syn("Minefield")) ? 1 : 0));
        if (p.Has(TroveItemId.TripleShot)) {
            int n = 1 + 2 * p.Count(TroveItemId.TripleShot);
            props.ShellShootCount = n;
            props.ShellSpread = p.Has(Syn("Ricochet Storm")) ? 0.32f : 0.2f;
            props.ShellLimit *= n; // a volley still counts as one shot
        }
        if (p.HomingOn)
            props.ShellHoming = p.Has(Syn("Seeker Swarm"))
                ? new() { Radius = 340f, Speed = props.ShellSpeed, Power = 0.14f * props.ShellSpeed }
                : new() { Radius = 200f, Speed = props.ShellSpeed, Power = 0.1f * props.ShellSpeed };
        if (p.LastStandOn) {
            props.ShellCooldown = (uint)Math.Max(1, props.ShellCooldown / 2);
            props.ShellLimit += 1;
        }
        int kidArmor = props.Armor?.HitPoints ?? 0; // Kid Mode's own armor (player 2)
        if (p.Shields > 0)
            props.Armor = new TankArmor(tank, kidArmor + p.Shields);
        _shieldBase[tank.PlayerId] = kidArmor;
        bool inv = Now < p.InvincibleUntil;
        props.Immortal = inv;
        props.InvulnerableToMines = inv;
        // Ghost Treads: breakable blocks (collision group 2) don't stop this tank
        tank.Physics?.SetCollidesWith(p.Has(TroveItemId.GhostTreads) ? Category.All & ~Category.Cat2 : Category.All);
    }

    private static readonly int[] _shieldBase = new int[4];

    /// <summary>Where a player's orbiters are right now (world positions), and how big they are.</summary>
    public static IEnumerable<(Vector2 Pos, float Radius)> Orbiters(int playerId) {
        var p = P(playerId);
        int n = p.Count(TroveItemId.OrbitGuard);
        if (n == 0 || GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            yield break;
        bool bulwark = p.Has(Syn("Bulwark"));
        if (bulwark) n++;
        float r = bulwark ? 13f : 9f;
        for (int i = 0; i < n; i++) {
            float a = (float)Now * 3f + i * MathHelper.TwoPi / n;
            yield return (tank.Position + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 32f, r);
        }
    }

    /// <summary>Every update: shields used up by hits, invincibility, orbiters, the decoy.</summary>
    public static void Update() {
        if (!TroveRun.Active)
            return;
        for (int i = 0; i < 4; i++) {
            var t = GameHandler.AllPlayerTanks[i];
            if (t is null)
                continue;
            var p = Players[i];
            if (IsLocal(i)) {
                if (t.IsDestroyed)
                    p.Shields = 0;
                else if (p.Shields > 0)
                    p.Shields = Math.Max(0, (t.Properties.Armor?.HitPoints ?? 0) - _shieldBase[i]);
            }
            if (t.IsDestroyed || !CampaignGlobals.InMission)
                continue;

            // invincibility: can't be hurt; drive into enemies to destroy them
            bool inv = Now < p.InvincibleUntil;
            if (inv) {
                t.Properties.Immortal = true;
                t.Properties.InvulnerableToMines = true;
                if ((int)(Now * 20) % 2 == 0)
                    GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, 10, 0), Color.Gold, 0.6f);
                if (IsLocal(i))
                    foreach (var ai in GameHandler.AllAITanks)
                        if (ai is { IsDestroyed: false } && IsEnemy(ai) && Vector2.Distance(ai.Position, t.Position) < (Has(i, Syn("Juggernaut")) ? 34f : 24f))
                            ai.Damage(new TankHurtContextOther(t, TankHurtContextOther.HurtContext.FromIngame, "Rammed"), true);
            }
            else if (p.WasInvincible) {
                t.Properties.Immortal = false;
                t.Properties.InvulnerableToMines = false;
            }
            p.WasInvincible = inv;

            if (IsLocal(i)) {
                // Battering Ram: a dash hits whatever it runs into (once each)
                if (Now < p.DashUntil && Has(i, Syn("Battering Ram")))
                    foreach (var ai in GameHandler.AllAITanks)
                        if (ai is { IsDestroyed: false } && IsEnemy(ai) && !p.Rammed.Contains(ai) && Vector2.Distance(ai.Position, t.Position) < 30f) {
                            p.Rammed.Add(ai);
                            ai.Damage(new TankHurtContextOther(t, TankHurtContextOther.HurtContext.FromIngame, "Rammed"), true);
                        }
                // Stomp: shrunken enemies get crushed
                if (_shrinkUntil > Now && Has(i, Syn("Stomp")))
                    foreach (var (st, _, _) in _shrunk)
                        if (!st.IsDestroyed && IsEnemy(st) && Vector2.Distance(st.Position, t.Position) < 26f)
                            st.Damage(new TankHurtContextOther(t, TankHurtContextOther.HurtContext.FromIngame, "Stomped"), true);
                // Tesla Coil: the orbiters zap the nearest enemy every 1.5 seconds
                if (Has(i, Syn("Tesla Coil")) && Now >= p.TeslaAt) {
                    var orbs = Orbiters(i).ToArray();
                    var near = GameHandler.AllAITanks.Where(a => a is { IsDestroyed: false } && IsEnemy(a) && Vector2.Distance(a.Position, t.Position) < 150f)
                        .OrderBy(a => Vector2.Distance(a!.Position, t.Position)).FirstOrDefault();
                    if (near is not null && orbs.Length > 0) {
                        Zap(t, i, orbs[0].Pos, near);
                        p.TeslaAt = Now + 1.5;
                    }
                }
            }

            if (IsLocal(i)) {
                // Last Stand: no shields and nobody else left
                bool last = p.Has(TroveItemId.LastStand) && p.Shields == 0
                    && !GameHandler.AllPlayerTanks.Any(o => o is { IsDestroyed: false } && !ReferenceEquals(o, t));
                if (last != p.LastStandOn) {
                    p.LastStandOn = last;
                    ApplyTo(t);
                }
                // Magnet Shells: steer toward the nearest enemy shell
                if (p.Has(TroveItemId.MagnetShells)) {
                    bool icpt = p.Has(Syn("Interceptor"));
                    float range = icpt ? 140f : 90f, pull = icpt ? 0.12f : 0.07f;
                    foreach (var mine in Shell.AllShells) {
                        if (mine is null || !ReferenceEquals(mine.Owner, t))
                            continue;
                        var target = Shell.AllShells.Where(e => e is not null && e.Owner is not null && IsEnemy(e.Owner) && Vector2.Distance(e.Position, mine.Position) < range)
                            .OrderBy(e => Vector2.Distance(e!.Position, mine.Position)).FirstOrDefault();
                        if (target is null)
                            continue;
                        float speed = mine.Velocity.Length();
                        var want = Vector2.Normalize(target.Position - mine.Position) * speed;
                        mine.Velocity = Vector2.Normalize(Vector2.Lerp(mine.Velocity, want, pull * TanksRebirth.RuntimeData.DeltaTime)) * speed;
                    }
                }
                // Afterburner: drop fire while moving
                if (p.Has(TroveItemId.Afterburner) && t.Velocity.LengthSquared() > 0.2f && Now - p.LastFireDrop > 0.12) {
                    p.LastFireDrop = Now;
                    Fires.Add((t.Position, Now + (p.Has(Syn("Fire Walker")) ? 3.5 : 2.0), i));
                }
                // Ricochet Shield: enemy shells that have bounced break near you
                if (p.Has(TroveItemId.RicochetShield)) {
                    float r = p.Has(Syn("Deflector")) ? 120f : 60f;
                    foreach (var s in Shell.AllShells.ToArray())
                        if (s is not null && s.Ricochets > 0 && s.Owner is not null && IsEnemy(s.Owner) && Vector2.Distance(s.Position, t.Position) < r)
                            s.Destroy(Shell.DestructionContext.WithShell);
                }
            }
            if (Now < p.MirrorUntil && (int)(Now * 12) % 2 == 0)
                GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, 10, 0), Color.LightBlue, 0.5f);
            if (p.CloneTank is { IsDestroyed: false } ct && Now > p.CloneUntil) {
                ct.Remove(true);
                p.CloneTank = null;
            }

            // orbit guard: knock enemy shells out of the air (this phone's players)
            if (IsLocal(i) && p.Has(TroveItemId.OrbitGuard))
                foreach (var (pos, r) in Orbiters(i).ToArray())
                    foreach (var s in Shell.AllShells)
                        if (s is not null && s.Owner is not null && IsEnemy(s.Owner) && Vector2.Distance(s.Position, pos) < r + 5f)
                            s.Destroy(Shell.DestructionContext.WithShell);
        }
        if (CampaignGlobals.InMission) {
            // afterburner fire burns enemies driving through it (once a second each); the burning player's phone decides
            Fires.RemoveAll(f => Now > f.Until);
            foreach (var (at, until, pl) in Fires) {
                if ((int)(Now * 10) % 3 == 0)
                    GameHandler.Particles.MakeShineSpot(new Vector3(at.X, 2, at.Y), new Color(255, 140, 40), 0.35f);
                if (!IsLocal(pl) || GameHandler.AllPlayerTanks[pl] is not { } burner)
                    continue;
                float reach = P(pl).Has(Syn("Fire Walker")) ? 20f : 14f;
                foreach (var e in GameHandler.AllAITanks)
                    if (e is { IsDestroyed: false } && IsEnemy(e) && Vector2.Distance(e.Position, at) < reach
                        && (!_burnedAt.TryGetValue(e, out var last) || Now - last > 1)) {
                        _burnedAt[e] = Now;
                        e.Damage(new TankHurtContextOther(burner, TankHurtContextOther.HurtContext.FromIngame, "Burned"), true);
                        if (Has(pl, Syn("Inferno")))
                            new Explosion(e.Position, 3f * BlastSize(pl), burner, 0.4f) { SparesOwner = true };
                    }
            }
            for (int i = _echoes.Count - 1; i >= 0; i--)
                if (Now >= _echoes[i].Due) {
                    var e = _echoes[i];
                    _echoes.RemoveAt(i);
                    FireEcho(e.Player, e.Shots);
                }
            for (int i = _airstrikes.Count - 1; i >= 0; i--)
                if (Now >= _airstrikes[i].Due) {
                    var a = _airstrikes[i];
                    _airstrikes.RemoveAt(i);
                    var owner = GameHandler.AllPlayerTanks[a.Player];
                    new Explosion(a.At, 6f * BlastSize(a.Player), owner, 0.5f) { SparesOwner = true };
                    if (owner is not null && Has(a.Player, Syn("Saturation")))
                        ClusterBurst(owner, a.At, true);
                    SendAny(TroveRun.EV_BLAST, EncodePos(a.At) * 4 + a.Player);
                }
            // homing mines crawl toward the nearest enemy
            foreach (var m in Mine.AllMines) {
                if (m is null || m.Detonated || m.Owner is not PlayerTank mp || !P(mp.PlayerId).Has(TroveItemId.HomingMines))
                    continue;
                var target = GameHandler.AllAITanks.Where(t => t is { IsDestroyed: false } && IsEnemy(t)).OrderBy(t => Vector2.Distance(t!.Position, m.Position)).FirstOrDefault();
                if (target is null)
                    continue;
                var to = target.Position - m.Position;
                float d = to.Length();
                if (d > 1f)
                    m.Position += to / d * Math.Min(d, 0.6f * TanksRebirth.RuntimeData.DeltaTime);
                if (d < 22f && Has(mp.PlayerId, Syn("Guided Missile")) && IsLocal(mp.PlayerId))
                    m.Detonate();
            }
        }
        else {
            _echoes.Clear();
            _airstrikes.Clear();
        }
        if (_shrinkUntil > 0 && Now > _shrinkUntil)
            RestoreShrunk();
        Bolts.RemoveAll(b => Now - b.Time > 0.35);
        if (DecoyTank is not null && Now > _decoyUntil) {
            if (Has(_decoyOwner, Syn("Booby Trap")) && !DecoyTank.IsDestroyed)
                new Explosion(DecoyTank.Position, 9f * BlastSize(_decoyOwner), GameHandler.AllPlayerTanks[_decoyOwner], 0.5f) { SparesOwner = true };
            if (!DecoyTank.IsDestroyed)
                DecoyTank.Remove(true);
            DecoyTank = null;
        }
    }

    /// <summary>A world position packed into an int for the network (and back).</summary>
    public static int EncodePos(Vector2 p) => (int)(Math.Clamp(p.X + 400, 0, 799)) * 1000 + (int)Math.Clamp(p.Y + 400, 0, 799);
    public static Vector2 DecodePos(int v) => new(v / 1000 - 400, v % 1000 - 400);

    /// <summary>Another phone's airstrike explosion (packed position * 4 + player).</summary>
    public static void RemoteBlast(int a) {
        int player = a % 4;
        var owner = GameHandler.AllPlayerTanks[player];
        new Explosion(DecodePos(a / 4), 6f * BlastSize(player), owner, 0.5f) { SparesOwner = true };
        if (owner is not null && Has(player, Syn("Saturation")))
            ClusterBurst(owner, DecodePos(a / 4), true);
    }

    private static string ShortName(TroveItemId id) => id switch {
        TroveItemId.Rocket => "ROCKETS",
        TroveItemId.Homing => "HOMING",
        TroveItemId.SliderBomb => "BOMBS",
        TroveItemId.BlockBust => "BUST",
        TroveItemId.Stopwatch => "STOP",
        TroveItemId.ShellBurst => "BURST",
        TroveItemId.Nuke => "NUKE",
        TroveItemId.Decoy => "DECOY",
        TroveItemId.Invincibility => "STAR",
        TroveItemId.Reroll => "REROLL",
        TroveItemId.Airstrike => "AIR",
        TroveItemId.WallBuilder => "WALL",
        TroveItemId.Dash => "DASH",
        TroveItemId.MineStorm => "MINES",
        TroveItemId.Convert => "CONVERT",
        TroveItemId.ShrinkRay => "SHRINK",
        TroveItemId.RepairKit => "REPAIR",
        TroveItemId.LightningStorm => "ZAP",
        TroveItemId.Clone => "CLONE",
        TroveItemId.EMP => "EMP",
        TroveItemId.Teleport => "WARP",
        TroveItemId.TurretDrop => "TURRET",
        TroveItemId.SupplyDrop => "SUPPLY",
        TroveItemId.MirrorShield => "MIRROR",
        _ => Def(id).Name.ToUpperInvariant(),
    };

    /// <summary>What the on-screen ITEM button shows for this player, or null.</summary>
    public static (string Title, string Sub, bool Usable)? ButtonFor(int playerId) {
        if (!TroveRun.Active || playerId is < 0 or >= 4)
            return null;
        var p = Players[playerId];
        if (p.Active is { } a)
            return p.ActiveReady ? (ShortName(a), "tap to use", true)
                : Def(a).RechargeSeconds > 0 ? (ShortName(a), $"{Math.Ceiling(p.ReadyAt - Now)}s", false)
                : (ShortName(a), $"charging {p.Charge}/{Def(a).Recharge}", false);
        if (MissileUpgrade.ShotsLeft(playerId) > 0)
            return ("ROCKETS", $"{MissileUpgrade.ShotsLeft(playerId)} shots", false);
        if (p.SliderBombs > 0)
            return ("BOMBS", $"{p.SliderBombs} left", false);
        if (p.HomingOn)
            return ("HOMING", "this floor", false);
        return null;
    }
}
