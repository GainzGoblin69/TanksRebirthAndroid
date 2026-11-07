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
    BountyHunter, HotPotato, Wingman, Momentum,
    GravityWell, SmokeBomb, RallyFlag, Shockwave, RocketBarrage, FreezeBomb,
    NitroTreads, RapidBarrel, ArmorPlating, HighCaliber, Steroids, LongBarrel, HeartyMeal, MagicMushroom,
    GlassCannon, HeavyArmor, Featherweight,
    // batch 6 (Isaac-inspired)
    BoomerangShells, MortarShells, Wraparound, MegaShell, HolyMantle, Pyromaniac, MineDrone, LuckyFoot,
    BoxOfFriends, OilSlick, TeleportPads, Battery, MysteryPill, CupidsArrow, SacredHeart,
    ChargeShot,
    // batch 7: speed and fire rate
    Turbocharger, HairTrigger, Adrenaline, OverdriveEngine, NitroBoost, GatlingBarrel, LightweightAlloy, CoffeeThermos,
    // found in cork: a refillable boost bar
    Boost,
}

/// <summary>How an item works: used with the ITEM button, takes effect when picked up, or lasts the whole run.</summary>
public enum TroveItemKind { Active, Instant, Passive }

/// <param name="Recharge">Active items: how slow it is to recharge (0 = one use), as "rooms" - each is <see cref="TroveItems.SECONDS_PER_RECHARGE"/> seconds of play.</param>
/// <param name="RechargeSeconds">Active items with their own (short) recharge time.</param>
public sealed record TroveItemDef(TroveItemId Id, string Name, string Description, TroveItemKind Kind, Color Color, int Recharge = 0, float RechargeSeconds = 0) {
    /// <summary>ITEM-button items wear out: how many uses before it breaks (slower recharge = fewer uses).</summary>
    /// <summary>Seconds of play before it can be used again.</summary>
    public float CooldownSeconds => RechargeSeconds > 0 ? RechargeSeconds : Recharge * TroveItems.SECONDS_PER_RECHARGE;
    public int MaxUses => Kind != TroveItemKind.Active ? 0 : RechargeSeconds > 0 ? 15 : Recharge switch { 0 => 1, 1 => 4, 2 => 3, _ => 2 };
}

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
        new(TroveItemId.BountyHunter, "Bounty Hunter", "Clear a room without getting hit and it drops something extra", TroveItemKind.Passive, new Color(200, 160, 60)),
        new(TroveItemId.HotPotato, "Hot Potato", "Enemy shells set your mines off at once, with a bigger blast", TroveItemKind.Passive, new Color(230, 110, 40)),
        new(TroveItemId.Wingman, "Wingman", "A little guard that shoots enemy shells out of the air near you", TroveItemKind.Passive, new Color(140, 200, 240)),
        new(TroveItemId.Momentum, "Momentum", "Shells fired while driving fast go through an enemy", TroveItemKind.Passive, new Color(240, 150, 90)),
        new(TroveItemId.GravityWell, "Gravity Well", "A vortex in front of you pulls enemies and their shells in for 4 seconds", TroveItemKind.Active, new Color(110, 70, 180), Recharge: 3),
        new(TroveItemId.SmokeBomb, "Smoke Bomb", "Enemies lose track of you for 5 seconds", TroveItemKind.Active, new Color(150, 150, 160), Recharge: 2),
        new(TroveItemId.RallyFlag, "Rally Flag", "Plant a flag: you and your allies near it shoot twice as fast for 8 seconds", TroveItemKind.Active, new Color(220, 60, 60), Recharge: 3),
        new(TroveItemId.Shockwave, "Shockwave", "Pushes nearby enemies and their shells away from you", TroveItemKind.Active, new Color(150, 220, 255), Recharge: 1),
        new(TroveItemId.RocketBarrage, "Rocket Barrage", "Fires 6 rockets one after another at the nearest enemies", TroveItemKind.Active, new Color(230, 90, 40), Recharge: 2),
        // stat upgrades (they stack, and colour your shells)
        new(TroveItemId.NitroTreads, "Nitro Treads", "Speed up", TroveItemKind.Passive, new Color(250, 215, 40)),
        new(TroveItemId.RapidBarrel, "Rapid Barrel", "Fire rate up, +1 shell on screen", TroveItemKind.Passive, new Color(60, 130, 255)),
        new(TroveItemId.ArmorPlating, "Armor Plating", "Health up: +1 hit you can take, refilled every room", TroveItemKind.Passive, new Color(70, 200, 90)),
        new(TroveItemId.HighCaliber, "High Caliber", "Damage up: armored enemies and bosses lose 2 hits per shot", TroveItemKind.Passive, new Color(230, 40, 40)),
        new(TroveItemId.Steroids, "Steroids", "Damage up and speed up", TroveItemKind.Passive, new Color(255, 140, 30)),
        new(TroveItemId.LongBarrel, "Long Barrel", "Shell speed up, +1 bounce", TroveItemKind.Passive, new Color(245, 245, 250)),
        new(TroveItemId.HeartyMeal, "Hearty Meal", "Health up: +2 hits, refilled every floor; speed down", TroveItemKind.Passive, new Color(150, 90, 50)),
        new(TroveItemId.MagicMushroom, "Magic Mushroom", "All stats up: speed, fire rate, health and damage", TroveItemKind.Passive, new Color(230, 60, 60)),
        new(TroveItemId.GlassCannon, "Glass Cannon", "Double damage, but you can't keep shields", TroveItemKind.Passive, new Color(200, 240, 255)),
        new(TroveItemId.HeavyArmor, "Heavy Armor", "Health up: +3 hits refilled every room; much slower", TroveItemKind.Passive, new Color(110, 115, 125)),
        new(TroveItemId.Featherweight, "Featherweight", "Much faster, but -1 life", TroveItemKind.Passive, new Color(170, 230, 230)),
        new(TroveItemId.FreezeBomb, "Freeze Bomb", "Drops a bomb that freezes enemies near it for 4 seconds", TroveItemKind.Active, new Color(150, 220, 250), Recharge: 2),
        new(TroveItemId.BoomerangShells, "Boomerang Shells", "Your shells fly out, then come back to you, hitting enemies both ways", TroveItemKind.Passive, new Color(205, 150, 60)),
        new(TroveItemId.MortarShells, "Mortar Shells", "Shells arc over walls and explode where they land", TroveItemKind.Passive, new Color(125, 115, 90)),
        new(TroveItemId.Wraparound, "Wraparound", "Shells leave one side of the room and come back in the other", TroveItemKind.Passive, new Color(80, 200, 175)),
        new(TroveItemId.MegaShell, "Mega Shell", "One huge, slow shell at a time that goes through everything and explodes", TroveItemKind.Passive, new Color(185, 55, 55)),
        new(TroveItemId.HolyMantle, "Holy Mantle", "A free shield at the start of every room", TroveItemKind.Passive, new Color(235, 235, 255)),
        new(TroveItemId.Pyromaniac, "Pyromaniac", "Explosions give you a shield instead of hurting you", TroveItemKind.Passive, new Color(255, 120, 40)),
        new(TroveItemId.MineDrone, "Mine Drone", "A drone follows you and drops a mine behind you every few seconds", TroveItemKind.Passive, new Color(110, 120, 135)),
        new(TroveItemId.LuckyFoot, "Lucky Foot", "Cork and cleared rooms drop items more often", TroveItemKind.Passive, new Color(80, 190, 90)),
        new(TroveItemId.BoxOfFriends, "Box of Friends", "Two buddy tanks join you for the rest of the room", TroveItemKind.Active, new Color(230, 170, 90), Recharge: 3),
        new(TroveItemId.OilSlick, "Oil Slick", "Spills oil ahead: enemies in it slide around helplessly for 5 seconds", TroveItemKind.Active, new Color(45, 45, 60), Recharge: 1),
        new(TroveItemId.TeleportPads, "Teleport Pads", "Drops two linked teleporters you and your shells can use this room", TroveItemKind.Active, new Color(150, 90, 230), Recharge: 2),
        new(TroveItemId.Battery, "Battery", "Recharges your ITEM right away", TroveItemKind.Instant, new Color(240, 215, 60)),
        new(TroveItemId.MysteryPill, "Mystery Pill", "A random stat change for this floor (usually good)", TroveItemKind.Instant, new Color(225, 225, 240)),
        new(TroveItemId.CupidsArrow, "Cupid's Arrow", "Your shells go through every enemy they hit", TroveItemKind.Passive, new Color(255, 120, 170)),
        new(TroveItemId.SacredHeart, "Sacred Heart", "Homing shells and damage up, fire rate a little down", TroveItemKind.Passive, new Color(225, 40, 60)),
        new(TroveItemId.Turbocharger, "Turbocharger", "Speed up; keep driving and you keep getting faster (up to +50%)", TroveItemKind.Passive, new Color(255, 150, 40)),
        new(TroveItemId.HairTrigger, "Hair Trigger", "Fire rate up and +1 shell on screen; smaller shells", TroveItemKind.Passive, new Color(255, 90, 90)),
        new(TroveItemId.Adrenaline, "Adrenaline", "Faster driving and shooting for every hit you've taken (lives lost, armor gone)", TroveItemKind.Passive, new Color(230, 40, 110)),
        new(TroveItemId.OverdriveEngine, "Overdrive Engine", "+25% speed and fire rate, but no mines; you throw sparks", TroveItemKind.Passive, new Color(255, 205, 60)),
        new(TroveItemId.NitroBoost, "Nitro Boost", "Speed up, and every few seconds a burst of double speed", TroveItemKind.Passive, new Color(80, 170, 255)),
        new(TroveItemId.GatlingBarrel, "Gatling Barrel", "Hold fire to keep shooting; the longer you hold, the faster it fires (+2 shells on screen)", TroveItemKind.Passive, new Color(175, 180, 195)),
        new(TroveItemId.LightweightAlloy, "Lightweight Alloy", "Faster, turns quicker, and drifts around corners", TroveItemKind.Passive, new Color(170, 245, 240)),
        new(TroveItemId.CoffeeThermos, "Coffee Thermos", "Much faster driving and shooting for the first 10 seconds of every room", TroveItemKind.Passive, new Color(160, 105, 60)),
        new(TroveItemId.ChargeShot, "Charge Shot", "Hold fire for 2 seconds, then let go: a super-fast shot that goes through enemies (let go early to cancel)", TroveItemKind.Passive, new Color(120, 200, 255)),
        new(TroveItemId.Boost, "Boost", "Hold BOOST (right trigger on a controller) to drive twice as fast until the bar runs out", TroveItemKind.Instant, new Color(255, 140, 40)),
    ];
    // looked up by id (the list above isn't in the enum's order)
    private static readonly TroveItemDef[] _byId = BuildIndex();
    private static TroveItemDef[] BuildIndex() {
        var arr = new TroveItemDef[Enum.GetValues<TroveItemId>().Length];
        foreach (var d in All)
            arr[(int)d.Id] = d;
        for (int i = 0; i < arr.Length; i++)
            arr[i] ??= new((TroveItemId)i, ((TroveItemId)i).ToString(), "", TroveItemKind.Passive, Color.Gray);
        return arr;
    }
    public static TroveItemDef Def(TroveItemId id) => _byId[(int)id];

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
        new("Jackpot", "Bounty drops come from the item-room pool", TroveItemId.BountyHunter, TroveItemId.TreasureMap),
        new("Powder Keg", "Hot Potato blasts are twice as big", TroveItemId.HotPotato, TroveItemId.BigMines),
        new("Air Support", "Your wingman shoots twice as often", TroveItemId.Wingman, TroveItemId.BuddyTank),
        new("Freight Train", "Momentum shells go through 2 more enemies", TroveItemId.Momentum, TroveItemId.SpeedTreads),
        new("Singularity", "The gravity well collapses in a big explosion", TroveItemId.GravityWell, TroveItemId.ExplosiveShells),
        new("Ninja", "Smoke lasts 8 seconds and you're faster in it", TroveItemId.SmokeBomb, TroveItemId.GhostTreads),
        new("War Banner", "The flag reaches twice as far and lasts 12 seconds", TroveItemId.RallyFlag, TroveItemId.BuddyTank),
        new("Sonic Boom", "Shockwave also damages the enemies it pushes", TroveItemId.Shockwave, TroveItemId.HeavyShells),
        new("Heat Seekers", "Barrage rockets home in", TroveItemId.RocketBarrage, TroveItemId.Homing),
        new("Cryo Cluster", "Freeze bombs freeze twice as far", TroveItemId.FreezeBomb, TroveItemId.ClusterMines),
        new("Tank Mode", "+2 more health", TroveItemId.HeavyArmor, TroveItemId.GiantTank),
        new("Speed Demon", "Your shells go faster too", TroveItemId.Featherweight, TroveItemId.NitroTreads),
        new("Lucky Break", "Second Wind works every room", TroveItemId.GlassCannon, TroveItemId.SecondWind),
        new("Big Guns", "Heavy shells go through one more enemy", TroveItemId.HighCaliber, TroveItemId.HeavyShells),
        new("Overclock", "+2 more shells on screen", TroveItemId.RapidBarrel, TroveItemId.QuickReload),
        new("Full Belly", "Hearty Meal refills every room", TroveItemId.HeartyMeal, TroveItemId.RegenPlating),
        new("Lawnmower", "Boomerang shells go through everything on the way back", TroveItemId.BoomerangShells, TroveItemId.HeavyShells),
        new("Artillery", "Mortar blasts are much bigger", TroveItemId.MortarShells, TroveItemId.BigMines),
        new("Infinite Loop", "Shells wrap around 2 more times", TroveItemId.Wraparound, TroveItemId.BouncyShells),
        new("Death Star", "The mega shell's explosion is huge", TroveItemId.MegaShell, TroveItemId.ExplosiveShells),
        new("Guardian", "Holy Mantle tops you up to 2 shields", TroveItemId.HolyMantle, TroveItemId.OrbitGuard),
        new("Phoenix", "Second Wind sets off a blast around you (and Pyromaniac makes it a shield)", TroveItemId.Pyromaniac, TroveItemId.SecondWind),
        new("Minesweeper", "Your drone drops mines twice as often", TroveItemId.MineDrone, TroveItemId.HomingMines),
        new("Four-Leaf Clover", "Even luckier: drops come three times as often", TroveItemId.LuckyFoot, TroveItemId.TreasureMap),
        new("Party", "Box of Friends brings 3 friends", TroveItemId.BoxOfFriends, TroveItemId.BuddyTank),
        new("Oil Fire", "The oil catches fire and burns enemies in it", TroveItemId.OilSlick, TroveItemId.Afterburner),
        new("Heartseeker", "Sacred Heart shells seek from much further away", TroveItemId.SacredHeart, TroveItemId.CupidsArrow),
        new("Mortar Boomerang", "Mortar shells come back for a second blast", TroveItemId.MortarShells, TroveItemId.BoomerangShells),
    ];

    // item rooms draw from nearly everything; challenge and secret rooms have stronger pools; rooms mostly drop small stuff
    public static readonly TroveItemId[] PedestalPool = [
        TroveItemId.ChargeShot, TroveItemId.Turbocharger, TroveItemId.HairTrigger, TroveItemId.Adrenaline, TroveItemId.OverdriveEngine,
        TroveItemId.NitroBoost, TroveItemId.GatlingBarrel, TroveItemId.LightweightAlloy, TroveItemId.CoffeeThermos,
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
        TroveItemId.SupplyDrop, TroveItemId.MirrorShield, TroveItemId.BountyHunter, TroveItemId.HotPotato, TroveItemId.Wingman,
        TroveItemId.Momentum, TroveItemId.GravityWell, TroveItemId.SmokeBomb, TroveItemId.RallyFlag, TroveItemId.Shockwave,
        TroveItemId.RocketBarrage, TroveItemId.FreezeBomb,
        TroveItemId.NitroTreads, TroveItemId.NitroTreads, TroveItemId.RapidBarrel, TroveItemId.RapidBarrel, TroveItemId.ArmorPlating,
        TroveItemId.ArmorPlating, TroveItemId.HighCaliber, TroveItemId.HighCaliber, TroveItemId.Steroids, TroveItemId.LongBarrel,
        TroveItemId.HeartyMeal, TroveItemId.GlassCannon, TroveItemId.HeavyArmor, TroveItemId.Featherweight,
        TroveItemId.BoomerangShells, TroveItemId.MortarShells, TroveItemId.Wraparound, TroveItemId.MegaShell, TroveItemId.HolyMantle,
        TroveItemId.Pyromaniac, TroveItemId.MineDrone, TroveItemId.LuckyFoot, TroveItemId.BoxOfFriends, TroveItemId.OilSlick,
        TroveItemId.TeleportPads, TroveItemId.CupidsArrow, TroveItemId.SacredHeart,
    ];
    public static readonly TroveItemId[] ChallengePool = [
        TroveItemId.TripleShot, TroveItemId.QuickReload, TroveItemId.HeavyShells, TroveItemId.ExplosiveShells, TroveItemId.BigMines,
        TroveItemId.OrbitGuard, TroveItemId.ShellBurst, TroveItemId.Stopwatch, TroveItemId.BossHunter, TroveItemId.Invincibility, TroveItemId.Nuke,
        TroveItemId.ChainLightning, TroveItemId.EchoShot, TroveItemId.Airstrike, TroveItemId.Convert, TroveItemId.SplitShot, TroveItemId.RicochetMaster,
        TroveItemId.Clone, TroveItemId.MirrorShield, TroveItemId.TurretDrop, TroveItemId.SecondWind,
        TroveItemId.RocketBarrage, TroveItemId.GravityWell, TroveItemId.Wingman,
        TroveItemId.Adrenaline, TroveItemId.OverdriveEngine, TroveItemId.GatlingBarrel,
        TroveItemId.MegaShell, TroveItemId.BoomerangShells, TroveItemId.BoxOfFriends, TroveItemId.Pyromaniac,
    ];
    public static readonly TroveItemId[] SecretPool = [
        TroveItemId.Nuke, TroveItemId.Invincibility, TroveItemId.BuddyTank, TroveItemId.TripleShot, TroveItemId.OrbitGuard,
        TroveItemId.BossHunter, TroveItemId.TreasureMap, TroveItemId.ExplosiveShells, TroveItemId.HeavyShells,
        TroveItemId.Airstrike, TroveItemId.Convert, TroveItemId.EchoShot, TroveItemId.VampireTreads,
        TroveItemId.SecondWind, TroveItemId.Clone, TroveItemId.GhostShells, TroveItemId.BountyHunter, TroveItemId.Wingman,
        TroveItemId.MagicMushroom, TroveItemId.MagicMushroom,
        TroveItemId.SacredHeart, TroveItemId.HolyMantle, TroveItemId.CupidsArrow,
    ];
    /// <summary>Items that stack: every extra one adds more.</summary>
    public static bool Stacks(TroveItemId id) => id is TroveItemId.BouncyShells or TroveItemId.ExtraShell or TroveItemId.MinePack
        or TroveItemId.SpeedTreads or TroveItemId.QuickReload or TroveItemId.OrbitGuard or TroveItemId.HeavyShells or TroveItemId.TripleShot
        or TroveItemId.SniperShells or TroveItemId.RegenPlating or TroveItemId.VampireTreads
        or (>= TroveItemId.NitroTreads and <= TroveItemId.Featherweight);
    // cleared rooms mostly leave hearts and shields, sometimes a stacking item or a Block Bust
    /// <summary>Battle: items that work against other players (no enemy-only tricks), found on the map and in cork.</summary>
    public static readonly TroveItemId[] BattlePool = [
        TroveItemId.Shield, TroveItemId.Shield, TroveItemId.SpeedTreads, TroveItemId.BouncyShells, TroveItemId.ExtraShell, TroveItemId.MinePack,
        TroveItemId.BigMines, TroveItemId.TripleShot, TroveItemId.QuickReload, TroveItemId.HeavyShells, TroveItemId.ExplosiveShells,
        TroveItemId.GhostTreads, TroveItemId.SplitShot, TroveItemId.SniperShells, TroveItemId.TinyTank, TroveItemId.RicochetMaster,
        TroveItemId.GiantTank, TroveItemId.EchoShot, TroveItemId.GhostShells, TroveItemId.NitroTreads, TroveItemId.RapidBarrel,
        TroveItemId.ArmorPlating, TroveItemId.LongBarrel, TroveItemId.Steroids, TroveItemId.MagicMushroom,
        TroveItemId.BoomerangShells, TroveItemId.MortarShells, TroveItemId.Wraparound, TroveItemId.MegaShell, TroveItemId.HolyMantle,
        TroveItemId.Pyromaniac, TroveItemId.CupidsArrow, TroveItemId.Battery, TroveItemId.MysteryPill, TroveItemId.Boost, TroveItemId.Boost,
        TroveItemId.Rocket, TroveItemId.Dash, TroveItemId.Teleport, TroveItemId.WallBuilder, TroveItemId.Invincibility,
        TroveItemId.RepairKit, TroveItemId.TeleportPads, TroveItemId.ChargeShot,
        TroveItemId.Turbocharger, TroveItemId.HairTrigger, TroveItemId.Adrenaline, TroveItemId.OverdriveEngine,
        TroveItemId.NitroBoost, TroveItemId.GatlingBarrel, TroveItemId.LightweightAlloy, TroveItemId.CoffeeThermos,
    ];
    public static readonly TroveItemId[] DropPool = [
        TroveItemId.ExtraLife, TroveItemId.ExtraLife, TroveItemId.Shield, TroveItemId.Shield, TroveItemId.Shield,
        TroveItemId.BouncyShells, TroveItemId.ExtraShell, TroveItemId.MinePack, TroveItemId.BlockBust, TroveItemId.RepairKit,
        TroveItemId.Battery, TroveItemId.MysteryPill, TroveItemId.MysteryPill,
    ];

    /// <summary>How many ITEM-button items one player can carry (switch between them with the swap button or X).</summary>
    public const int MAX_ACTIVES = 3;

    public sealed class PlayerItems {
        /// <summary>The ITEM-button items being carried, and which one the button uses.</summary>
        public readonly List<TroveItemId> Actives = [];
        public int Selected;
        /// <summary>When each carried item can be used again (play time, see <see cref="TroveRun.PlayTime"/>).</summary>
        public readonly Dictionary<TroveItemId, double> ReadyTimes = [];
        /// <summary>When the selection last changed (the description shows up bigger for a moment).</summary>
        public double SelectedAt = -10;
        public TroveItemId? Active => Actives.Count > 0 ? Actives[Math.Clamp(Selected, 0, Actives.Count - 1)] : null;
        public double ReadyTimeOf(TroveItemId id) => ReadyTimes.TryGetValue(id, out var t) ? t : -100;
        public bool IsReady(TroveItemId id) => TroveRun.PlayTime >= ReadyTimeOf(id);
        /// <summary>Seconds until it's ready (0 = ready).</summary>
        public double SecondsLeft(TroveItemId id) => Math.Max(0, ReadyTimeOf(id) - TroveRun.PlayTime);
        /// <summary>How far along its recharge is (1 = ready).</summary>
        public float RechargeFraction(TroveItemId id) {
            if (IsReady(id))
                return 1f;
            float total = CooldownFor(this, id);
            return total <= 0 ? 1f : MathHelper.Clamp(1f - (float)(SecondsLeft(id) / total), 0f, 1f);
        }
        /// <summary>It came back within the last few seconds (it blinks so you notice).</summary>
        public bool JustReady(TroveItemId id) {
            double since = TroveRun.PlayTime - ReadyTimeOf(id);
            return ReadyTimes.ContainsKey(id) && ReadyTimeOf(id) > 0 && since >= 0 && since < READY_BLINK_SECONDS;
        }
        /// <summary>Picks up an ITEM-button item: a free slot if there is one, otherwise it replaces the selected one.</summary>
        public void AddActive(TroveItemId id) {
            ReadyTimes[id] = -100; // comes charged
            UsesLeft[id] = Def(id).MaxUses; // and new (picking up one you carry repairs it)
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
            ReadyTimes.Remove(a);
            UsesLeft.Remove(a);
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
        // speed and fire-rate items that change from moment to moment: the stats ApplyTo worked out, and what feeds the changes
        public float BaseSpeed, DriveTime, GatlingHold;
        public uint BaseCooldown;
        public bool Dynamic;
        /// <summary>Boost (found in cork): how much is left and how big the bar is (0 = no boost), and whether it's on right now.</summary>
        public float BoostFuel, BoostMax;
        public bool Boosting;
        public int RoomsForRegen, KillsForVampire;
        public double DashUntil = -1, TeslaAt;
        public double MirrorUntil = -1, CloneUntil = -1, LastFireDrop;
        public bool SecondWindUsed, LastStandOn;
        public AITank? CloneTank;
        public AITank? WingmanTank;
        public double WingmanShotAt, SmokeUntil = -1;
        public bool HitThisRoom, Rallied;
        /// <summary>Mystery Pill: the effect for this floor (-1 none).</summary>
        public int Pill = -1;
        public AITank? DroneTank;
        public double DroneAt;
        public readonly List<Mine> DroneMines = [];
        /// <summary>Health from stat items: refilled each room / each floor (lost after shields).</summary>
        public int RoomHp, FloorHp;
        /// <summary>Battle: the extra hits from the match's hit points setting (refilled every round).</summary>
        public int BattleHp;
        public int RoomHpMax => Count(TroveItemId.ArmorPlating) + 3 * Count(TroveItemId.HeavyArmor) + Count(TroveItemId.MagicMushroom)
            + (Has(TroveItemId.HeavyArmor) && Has(TroveItemId.GiantTank) ? 2 : 0) + (Has(TroveItemId.HeartyMeal) && Has(TroveItemId.RegenPlating) ? 2 * Count(TroveItemId.HeartyMeal) : 0);
        public int FloorHpMax => Has(TroveItemId.HeartyMeal) && Has(TroveItemId.RegenPlating) ? 0 : 2 * Count(TroveItemId.HeartyMeal);
        /// <summary>The colour your shells are (the last stat item you picked up).</summary>
        public TroveItemId? ShellColorFrom;
        /// <summary>Uses left on each carried ITEM-button item (it breaks at 0).</summary>
        public readonly Dictionary<TroveItemId, int> UsesLeft = [];
        public int UsesOf(TroveItemId id) => UsesLeft.TryGetValue(id, out var u) ? u : Def(id).MaxUses;
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
    /// <summary>An ITEM-button item that just broke (its last use), for a little banner.</summary>
    public static (TroveItemDef Def, double Time)? LastBroken;
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
    /// <summary>Gravity wells: where, until when, whose.</summary>
    public static readonly List<(Vector2 At, double Until, int Player)> Wells = [];
    /// <summary>Rally flags: where, until when, how far they reach, whose.</summary>
    public static readonly List<(Vector2 At, double Until, float Reach, int Player)> Flags = [];
    /// <summary>Freeze bombs waiting to go off: where, when they go off anyway, whose.</summary>
    public static readonly List<(Vector2 At, double Fuse, int Player)> FreezeBombs = [];
    /// <summary>Enemies frozen by a freeze bomb, and until when.</summary>
    public static readonly Dictionary<Tank, double> IcedUntil = [];
    /// <summary>Shockwave rings to draw: where, when.</summary>
    public static readonly List<(Vector2 At, double Time)> Waves = [];
    private static readonly List<(double At, int Player)> _barrage = [];
    /// <summary>Box of Friends tanks and whose they are (for the rest of the room).</summary>
    public static readonly List<(AITank Tank, int Player)> Friends = [];
    /// <summary>Oil slicks: where, until when, whose.</summary>
    public static readonly List<(Vector2 At, double Until, int Player)> Oils = [];
    private static readonly Dictionary<Tank, Vector2> _oilSlide = [];
    public const float OIL_RADIUS = 55f;

    private static bool _hooked;
    private static PlayerItems P(int id) => Players[Math.Clamp(id, 0, 3)];
    public static bool Has(int playerId, TroveSynergy s) => playerId is >= 0 and < 4 && Players[playerId].Has(s);
    private static TroveSynergy Syn(string name) => Synergies.First(s => s.Name == name);
    private static double Now => TroveRun.PlayTime;
    public static bool Frozen => TroveRun.Active && Now < FrozenUntil;

    /// <summary>A new Tank Troves run: everyone starts with a Shield and a full Boost bar.</summary>
    public static void StartingKit() {
        foreach (var p in Players) {
            p.Shields = 1;
            p.BoostMax = 1f;
            p.BoostFuel = 1f;
        }
    }

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
        Wells.Clear();
        Flags.Clear();
        FreezeBombs.Clear();
        IcedUntil.Clear();
        Waves.Clear();
        _barrage.Clear();
        Friends.Clear();
        Oils.Clear();
        _oilSlide.Clear();
        EmpUntil = -1;
        InstallHooks();
    }

    public static void NewFloor() {
        foreach (var p in Players) {
            p.HomingOn = false;
            p.Pill = -1; // Mystery Pill lasts a floor
        }
    }

    /// <summary>Players whose items this phone looks after.</summary>
    public static bool IsLocal(int playerId) =>
        LocalCoop.Active ? playerId < LocalCoop.PlayerCount : !Client.IsConnected() ? playerId == 0 : NetPlay.IsClientMatched(playerId);

    private static bool IsEnemy(Tank t) => t.Team != TeamID.Red;

    /// <summary>The speed and fire-rate items that change from moment to moment (Turbocharger, Adrenaline, Nitro Boost,
    /// Coffee Thermos, Gatling Barrel), on top of what ApplyTo set.</summary>
    private static void SpeedItems(Tank t, PlayerItems p) {
        if ((!p.Dynamic && p.BoostMax <= 0) || p.BaseSpeed <= 0)
            return;
        float dt = TanksRebirth.RuntimeData.DeltaTime / 60f;
        float speed = 1f, fire = 1f;
        // Boost: twice as fast while it's held (on top of every other speed item), draining the bar
        bool wasBoosting = p.Boosting;
        p.Boosting = p.BoostFuel > 0 && BoostInput(p.Id) && !TroveRun.InReady && !TanksRebirth.GameContent.UI.GameUI.Paused
            && t.Velocity.Length() > p.BaseSpeed * 0.15f;
        if (p.Boosting) {
            speed *= 2f;
            p.BoostFuel = Math.Max(0f, p.BoostFuel - dt / BOOST_SECONDS);
            if (!wasBoosting)
                SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_rocket.ogg", SoundContext.Effect, 0.35f, pitchOverride: -0.3f);
            if (p.BoostFuel <= 0)
                SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.6f, pitchOverride: -0.6f);
        }
        if (p.Has(TroveItemId.Turbocharger)) {
            bool driving = t.Velocity.Length() > p.BaseSpeed * 0.3f;
            p.DriveTime = driving ? p.DriveTime + dt : Math.Max(0, p.DriveTime - dt * 4);
            speed *= 1f + 0.5f * Math.Min(1f, p.DriveTime / 3f);
        }
        if (p.Has(TroveItemId.Adrenaline)) {
            int hurt = Math.Max(0, PlayerTank.StartingLives - PlayerTank.Lives[p.Id]) + Math.Max(0, p.RoomHpMax - p.RoomHp) + Math.Max(0, p.FloorHpMax - p.FloorHp);
            float k = 1f + 0.12f * Math.Min(4, hurt);
            speed *= k;
            fire *= k;
        }
        if (p.Has(TroveItemId.NitroBoost) && Now % 6.0 < 1.5)
            speed *= 2f;
        if (p.Has(TroveItemId.CoffeeThermos) && TroveRun.RoomTime < 10f && !TroveRun.InReady)
        {
            speed *= 1.4f;
            fire *= 1.4f;
        }
        if (p.Has(TroveItemId.GatlingBarrel) && !p.Has(TroveItemId.ChargeShot))
            fire *= 1f + 1.8f * Math.Min(1f, p.GatlingHold / 2f);
        t.Properties.MaxSpeed = p.BaseSpeed * speed;
        t.Properties.ShellCooldown = (uint)Math.Max(1, MathF.Round(p.BaseCooldown / fire));
    }

    /// <summary>Active items recharge over time: this many seconds of play for each step of their recharge
    /// (Shell Burst 1 step = 20s, Stopwatch 2 = 40s, Airstrike 3 = 60s, Convert 4 = 80s).</summary>
    public const float SECONDS_PER_RECHARGE = 20f;
    /// <summary>How long an item blinks after it's ready again.</summary>
    public const float READY_BLINK_SECONDS = 3f;

    /// <summary>This player's recharge time for an item (Fortune Teller halves Reroll's).</summary>
    public static float CooldownFor(PlayerItems p, TroveItemId id) {
        float sec = Def(id).CooldownSeconds;
        if (id == TroveItemId.Reroll && p.Has(Syn("Fortune Teller")))
            sec /= 2f;
        return sec;
    }

    /// <summary>The ITEM button's selected item just recharged (the button blinks).</summary>
    public static bool SelectedJustReady(int playerId) =>
        TroveRun.Active && playerId is >= 0 and < 4 && Players[playerId].Active is { } a && Players[playerId].JustReady(a);

    // which of this phone's items were recharging last frame (a tick when one comes back)
    private static readonly HashSet<TroveItemId> _recharging = [];
    private static void ReadyChimes() {
        var p = Players[MyPlayer];
        foreach (var id in p.Actives) {
            if (!p.IsReady(id))
                _recharging.Add(id);
            else if (_recharging.Remove(id)) {
                SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.9f, pitchOverride: 0.4f);
                TanksRebirth.Internals.Common.Framework.Input.Haptics.Tap();
            }
        }
        _recharging.RemoveWhere(id => !p.Actives.Contains(id));
    }

    /// <summary>A full Boost bar lasts this many seconds of boosting.</summary>
    public const float BOOST_SECONDS = 4f;

    /// <summary>This player has boost left (the right trigger boosts instead of shooting then).</summary>
    public static bool HasBoost(int playerId) => TroveRun.Active && playerId is >= 0 and < 4 && Players[playerId].BoostFuel > 0;

    /// <summary>Whether this player is holding boost: the BOOST button, the right trigger, or Shift.</summary>
    public static bool BoostInput(int playerId) {
        if (LocalCoop.Active)
            return LocalCoop.BoostHeld(playerId);
        var pad = TanksRebirth.Internals.Common.InputUtils.CurrentGamePadSnapshot;
        return TanksRebirth.Internals.Common.Framework.Input.TouchControls.BoostHeld || pad.Triggers.Right > 0.3f
            || TanksRebirth.Internals.Common.InputUtils.CurrentKeySnapshot.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.LeftShift);
    }

    /// <summary>Little effects behind tanks with some items: Nitro flames, Overdrive sparks, Coffee steam.</summary>
    private static void ItemTrails(Tank t, PlayerItems p) {
        float dt = TanksRebirth.RuntimeData.DeltaTime;
        if (t.Velocity.LengthSquared() < 0.05f)
            return;
        var back = t.Position3D - new Vector3(t.Velocity.X, 0, t.Velocity.Y) * 6f + new Vector3(0, 6, 0);
        if (p.Has(TroveItemId.NitroBoost) && Now % 6.0 < 1.5 && Random.Shared.NextDouble() < 0.8 * dt)
            GameHandler.Particles.MakeShineSpot(back, Random.Shared.Next(2) == 0 ? new Color(80, 170, 255) : new Color(255, 160, 60), 0.5f);
        if (p.Boosting) {
            // boost: a stream of flame and sparks behind the tank
            for (int k = 0; k < 2; k++)
                if (Random.Shared.NextDouble() < 0.9 * dt)
                    GameHandler.Particles.MakeShineSpot(back + new Vector3(Random.Shared.NextSingle() * 4 - 2, 0, Random.Shared.NextSingle() * 4 - 2),
                        Random.Shared.Next(3) == 0 ? new Color(255, 230, 120) : new Color(255, 120, 30), 0.55f);
        }
        if (p.Has(TroveItemId.OverdriveEngine) && Random.Shared.NextDouble() < 0.25 * dt)
            GameHandler.Particles.MakeShineSpot(back, new Color(255, 220, 90), 0.35f);
        if (p.Has(TroveItemId.CoffeeThermos) && TroveRun.RoomTime < 10f && Random.Shared.NextDouble() < 0.15 * dt)
            GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, 16, 0), new Color(235, 230, 220), 0.4f);
    }

    /// <summary>Gatling Barrel: holding fire keeps shooting (called by the player's tank while fire is held).</summary>
    public static bool HasGatling(int playerId) => TroveRun.Active && playerId is >= 0 and < 4 && Players[playerId].Has(TroveItemId.GatlingBarrel) && !Players[playerId].Has(TroveItemId.ChargeShot);
    public static void GatlingHeld(int playerId, bool held) {
        if (playerId is < 0 or >= 4)
            return;
        var p = Players[playerId];
        p.GatlingHold = held ? p.GatlingHold + TanksRebirth.RuntimeData.DeltaTime / 60f : 0f;
    }

    /// <summary>Lightweight Alloy: the tank keeps some of its momentum, so it drifts around corners.</summary>
    private static readonly Dictionary<Tank, Vector2> _drift = [];
    private static Vector2 DriftFilter(Tank t, Vector2 v) {
        if (t is not PlayerTank pt || pt.PlayerId is < 0 or > 3 || !Players[pt.PlayerId].Has(TroveItemId.LightweightAlloy))
            return v;
        var last = _drift.TryGetValue(t, out var l) ? l : v;
        var now = Vector2.Lerp(last, v, Math.Min(1f, 0.16f * TanksRebirth.RuntimeData.DeltaTime));
        _drift[t] = now;
        return now;
    }

    /// <summary>How dark this floor is (0 = normal; the darkest floors are about 0.4).</summary>
    public static float SceneDarkness() => !TroveRun.Active || !CampaignGlobals.InMission ? 0f : TroveThemes.Current switch {
        FloorTheme.Haunted => 0.35f, FloorTheme.Crystal => 0.4f, FloorTheme.Factory => 0.3f, FloorTheme.Swamp => 0.12f, FloorTheme.Volcano => 0.15f,
        FloorTheme.NeonCity => 0.4f, FloorTheme.Carnival => 0.42f, FloorTheme.ToxicLab => 0.3f, FloorTheme.SpaceStation => 0.35f, _ => 0f,
    };

    /// <summary>A soft white glow under your tank on the darkest floors (when no speed item gives it a color).</summary>
    public static readonly Color DarkGlow = new(150, 150, 160);

    // ---- underglow: a neon glow under tanks carrying speed items (movement or fire rate)
    private static readonly HashSet<TroveItemId> _glowItems = [
        TroveItemId.Turbocharger, TroveItemId.HairTrigger, TroveItemId.Adrenaline, TroveItemId.OverdriveEngine, TroveItemId.NitroBoost,
        TroveItemId.GatlingBarrel, TroveItemId.LightweightAlloy, TroveItemId.CoffeeThermos,
        TroveItemId.SpeedTreads, TroveItemId.NitroTreads, TroveItemId.Steroids, TroveItemId.Featherweight, TroveItemId.GhostTreads,
    ];
    private static bool Glows(TroveItemId id) => _glowItems.Contains(id);

    /// <summary>The colour of a player's underglow, or null for none (the newest item that gives one).</summary>
    public static Color? UnderglowColor(int playerId) {
        if (!TroveRun.Active || playerId is < 0 or > 3)
            return null;
        var p = Players[playerId];
        TroveItemId? best = null;
        foreach (var id in p.Passives.Keys.Concat(p.Actives))
            if (Glows(id) && (best is null || (int)id > (int)best.Value))
                best = id;
        if (best is { } b)
            return Def(b).Color;
        return SceneDarkness() >= 0.35f ? DarkGlow : null;
    }

    private static readonly ParticleSystem.Particle?[] _glow = new ParticleSystem.Particle?[4];
    private static Microsoft.Xna.Framework.Graphics.Texture2D? _glowTex;

    /// <summary>Keeps each player's underglow under their tank (a flat glowing shape on the floor).</summary>
    private static void UpdateUnderglow() {
        for (int i = 0; i < 4; i++) {
            var t = GameHandler.AllPlayerTanks[i];
            var col = t is { IsDestroyed: false } && CampaignGlobals.InMission ? UnderglowColor(i) : null;
            if (col is null) {
                if (_glow[i] is { } old) { old.Destroy(); _glow[i] = null; }
                continue;
            }
            var tex = GlowTexture();
            if (tex is null)
                continue;
            if (_glow[i] is null || !GameHandler.Particles.CurrentParticles.Contains(_glow[i])) {
                var g = GameHandler.Particles.MakeParticle(t!.Position3D + new Vector3(0, 0.25f, 0), tex);
                g.HasAdditiveBlending = true;
                g.Pitch = MathHelper.PiOver2;
                _glow[i] = g;
            }
            var gp = _glow[i]!;
            gp.Position = t!.Position3D + new Vector3(0, 0.25f, 0);
            gp.Yaw = -t.ChassisRotation;
            gp.Scale = new Vector3(1.1f * t.Scaling.X);
            gp.Color = col.Value;
            gp.Alpha = col.Value == DarkGlow ? 0.45f : 0.75f + 0.2f * MathF.Sin((float)Now * 3f + i);
            gp.LifeTime = 0;
        }
    }

    /// <summary>Underglow lights for the dynamic lighting.</summary>
    public static void AddUnderglowLights() {
        for (int i = 0; i < 4; i++)
            if (GameHandler.AllPlayerTanks[i] is { IsDestroyed: false } t && UnderglowColor(i) is { } c)
                Graphics.DynamicLighting.GameplayLights.AddGlow(t.Position3D + new Vector3(0, 2.5f, 0), c,
                    c == DarkGlow ? 0.6f : 1.3f, c == DarkGlow ? 45f : 60f);
    }

    private static Microsoft.Xna.Framework.Graphics.Texture2D? GlowTexture() {
        if (_glowTex is { IsDisposed: false })
            return _glowTex;
        var gd = TankGame.Instance?.GraphicsDevice;
        if (gd is null)
            return null;
        const int N = 64;
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++) {
                // a soft rounded rectangle, a bit longer than wide (the shape of a tank)
                float dx = Math.Max(0, Math.Abs(x + 0.5f - N / 2f) - 9f) / 13f, dy = Math.Max(0, Math.Abs(y + 0.5f - N / 2f) - 13f) / 13f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float a = Math.Clamp(1f - d, 0f, 1f);
                a *= a;
                px[y * N + x] = new Color(255, 255, 255, (int)(a * 255));
            }
        _glowTex = new Microsoft.Xna.Framework.Graphics.Texture2D(gd, N, N);
        _glowTex.SetData(px);
        return _glowTex;
    }

    /// <summary>A tank's headlight colour from its items (null = the normal warm white).</summary>
    public static Color? HeadlightColor(Tank t) {
        if (!TroveRun.Active)
            return null;
        if (TroveBosses.IsBoss(t))
            return TroveBosses.HeadlightColor(t);
        if (t is not PlayerTank pt || pt.PlayerId is < 0 or > 3)
            return null;
        var p = Players[pt.PlayerId];
        float now = (float)Now;
        if (Now < p.InvincibleUntil) // a rainbow while invincible
            return new Color(0.5f + 0.5f * MathF.Sin(now * 5f), 0.5f + 0.5f * MathF.Sin(now * 5f + 2.1f), 0.5f + 0.5f * MathF.Sin(now * 5f + 4.2f));
        if (Now < p.MirrorUntil) return new Color(200, 235, 255);
        if (Now < p.SmokeUntil) return new Color(120, 120, 135);
        if (p.Has(TroveItemId.SacredHeart)) return new Color(255, 70, 90);
        if (p.Has(TroveItemId.MegaShell)) return new Color(255, 80, 50);
        if (p.Has(TroveItemId.GlassCannon)) return new Color(150, 235, 255);
        if (p.Has(TroveItemId.Pyromaniac)) return new Color(255, 150, 50);
        if (p.Has(TroveItemId.HolyMantle) && p.Shields > 0) return new Color(255, 245, 180);
        if (p.Has(TroveItemId.GhostShells) || p.Has(TroveItemId.GhostTreads)) return new Color(190, 170, 255);
        if (p.Has(TroveItemId.MagicMushroom)) return new Color(255, 130, 210);
        if (p.Has(TroveItemId.CupidsArrow)) return new Color(255, 150, 190);
        if (p.Has(TroveItemId.Wraparound)) return new Color(110, 240, 210);
        // Battle: your own colour
        if (TroveRun.Battle && BattleMode.Fighters.FirstOrDefault(f => f.PlayerId == pt.PlayerId) is { } fi)
            return Color.Lerp(Color.White, fi.Color, 0.55f);
        return null;
    }

    /// <summary>The ways Tank Troves reaches into the rest of the game (once).</summary>
    private static void InstallHooks() {
        if (_hooked)
            return;
        _hooked = true;
        Block.OnDestroy += TroveRun.OnBlockBroken; // items hidden in cork
        // headlights take on the colour of what you're carrying (and bosses get their own)
        Graphics.DynamicLighting.LightManager.HeadlightTint = HeadlightColor;
        Graphics.DynamicLighting.LightManager.ExtraHeadlightTanks = () => !TroveRun.Active || TroveBosses.Boss is not { } bt || bt.Properties.Invisible ? []
            : TroveBosses.Twin is { } tw ? [bt, tw] : [bt];
        Graphics.DynamicLighting.LightManager.ExtraLights = () => { TroveThemes.AddLights(); if (TroveRun.Active && CampaignGlobals.InMission) AddUnderglowLights(); };
        Graphics.DynamicLighting.LightManager.SceneDarkness = SceneDarkness;
        AITank.IsFrozen = t => Frozen && IsEnemy(t) || TroveRun.InReady || IcedUntil.TryGetValue(t, out var iu) && Now < iu;
        // Smoke Bomb: enemies can't see you
        AITank.IsHidden = t => TroveRun.Active && t is PlayerTank pt && pt.PlayerId is >= 0 and < 4
            && (Now < P(pt.PlayerId).SmokeUntil || TroveThemes.HiddenInBush(t));
        // floor themes: quicksand slows, ice slides
        Tank.VelocityFilter = (t, v) => TroveRun.Active ? DriftFilter(t, TroveBosses.SlowFilter(t, OilFilter(t, TroveThemes.FilterVelocity(t, v) ?? v))) : null;
        // Pyromaniac: explosions give you a shield instead of hurting you
        Explosion.Absorbs = (tank, _) => {
            if (!TroveRun.Active || tank is not PlayerTank pt || pt.PlayerId is < 0 or >= 4 || !P(pt.PlayerId).Has(TroveItemId.Pyromaniac))
                return false;
            if (IsLocal(pt.PlayerId)) {
                var pp = P(pt.PlayerId);
                if (pp.Shields < 5) pp.Shields++;
                ApplyTo(pt);
                GameHandler.Particles.MakeShineSpot(pt.Position3D + new Vector3(0, 12, 0), new Color(255, 150, 60), 1f);
            }
            return true;
        };
        // Bounty Hunter: getting hit spoils this room's bounty
        Tank.OnDamage += (victim, _, _) => {
            if (TroveRun.Active && victim is PlayerTank pt && pt.PlayerId is >= 0 and < 4)
                P(pt.PlayerId).HitThisRoom = true;
        };
        Tank.MovementLocked = _ => TroveRun.InReady;
        Shell.IsFrozen = s => Frozen && s.Owner is AITank ai && IsEnemy(ai);
        AITank.TargetOverride = ai => TroveRun.Active && DecoyTank is { IsDestroyed: false } d && IsEnemy(ai) ? d : null;
        Tank.ExtraArmorDamage = (tank, ctx) => {
            if (!TroveRun.Active || tank is PlayerTank || tank.Team == TeamID.Red)
                return 0;
            // whose hit: a player's, or one of their helpers'
            int pid = ctx.Source is PlayerTank pt ? pt.PlayerId : ctx.Source is AITank ai ? OwnerOf(ai) : -1;
            if (pid is < 0 or >= 4)
                return 0;
            var p = P(pid);
            bool boss = ReferenceEquals(tank, TroveRun.BossTank) || TroveBosses.IsBoss(tank);
            int extra = 0;
            if (boss && (ctx.Source is PlayerTank && p.Has(TroveItemId.BossHunter) || ctx.Source is AITank && Has(pid, Syn("Pack Hunter"))))
                extra++;
            // damage ups (stat items) count for the player's own hits
            if (ctx.Source is PlayerTank) {
                extra += p.Count(TroveItemId.HighCaliber) + p.Count(TroveItemId.Steroids) + p.Count(TroveItemId.MagicMushroom) + p.Count(TroveItemId.SacredHeart);
                if (p.Has(TroveItemId.GlassCannon))
                    extra = extra * 2 + 1;
            }
            return extra;
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
        // Mortar Boomerang: where a mortar shell lands, a second one is lobbed back toward you
        Shell.OnDestroy += (shell, _) => {
            if (!TroveRun.Active || !shell.Lobbed || shell.SecondLob || shell.Owner is not PlayerTank mp || !IsLocal(mp.PlayerId)
                || !Has(mp.PlayerId, Syn("Mortar Boomerang")) || mp.IsDestroyed)
                return;
            var back = mp.Position - shell.Position;
            if (back.LengthSquared() < 400f)
                return;
            float speed = MathF.Max(shell.Velocity.Length(), 2f);
            var r = new Shell(shell.Position, Vector2.Normalize(back) * speed, shell.Type, mp, 0, playSpawnSound: false);
            ModifyShell(mp.PlayerId, r);
            r.SecondLob = true;
            r.LobTime = MathF.Min(50f, back.Length() / (speed * 0.62f)); // lands near you (it can't hurt you)
            if (Client.IsConnected())
                Client.SyncShellFire(r);
        };
        // slider bombs are mines too: they cluster
        Shell.OnDestroy += (shell, _) => {
            if (TroveRun.Active && shell.IsSliderBomb && shell.Owner is PlayerTank pt && P(pt.PlayerId).Has(TroveItemId.ClusterMines))
                ClusterBurst(pt, shell.Position, shell.SparesOwner);
        };
        // bosses: Shield Commander's shield, and boss shells' special touches
        Shell.Blocks = TroveBosses.ShieldBlocks;
        Tank.OnShoot += TroveBosses.OnBossShoot;
        Tank.OnShoot += TroveEnemies.OnShoot;
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
            // Phoenix: rise from the flames
            if (p.Has(Syn("Phoenix")) && IsLocal(pt.PlayerId)) {
                new Explosion(pt.Position, 9f * BlastSize(pt.PlayerId), pt, 0.6f) { SparesOwner = !p.Has(TroveItemId.Pyromaniac) };
                SendAny(TroveRun.EV_BLAST, EncodePos(pt.Position) * 4 + pt.PlayerId);
            }
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
        if (local) TanksRebirth.Internals.Common.Framework.Input.Haptics.ItemPickup();
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
                else if (id == TroveItemId.Battery && p.Active is { } charged) {
                    // the carried ITEM is ready again
                    p.ReadyTimes[charged] = -100;
                }
                else if (id == TroveItemId.Boost) {
                    // a full bar; finding more makes the bar bigger (up to twice as long)
                    p.BoostMax = p.BoostMax <= 0 ? 1f : Math.Min(2f, p.BoostMax + 0.5f);
                    p.BoostFuel = p.BoostMax;
                }
                else if (id == TroveItemId.MysteryPill && local) {
                    // usually good: the last two effects are the bad ones (1 in 4)
                    int effect = Random.Shared.Next(4) == 0 ? 6 + Random.Shared.Next(2) : Random.Shared.Next(6);
                    if (effect == 4) p.Shields++;
                    else p.Pill = effect;
                    LastPickup = (def with { Description = PillText[effect] + " (this floor)" }, Now, 1);
                }
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
        if (id is >= TroveItemId.NitroTreads and <= TroveItemId.Featherweight or TroveItemId.CupidsArrow or TroveItemId.SacredHeart)
            p.ShellColorFrom = id;
        if (id == TroveItemId.MineDrone && CampaignGlobals.InMission)
            SpawnDrone(playerId);
        if (id is TroveItemId.ArmorPlating or TroveItemId.HeavyArmor or TroveItemId.MagicMushroom)
            p.RoomHp = p.RoomHpMax;
        if (id == TroveItemId.HeartyMeal)
            p.FloorHp = p.FloorHpMax;
        if (id == TroveItemId.Featherweight && local && PlayerTank.Lives[playerId] > 1) {
            PlayerTank.Lives[playerId]--;
            if (Client.IsConnected())
                Client.SendLives();
        }
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
    // the second screen's item panel asks for these; they happen on the next update
    private static int _reqSelect = -1;
    private static bool _reqUse;
    public static void RequestSelect(int index) => _reqSelect = index;
    public static void RequestUse() => _reqUse = true;
    /// <summary>This phone's player (the one the second screen shows).</summary>
    public static int MyPlayer => LocalCoop.Active ? 0 : Math.Clamp(NetPlay.GetMyClientId(), 0, 3);
    private static void HandleRequests() {
        if (_reqSelect >= 0) {
            SelectActive(MyPlayer, _reqSelect);
            _reqSelect = -1;
        }
        if (_reqUse) {
            _reqUse = false;
            if (CampaignGlobals.InMission && !TanksRebirth.GameContent.UI.GameUI.Paused && GameHandler.AllPlayerTanks[MyPlayer] is { IsDestroyed: false })
                UseActive(MyPlayer);
        }
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
        // Bounty Hunter (the host decides, and tells the others what dropped)
        if (!TroveRun.Battle && (!Client.IsConnected() || Client.IsHost()) && TroveRun.Room is { } br && !br.PickupVisible)
            for (int i = 0; i < 4; i++) {
                var bp = Players[i];
                if (!bp.Has(TroveItemId.BountyHunter) || bp.HitThisRoom || GameHandler.AllPlayerTanks[i] is not { IsDestroyed: false })
                    continue;
                var pool = bp.Has(Syn("Jackpot")) ? PedestalPool : DropPool;
                var drop = pool[Random.Shared.Next(pool.Length)];
                br.Pickup = null;
                TroveRun.SetPickup(drop);
                SendAny(TroveRun.EV_REROLL, (int)drop);
                break;
            }
        // Lucky Foot: a cleared room with nothing to pick up sometimes drops something anyway
        if (!TroveRun.Battle && (!Client.IsConnected() || Client.IsHost()) && Luck() > 1 && TroveRun.Room is { PickupVisible: false } lr && lr.Kind != TroveRoomKind.Start
            && Random.Shared.NextDouble() < (Luck() >= 3 ? 0.5 : 0.3)) {
            var drop = DropPool[Random.Shared.Next(DropPool.Length)];
            lr.Pickup = null;
            TroveRun.SetPickup(drop);
            SendAny(TroveRun.EV_REROLL, (int)drop);
        }
        for (int i = 0; i < 4; i++) {
            var p = Players[i];
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
            Players[i].FloorHp = Players[i].FloorHpMax;
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
        foreach (var (ft, pl) in Friends)
            if (ReferenceEquals(ft, t))
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
        if (IsLocal(playerId)) TanksRebirth.Internals.Common.Framework.Input.Haptics.ItemUse();
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
                        Client.SyncMinePlace(m.Position, m.DetonateTime, tank.WorldId, m.NetId);
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
            case TroveItemId.GravityWell: {
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                var aim = Vector2.UnitY.Rotate(tank.TurretRotation);
                var at = tank.Position + new Vector2(-aim.X, aim.Y) * 110f;
                at = new Vector2(Math.Clamp(at.X, Graphics.GameScene.TANKS_MIN_X + 20, Graphics.GameScene.TANKS_MAX_X - 20), Math.Clamp(at.Y, Graphics.GameScene.TANKS_MIN_Y + 20, Graphics.GameScene.TANKS_MAX_Y - 20));
                StartWell(at, playerId);
                SendAny(TroveRun.EV_WELL, EncodePos(at) * 4 + playerId);
                break;
            }
            case TroveItemId.SmokeBomb:
                StartSmoke(playerId);
                SendAny(TroveRun.EV_SMOKE, playerId);
                break;
            case TroveItemId.RallyFlag:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                PlantFlag(tank.Position, playerId);
                SendAny(TroveRun.EV_FLAG, EncodePos(tank.Position) * 4 + playerId);
                break;
            case TroveItemId.Shockwave:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                DoShockwave(playerId);
                SendAny(TroveRun.EV_SHOCK, playerId);
                break;
            case TroveItemId.RocketBarrage:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                for (int i = 0; i < 6; i++)
                    _barrage.Add((Now + i * 0.15, playerId));
                break;
            case TroveItemId.FreezeBomb:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                DropFreezeBomb(tank.Position, playerId);
                SendAny(TroveRun.EV_FREEZE_BOMB, EncodePos(tank.Position) * 4 + playerId);
                break;
            case TroveItemId.MirrorShield:
                StartMirror(playerId);
                SendAny(TroveRun.EV_MIRROR, playerId);
                break;
            case TroveItemId.BoxOfFriends:
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                SpawnFriends(playerId);
                SendAny(TroveRun.EV_FRIENDS, playerId);
                break;
            case TroveItemId.OilSlick: {
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                var aim = Vector2.UnitY.Rotate(tank.TurretRotation);
                var at = tank.Position + new Vector2(-aim.X, aim.Y) * 95f;
                at = new Vector2(Math.Clamp(at.X, Graphics.GameScene.TANKS_MIN_X + 20, Graphics.GameScene.TANKS_MAX_X - 20), Math.Clamp(at.Y, Graphics.GameScene.TANKS_MIN_Y + 20, Graphics.GameScene.TANKS_MAX_Y - 20));
                SpillOil(at, playerId);
                SendAny(TroveRun.EV_OIL, EncodePos(at) * 4 + playerId);
                break;
            }
            case TroveItemId.TeleportPads: {
                if (tank is null || tank.IsDestroyed) { SoundPlayer.SoundError(); return; }
                var far = FarPadSpot(tank);
                if (far is null) { SoundPlayer.SoundError(); return; }
                // one pad just behind you, the other far across the room
                var back = Vector2.UnitY.Rotate(tank.ChassisRotation);
                var near = tank.Position - back * Block.SIDE_LENGTH * 1.5f;
                PlacePad(near, playerId);
                PlacePad(far.Value, playerId);
                SendAny(TroveRun.EV_PAD, EncodePos(near) * 4 + playerId);
                SendAny(TroveRun.EV_PAD, EncodePos(far.Value) * 4 + playerId);
                break;
            }
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
        // used up (it breaks after its last use), or recharging
        int usesLeft = p.UsesOf(id) - 1;
        p.UsesLeft[id] = usesLeft;
        if (usesLeft <= 0) {
            p.RemoveActive();
            if (Def(id).MaxUses > 1) {
                LastBroken = (Def(id), Now);
                SoundPlayer.PlaySoundInstance("Assets/sounds/bullet_destroy.ogg", SoundContext.Effect, 1f, pitchOverride: -0.5f);
            }
        }
        else
            p.ReadyTimes[id] = Now + CooldownFor(p, id);
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

    public static void StartWell(Vector2 at, int playerId) {
        Wells.Add((at, Now + 4, playerId));
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 1f, pitchOverride: -0.9f);
    }
    public static void StartSmoke(int playerId) {
        P(playerId).SmokeUntil = Now + (Has(playerId, Syn("Ninja")) ? 8 : 5);
        if (GameHandler.AllPlayerTanks[playerId] is { } t)
            GameHandler.Particles.MakeSmallExplosion(t.Position3D, 14, 0, 0.5f, 6);
    }
    public static void PlantFlag(Vector2 at, int playerId) {
        bool banner = Has(playerId, Syn("War Banner"));
        Flags.Add((at, Now + (banner ? 12 : 8), banner ? 200f : 100f, playerId));
        SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.4f, pitchOverride: -0.2f);
    }
    /// <summary>Shockwave: enemy shells nearby turn around, and the host shoves the enemy tanks (Sonic Boom: and hurts them).</summary>
    public static void DoShockwave(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } t)
            return;
        Waves.Add((t.Position, Now));
        foreach (var s in Shell.AllShells)
            if (s is not null && s.Owner is not null && IsEnemy(s.Owner) && Vector2.Distance(s.Position, t.Position) < 130f) {
                var away = s.Position - t.Position;
                if (away.LengthSquared() > 0.01f)
                    s.Velocity = Vector2.Normalize(away) * s.Velocity.Length();
            }
        if (Client.IsConnected() && !Client.IsHost())
            return;
        bool boom = Has(playerId, Syn("Sonic Boom"));
        foreach (var e in GameHandler.AllAITanks)
            if (e is { IsDestroyed: false } && IsEnemy(e) && Vector2.Distance(e.Position, t.Position) < 130f) {
                var away = e.Position - t.Position;
                if (away.LengthSquared() > 0.01f)
                    e.KnockbackVelocity = Vector2.Normalize(away) * 6f;
                if (boom)
                    e.Damage(new TankHurtContextOther(t, TankHurtContextOther.HurtContext.FromIngame, "Sonic Boom"), true);
            }
        SoundPlayer.PlaySoundInstance("Assets/sounds/mine_explode.ogg", SoundContext.Effect, 0.5f, pitchOverride: 0.6f);
    }
    public static void DropFreezeBomb(Vector2 at, int playerId) => FreezeBombs.Add((at, Now + 4, playerId));

    /// <summary>A freeze bomb goes off: enemies near it freeze for 4 seconds.</summary>
    private static void FreezeBurst(Vector2 at, int playerId) {
        float reach = Has(playerId, Syn("Cryo Cluster")) ? 150f : 75f;
        foreach (var e in GameHandler.AllAITanks)
            if (e is { IsDestroyed: false } && IsEnemy(e) && Vector2.Distance(e.Position, at) < reach && !TroveBosses.IsBoss(e))
                IcedUntil[e] = Now + 4;
            else if (e is { IsDestroyed: false } && TroveBosses.IsBoss(e) && Vector2.Distance(e.Position, at) < reach)
                IcedUntil[e] = Now + 1.5; // bosses shake it off sooner
        Waves.Add((at, Now));
        for (int i = 0; i < 8; i++)
            GameHandler.Particles.MakeShineSpot(new Vector3(at.X + MathF.Cos(i) * reach * 0.5f, 6, at.Y + MathF.Sin(i) * reach * 0.5f), Color.LightCyan, 0.8f);
        SoundPlayer.PlaySoundInstance("Assets/sounds/bullet_ricochet.ogg", SoundContext.Effect, 1f, pitchOverride: 0.9f);
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
        Wells.Clear();
        Flags.Clear();
        FreezeBombs.Clear();
        IcedUntil.Clear();
        _barrage.Clear();
        Friends.Clear();
        Oils.Clear();
        _oilSlide.Clear();
        foreach (var p in Players) {
            p.DroneTank = null;
            p.DroneMines.Clear();
            p.CloneTank = null;
            p.WingmanTank = null;
            p.HitThisRoom = false;
            p.Rallied = false;
            // health ups refill every room (Lucky Break: so does Second Wind)
            p.RoomHp = p.RoomHpMax;
            if (p.Has(Syn("Lucky Break")))
                p.SecondWindUsed = false;
        }
        for (int i = 0; i < 4; i++) {
            if (Players[i].Has(TroveItemId.MineDrone))
                SpawnDrone(i);
            // Holy Mantle: a free shield every room (Guardian: up to 2)
            if (Players[i].Has(TroveItemId.HolyMantle) && IsLocal(i)) {
                int want = Players[i].Has(Syn("Guardian")) ? 2 : 1;
                if (Players[i].Shields < want)
                    Players[i].Shields = want;
                if (GameHandler.AllPlayerTanks[i] is { IsDestroyed: false } mt)
                    ApplyTo(mt);
            }
            if (Players[i].Has(TroveItemId.BuddyTank))
                SpawnBuddy(i);
            if (Players[i].Has(TroveItemId.Wingman) && GameHandler.AllPlayerTanks[i] is { IsDestroyed: false } pt) {
                var w = SpawnAlly(TankID.Silver, pt.Position + new Vector2(0, 26), 0.55f);
                w.Properties.ShellLimit = 0;
                w.Properties.MineLimit = 0;
                Players[i].WingmanTank = w;
            }
        }
    }

    /// <summary>Mine Drone: a little drone that trails behind its player.</summary>
    private static void SpawnDrone(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank || P(playerId).DroneTank is { IsDestroyed: false })
            return;
        var d = SpawnAlly(TankID.Silver, tank.Position + new Vector2(0, 26), 0.45f);
        d.Properties.ShellLimit = 0;
        d.Properties.MineLimit = 0;
        d.Properties.Stationary = true;
        P(playerId).DroneTank = d;
        P(playerId).DroneAt = Now + 2;
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

    // ------------------------------------------------------------------ batch 6 helpers

    /// <summary>Oil Slick: enemies in the oil keep sliding the way they were going and can barely steer.</summary>
    private static Vector2 OilFilter(Tank t, Vector2 v) {
        if (Oils.Count == 0 || t is not AITank || !IsEnemy(t) || !Oils.Any(o => Now < o.Until && Vector2.Distance(o.At, t.Position) < OIL_RADIUS)) {
            _oilSlide.Remove(t);
            return v;
        }
        var last = _oilSlide.TryGetValue(t, out var l) ? l : v;
        // a little steering, and a little spin
        var now = Vector2.Lerp(last, v, 0.02f * TanksRebirth.RuntimeData.DeltaTime).Rotate(0.02f * TanksRebirth.RuntimeData.DeltaTime);
        if (now.LengthSquared() < 0.3f && v.LengthSquared() > 0.01f)
            now = Vector2.Normalize(v) * 0.6f; // once in, it never quite stops
        _oilSlide[t] = now;
        return now;
    }

    /// <summary>Oil Slick: a puddle ahead of the player (Oil Fire: it burns too).</summary>
    public static void SpillOil(Vector2 at, int playerId) {
        Oils.Add((at, Now + 5, playerId));
        if (Has(playerId, Syn("Oil Fire")))
            for (int i = 0; i < 7; i++)
                Fires.Add((at + Vector2.UnitX.Rotate(i * MathHelper.TwoPi / 7) * (i == 0 ? 0 : OIL_RADIUS * 0.6f), Now + 5, playerId));
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 1f, pitchOverride: -0.8f);
    }

    /// <summary>Box of Friends: buddy tanks next to the player for the rest of the room (Party: 3).</summary>
    public static void SpawnFriends(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        int n = Has(playerId, Syn("Party")) ? 3 : 2;
        for (int i = 0; i < n; i++) {
            var at = tank.Position + Vector2.UnitX.Rotate(i * MathHelper.TwoPi / n + 0.8f) * 32f;
            var f = SpawnAlly(TankID.Marine, at, 0.75f);
            Friends.Add((f, playerId));
        }
        GameHandler.Particles.MakeShineSpot(tank.Position3D + new Vector3(0, 12, 0), new Color(240, 190, 110), 1.5f);
    }

    /// <summary>Teleport Pads: one linked pad at a spot (each player has their own pair).</summary>
    public static void PlacePad(Vector2 at, int playerId) {
        Vector2 origin = new Coordinates.BlockMapPosition(0, 0);
        var gx = (int)MathF.Round((at.X - origin.X) / Block.SIDE_LENGTH);
        var gy = (int)MathF.Round((at.Y - origin.Y) / Block.SIDE_LENGTH);
        gx = Math.Clamp(gx, 1, Coordinates.BlockMapPosition.MAP_WIDTH_169 - 2);
        gy = Math.Clamp(gy, 1, Coordinates.BlockMapPosition.MAP_HEIGHT - 2);
        Vector2 pos = new Coordinates.BlockMapPosition(gx, gy);
        sbyte link = (sbyte)(100 + playerId);
        // a new pair replaces this player's old one (a third pad would confuse the link)
        int mine = Block.AllBlocks.Count(b => b is not null && b.Type == BlockID.Teleporter && b.TpLink == link);
        if (mine >= 2)
            foreach (var old in Block.AllBlocks.Where(b => b is not null && b.Type == BlockID.Teleporter && b.TpLink == link).ToArray())
                old!.Remove();
        var pad = new Block(BlockID.Teleporter, 1, pos) { TpLink = link };
        GameHandler.Particles.MakeShineSpot(new Vector3(pos.X, 8, pos.Y), new Color(170, 110, 255), 1.2f);
    }

    /// <summary>A clear grid spot for a pad: far from the player (the other end of the pair).</summary>
    private static Vector2? FarPadSpot(Tank tank) {
        var spots = new List<Vector2>();
        for (int x = 2; x < Coordinates.BlockMapPosition.MAP_WIDTH_169 - 2; x++)
            for (int y = 2; y < Coordinates.BlockMapPosition.MAP_HEIGHT - 2; y++) {
                Vector2 pos = new Coordinates.BlockMapPosition(x, y);
                if (Vector2.Distance(pos, tank.Position) < 180f)
                    continue;
                if (Block.AllBlocks.Any(b => b is not null && Vector2.Distance(b.Position, pos) < Block.SIDE_LENGTH))
                    continue;
                if (GameHandler.AllTanks.Any(t => t is { IsDestroyed: false } && Vector2.Distance(t.Position, pos) < Block.SIDE_LENGTH))
                    continue;
                spots.Add(pos);
            }
        return spots.Count == 0 ? null : spots[Random.Shared.Next(spots.Count)];
    }

    /// <summary>Mystery Pill effects: what each does (the last two are the bad ones).</summary>
    private static readonly string[] PillText = [
        "Speed up!", "Fire rate up!", "+1 shell on screen!", "+1 bounce!", "+1 shield!", "Shells faster!",
        "Speed down...", "Fire rate down...",
    ];

    /// <summary>Lucky Foot: how many times more often things drop (1 = normal).</summary>
    public static int Luck() {
        int luck = 1;
        foreach (var p in Players) {
            if (p.Has(Syn("Four-Leaf Clover"))) luck = Math.Max(luck, 3);
            else if (p.Has(TroveItemId.LuckyFoot)) luck = Math.Max(luck, 2);
        }
        return luck;
    }

    // ------------------------------------------------------------------ shells and mines

    /// <summary>Blasts (shell explosions, slider bombs, the Nuke) are 1.5x with Big Mines.</summary>
    public static float BlastSize(int playerId) => playerId is >= 0 and < 4 && Players[playerId].Has(TroveItemId.BigMines) ? 1.5f : 1f;
    public static bool BlastProof(int playerId) => Has(playerId, Syn("Blast Proof"));

    public const double CHARGE_SECONDS = 2.0;
    public static bool HasChargeShot(int playerId) => TroveRun.Active && playerId is >= 0 and < 4 && Players[playerId].Has(TroveItemId.ChargeShot);

    /// <summary>A fully charged shot: the shells it made fly much faster, pierce and break cork.</summary>
    public static void SuperCharge(PlayerTank tank, HashSet<Shell> before) {
        foreach (var s in Shell.AllShells) {
            if (s is null || !ReferenceEquals(s.Owner, tank) || before.Contains(s))
                continue;
            s.Velocity *= 3.2f;
            s.Pierce += 2;
            s.BreaksCork = true;
            s.VisualScale *= 1.3f;
            s.Tint = new Color(150, 220, 255);
        }
        GameHandler.Particles.MakeShineSpot(tank.TurretPosition3D, new Color(150, 220, 255), 1.4f);
        SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_regular_1.ogg", SoundContext.Effect, 0.8f, pitchOverride: -0.4f);
    }

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
        if (p.ShellColorFrom is { } colorFrom) {
            if (colorFrom == TroveItemId.MagicMushroom)
                s.Rainbow = true;
            else
                s.Tint = Def(colorFrom).Color;
        }
        if (p.Has(Syn("Big Guns")) && p.Has(TroveItemId.HeavyShells))
            s.Pierce++;
        if (p.Has(TroveItemId.Momentum) && GameHandler.AllPlayerTanks[playerId] is { } mt && mt.Velocity.Length() > mt.Properties.MaxSpeed * 0.6f)
            s.Pierce += p.Has(Syn("Freight Train")) ? 3 : 1;
        if (p.Has(TroveItemId.GhostShells))
            s.WallPasses = p.Has(Syn("Phase Rifle")) ? 2 : 1;
        if (p.Has(TroveItemId.RicochetMaster))
            s.GrowsOnBounce = true;
        // batch 6
        if (p.Has(TroveItemId.BoomerangShells)) {
            s.Boomerang = true;
            s.PiercesOnReturn = p.Has(Syn("Lawnmower"));
        }
        if (p.Has(TroveItemId.MortarShells)) {
            s.Lobbed = true;
            s.LobTime = 50f;
            s.ExplodeScale = MathF.Max(s.ExplodeScale, 4.5f * BlastSize(playerId) * (p.Has(Syn("Artillery")) ? 1.7f : 1f));
            s.SparesOwner = true;
            s.Boomerang = false; // a lobbed shell lands; with Mortar Boomerang a second one is lobbed back from there
        }
        if (p.Has(TroveItemId.Wraparound))
            s.Wraps = 2 + (p.Has(Syn("Infinite Loop")) ? 2 : 0);
        if (p.Has(TroveItemId.MegaShell)) {
            s.VisualScale *= 2.4f;
            s.HitRadius = 18f;
            s.ShellHitRadius = 18f;
            s.Pierce += 99;
            s.BreaksCork = true;
            s.Velocity *= 0.6f;
            s.ExplodeScale = MathF.Max(s.ExplodeScale, 6f * BlastSize(playerId) * (p.Has(Syn("Death Star")) ? 1.6f : 1f));
            s.SparesOwner = true;
        }
        if (p.Has(TroveItemId.CupidsArrow))
            s.Pierce += 99;
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
        if (Now < p.SmokeUntil && p.Has(Syn("Ninja")))
            props.MaxSpeed *= 1.2f;
        // stat upgrades
        props.MaxSpeed *= MathF.Pow(1.15f, p.Count(TroveItemId.NitroTreads)) * MathF.Pow(1.1f, p.Count(TroveItemId.Steroids))
            * MathF.Pow(1.1f, p.Count(TroveItemId.MagicMushroom)) * MathF.Pow(1.35f, p.Count(TroveItemId.Featherweight))
            * MathF.Pow(0.9f, p.Count(TroveItemId.HeartyMeal)) * MathF.Pow(0.72f, p.Count(TroveItemId.HeavyArmor));
        props.ShellCooldown = (uint)Math.Max(1, MathF.Round(props.ShellCooldown * MathF.Pow(0.75f, p.Count(TroveItemId.RapidBarrel)) * MathF.Pow(0.85f, p.Count(TroveItemId.MagicMushroom))));
        props.ShellLimit += p.Count(TroveItemId.RapidBarrel) + p.Count(TroveItemId.MagicMushroom) + (p.Has(Syn("Overclock")) ? 2 : 0);
        props.ShellSpeed *= MathF.Pow(1.25f, Math.Min(p.Count(TroveItemId.LongBarrel), 3)) * (p.Has(Syn("Speed Demon")) ? 1.25f : 1f);
        props.RicochetCount += (uint)p.Count(TroveItemId.LongBarrel);
        if (p.Has(TroveItemId.MagicMushroom))
            tank.Scaling *= 1.15f;
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
        if (p.Rallied)
            props.ShellCooldown = (uint)Math.Max(1, props.ShellCooldown / 2);
        if (p.LastStandOn) {
            props.ShellCooldown = (uint)Math.Max(1, props.ShellCooldown / 2);
            props.ShellLimit += 1;
        }
        // batch 6
        if (p.Has(TroveItemId.SacredHeart)) {
            float reach = p.Has(Syn("Heartseeker")) ? 420f : 240f;
            props.ShellHoming = new() { Radius = reach, Speed = props.ShellSpeed, Power = 0.09f * props.ShellSpeed };
            props.ShellCooldown = (uint)Math.Max(1, MathF.Round(props.ShellCooldown * 1.2f));
        }
        switch (p.Pill) { // Mystery Pill, this floor
            case 0: props.MaxSpeed *= 1.2f; break;
            case 1: props.ShellCooldown = (uint)Math.Max(1, MathF.Round(props.ShellCooldown * 0.75f)); break;
            case 2: props.ShellLimit += 1; break;
            case 3: props.RicochetCount += 1; break;
            case 5: props.ShellSpeed *= 1.25f; break;
            case 6: props.MaxSpeed *= 0.85f; break;
            case 7: props.ShellCooldown = (uint)Math.Max(1, MathF.Round(props.ShellCooldown * 1.25f)); break;
        }
        if (p.Has(TroveItemId.MegaShell)) {
            // one shell at a time (one per barrel with Triple Shot), and a slower reload
            props.ShellLimit = Math.Max(1, props.ShellShootCount);
            props.ShellCooldown = (uint)Math.Max(1, MathF.Round(props.ShellCooldown * 1.5f));
        }
        if (p.Has(TroveItemId.MineDrone))
            props.MineLimit += (uint)(p.Has(Syn("Minesweeper")) ? 2 : 1); // the drone's mines don't use up yours
        // speed and fire-rate items
        if (p.Has(TroveItemId.Featherweight)) {
            // light and nimble: it gets up to speed and turns quicker too
            props.Acceleration *= MathF.Pow(1.5f, p.Count(TroveItemId.Featherweight));
            props.TurningSpeed *= 1.3f;
        }
        if (p.Has(TroveItemId.Turbocharger)) props.MaxSpeed *= 1.1f;
        if (p.Has(TroveItemId.NitroBoost)) props.MaxSpeed *= 1.1f;
        if (p.Has(TroveItemId.HairTrigger)) {
            props.ShellCooldown = (uint)Math.Max(1, MathF.Round(props.ShellCooldown * 0.75f));
            props.ShellLimit += 1;
        }
        if (p.Has(TroveItemId.OverdriveEngine)) {
            props.MaxSpeed *= 1.25f;
            props.ShellCooldown = (uint)Math.Max(1, MathF.Round(props.ShellCooldown * 0.8f));
            props.MineLimit = 0;
        }
        if (p.Has(TroveItemId.GatlingBarrel)) props.ShellLimit += 2;
        if (p.Has(TroveItemId.LightweightAlloy)) {
            props.MaxSpeed *= 1.2f;
            props.TurningSpeed *= 1.6f;
            props.MaximalTurn *= 1.4f;
        }
        p.BaseSpeed = props.MaxSpeed;
        p.BaseCooldown = props.ShellCooldown;
        p.Dynamic = p.Has(TroveItemId.Turbocharger) || p.Has(TroveItemId.Adrenaline) || p.Has(TroveItemId.NitroBoost)
            || p.Has(TroveItemId.CoffeeThermos) || p.Has(TroveItemId.GatlingBarrel);
        int kidArmor = props.Armor?.HitPoints ?? 0; // Kid Mode's own armor (player 2)
        if (p.Has(TroveItemId.GlassCannon))
            p.Shields = 0; // glass cannons can't keep shields
        p.RoomHp = Math.Min(p.RoomHp, p.RoomHpMax);
        p.FloorHp = Math.Min(p.FloorHp, p.FloorHpMax);
        int extraHits = p.Shields + p.RoomHp + p.FloorHp + p.BattleHp;
        if (extraHits > 0)
            props.Armor = new TankArmor(tank, kidArmor + extraHits);
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
        if (!TroveRun.Active) {
            for (int i = 0; i < 4; i++)
                if (_glow[i] is { } g) { g.Destroy(); _glow[i] = null; }
            return;
        }
        HandleRequests();
        UpdateUnderglow();
        if (CampaignGlobals.InMission)
            ReadyChimes();
        // the BOOST button shows this phone's player's bar
        var me = Players[MyPlayer];
        if (me.BoostFuel > 0 && me.BoostMax > 0 && CampaignGlobals.InMission)
            TanksRebirth.Internals.Common.Framework.Input.TouchControls.SetBoost(me.BoostFuel / me.BoostMax, me.Boosting);
        for (int i = 0; i < 4; i++) {
            var t = GameHandler.AllPlayerTanks[i];
            if (t is null)
                continue;
            var p = Players[i];
            if (IsLocal(i)) {
                if (t.IsDestroyed)
                    p.Shields = 0;
                else if (p.Shields + p.RoomHp + p.FloorHp + p.BattleHp > 0) {
                    int have = Math.Max(0, (t.Properties.Armor?.HitPoints ?? 0) - _shieldBase[i]);
                    int lost = p.Shields + p.RoomHp + p.FloorHp + p.BattleHp - have;
                    for (; lost > 0; lost--) {
                        if (p.Shields > 0) p.Shields--;
                        else if (p.RoomHp > 0) p.RoomHp--;
                        else if (p.FloorHp > 0) p.FloorHp--;
                        else if (p.BattleHp > 0) p.BattleHp--;
                    }
                }
            }
            if (t.IsDestroyed || !CampaignGlobals.InMission)
                continue;
            if (IsLocal(i))
                SpeedItems(t, p);
            ItemTrails(t, p);

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

            if (IsLocal(i)) {
                // Hot Potato: an enemy shell touching your mine sets it off, bigger
                if (p.Has(TroveItemId.HotPotato))
                    foreach (var m in Mine.AllMines.ToArray()) {
                        if (m is null || m.Detonated || !ReferenceEquals(m.Owner, t))
                            continue;
                        var hit = Shell.AllShells.FirstOrDefault(s => s is not null && s.Owner is not null && IsEnemy(s.Owner) && Vector2.Distance(s.Position, m.Position) < 16f);
                        if (hit is null)
                            continue;
                        hit.Destroy(Shell.DestructionContext.WithMine);
                        m.ExplosionRadius *= p.Has(Syn("Powder Keg")) ? 2f : 1.5f;
                        m.Detonate();
                    }
                // Wingman: shoots enemy shells near you out of the air
                if (p.WingmanTank is { IsDestroyed: false } wm) {
                    // stays close by
                    var want = t.Position + new Vector2(MathF.Cos((float)Now), MathF.Sin((float)Now)) * 34f;
                    wm.Position = Vector2.Lerp(wm.Position, want, 0.12f);
                    double every = p.Has(Syn("Air Support")) ? 0.4 : 0.8;
                    if (Now - p.WingmanShotAt > every) {
                        var target = Shell.AllShells.Where(s => s is not null && s.Owner is not null && IsEnemy(s.Owner) && Vector2.Distance(s.Position, t.Position) < 140f)
                            .OrderBy(s => Vector2.Distance(s!.Position, t.Position)).FirstOrDefault();
                        if (target is not null) {
                            p.WingmanShotAt = Now;
                            Bolts.Add((wm.Position, target.Position, Now));
                            target.Destroy(Shell.DestructionContext.WithShell);
                        }
                    }
                }
                // Mine Drone: trails behind you and drops mines (they can't hurt you)
                if (p.DroneTank is { IsDestroyed: false } dr) {
                    var back = Vector2.UnitY.Rotate(t.ChassisRotation);
                    dr.Position = Vector2.Lerp(dr.Position, t.Position - back * 30f, 0.1f);
                    dr.Physics.Position = dr.Position / Tank.UNITS_PER_METER;
                    p.DroneMines.RemoveAll(m => m.Detonated || !Mine.AllMines.Contains(m));
                    bool sweeper = p.Has(Syn("Minesweeper"));
                    if (Now >= p.DroneAt && p.DroneMines.Count < (sweeper ? 2 : 1)) {
                        p.DroneAt = Now + (sweeper ? 2.0 : 4.0);
                        var m = new Mine(t, dr.Position, 600) { SparesOwner = true };
                        t.OwnedMineCount++;
                        AfterMine(t, m);
                        m.SparesOwner = true;
                        p.DroneMines.Add(m);
                        if (Client.IsConnected())
                            Client.SyncMinePlace(m.Position, m.DetonateTime, t.WorldId, m.NetId);
                    }
                }
                // Rally Flag: near one of your flags, you shoot twice as fast
                bool rallied = Flags.Any(f => Now < f.Until && Vector2.Distance(f.At, t.Position) < f.Reach);
                if (rallied != p.Rallied) {
                    p.Rallied = rallied;
                    ApplyTo(t);
                }
                // Ninja: faster in your smoke
                if (Now < p.SmokeUntil && Has(i, Syn("Ninja")) && (int)(Now * 10) % 4 == 0)
                    GameHandler.Particles.MakeSmokeCloud(t.Position3D, 2, 1);
            }
            if (Now < p.SmokeUntil && (int)(Now * 8) % 3 == 0)
                GameHandler.Particles.MakeSmokeCloud(t.Position3D, 4, 1);

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
            // gravity wells pull enemy shells (everyone) and enemy tanks (the host moves those)
            for (int w = Wells.Count - 1; w >= 0; w--) {
                var (wat, wuntil, wpl) = Wells[w];
                if (Now > wuntil) {
                    Wells.RemoveAt(w);
                    if (Has(wpl, Syn("Singularity")))
                        new Explosion(wat, 10f * BlastSize(wpl), GameHandler.AllPlayerTanks[wpl], 0.8f, -0.4f) { SparesOwner = true };
                    continue;
                }
                foreach (var s in Shell.AllShells)
                    if (s is not null && s.Owner is not null && IsEnemy(s.Owner) && Vector2.Distance(s.Position, wat) < 160f) {
                        float sp = s.Velocity.Length();
                        var want = Vector2.Normalize(wat - s.Position + new Vector2(0.01f)) * sp;
                        s.Velocity = Vector2.Normalize(Vector2.Lerp(s.Velocity, want, 0.12f * TanksRebirth.RuntimeData.DeltaTime)) * sp;
                    }
                if (!Client.IsConnected() || Client.IsHost())
                    foreach (var e in GameHandler.AllAITanks)
                        if (e is { IsDestroyed: false } && IsEnemy(e) && Vector2.Distance(e.Position, wat) is var d && d < 170f && d > 8f && !TroveBosses.IsBoss(e))
                            e.Position += Vector2.Normalize(wat - e.Position) * 0.7f * TanksRebirth.RuntimeData.DeltaTime;
            }
            // freeze bombs: go off when an enemy comes close, or when the fuse runs out
            for (int fb = FreezeBombs.Count - 1; fb >= 0; fb--) {
                var (fat, fuse, fpl) = FreezeBombs[fb];
                bool near = GameHandler.AllAITanks.Any(e => e is { IsDestroyed: false } && IsEnemy(e) && Vector2.Distance(e.Position, fat) < 40f);
                if (near || Now > fuse) {
                    FreezeBombs.RemoveAt(fb);
                    FreezeBurst(fat, fpl);
                }
            }
            // rocket barrage: one rocket at a time, each at the nearest enemy
            for (int rb = _barrage.Count - 1; rb >= 0; rb--) {
                if (Now < _barrage[rb].At)
                    continue;
                int bpl = _barrage[rb].Player;
                _barrage.RemoveAt(rb);
                if (GameHandler.AllPlayerTanks[bpl] is not { IsDestroyed: false } bt)
                    continue;
                var foe = GameHandler.AllAITanks.Where(e => e is { IsDestroyed: false } && IsEnemy(e)).OrderBy(e => Vector2.Distance(e!.Position, bt.Position)).FirstOrDefault();
                var dir = foe is not null ? Vector2.Normalize(foe.Position - bt.Position) : new Vector2(-Vector2.UnitY.Rotate(bt.TurretRotation).X, Vector2.UnitY.Rotate(bt.TurretRotation).Y);
                var homing = Has(bpl, Syn("Heat Seekers")) ? new Shell.HomingProperties { Radius = 260f, Speed = 5f, Power = 0.4f } : default;
                var rk = new Shell(bt.Position + dir * 20f, dir * 5f, ShellID.Rocket, bt, 0, homing);
                ModifyShell(bpl, rk);
                if (Client.IsConnected())
                    Client.SyncShellFire(rk);
            }
            Flags.RemoveAll(f => Now > f.Until);
            Oils.RemoveAll(o => Now > o.Until);
            Friends.RemoveAll(f => f.Tank.IsDestroyed);
            foreach (var key in IcedUntil.Where(kv => Now > kv.Value).Select(kv => kv.Key).ToList())
                IcedUntil.Remove(key);
            Waves.RemoveAll(wv => Now - wv.Time > 0.5);
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
        TroveItemId.GravityWell => "WELL",
        TroveItemId.SmokeBomb => "SMOKE",
        TroveItemId.RallyFlag => "FLAG",
        TroveItemId.Shockwave => "SHOCK",
        TroveItemId.RocketBarrage => "BARRAGE",
        TroveItemId.FreezeBomb => "FREEZE",
        TroveItemId.BoxOfFriends => "FRIENDS",
        TroveItemId.OilSlick => "OIL",
        TroveItemId.TeleportPads => "PADS",
        _ => Def(id).Name.ToUpperInvariant(),
    };

    /// <summary>What the on-screen ITEM button shows for this player, or null.</summary>
    public static (string Title, string Sub, bool Usable)? ButtonFor(int playerId) {
        if (!TroveRun.Active || playerId is < 0 or >= 4)
            return null;
        var p = Players[playerId];
        if (p.Active is { } a)
            return p.ActiveReady ? (ShortName(a), "tap to use", true)
                : (ShortName(a), $"ready in {Math.Ceiling(p.SecondsLeft(a))}s", false);
        if (MissileUpgrade.ShotsLeft(playerId) > 0)
            return ("ROCKETS", $"{MissileUpgrade.ShotsLeft(playerId)} shots", false);
        if (p.SliderBombs > 0)
            return ("BOMBS", $"{p.SliderBombs} left", false);
        if (p.HomingOn)
            return ("HOMING", "this floor", false);
        return null;
    }
}
