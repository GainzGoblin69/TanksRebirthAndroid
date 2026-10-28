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
}

/// <summary>How an item works: used with the ITEM button, takes effect when picked up, or lasts the whole run.</summary>
public enum TroveItemKind { Active, Instant, Passive }

/// <param name="Recharge">Active items: rooms to clear before it can be used again (0 = used up).</param>
public sealed record TroveItemDef(TroveItemId Id, string Name, string Description, TroveItemKind Kind, Color Color, int Recharge = 0);

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
    ];

    // item rooms draw from nearly everything; challenge and secret rooms have stronger pools; rooms mostly drop small stuff
    public static readonly TroveItemId[] PedestalPool = [
        TroveItemId.Rocket, TroveItemId.Homing, TroveItemId.SliderBomb, TroveItemId.BlockBust, TroveItemId.Stopwatch, TroveItemId.ShellBurst,
        TroveItemId.Decoy, TroveItemId.Reroll, TroveItemId.SpeedTreads, TroveItemId.BouncyShells, TroveItemId.ExtraShell, TroveItemId.MinePack,
        TroveItemId.Detonator, TroveItemId.BigMines, TroveItemId.TripleShot, TroveItemId.QuickReload, TroveItemId.OrbitGuard, TroveItemId.TreasureMap,
        TroveItemId.HeavyShells, TroveItemId.ExplosiveShells, TroveItemId.GhostTreads, TroveItemId.ShellShield, TroveItemId.BossHunter,
        TroveItemId.BuddyTank, TroveItemId.ExtraLife, TroveItemId.Shield,
    ];
    public static readonly TroveItemId[] ChallengePool = [
        TroveItemId.TripleShot, TroveItemId.QuickReload, TroveItemId.HeavyShells, TroveItemId.ExplosiveShells, TroveItemId.BigMines,
        TroveItemId.OrbitGuard, TroveItemId.ShellBurst, TroveItemId.Stopwatch, TroveItemId.BossHunter, TroveItemId.Invincibility, TroveItemId.Nuke,
    ];
    public static readonly TroveItemId[] SecretPool = [
        TroveItemId.Nuke, TroveItemId.Invincibility, TroveItemId.BuddyTank, TroveItemId.TripleShot, TroveItemId.OrbitGuard,
        TroveItemId.BossHunter, TroveItemId.TreasureMap, TroveItemId.ExplosiveShells, TroveItemId.HeavyShells,
    ];
    /// <summary>Items that stack: every extra one adds more.</summary>
    public static bool Stacks(TroveItemId id) => id is TroveItemId.BouncyShells or TroveItemId.ExtraShell or TroveItemId.MinePack
        or TroveItemId.SpeedTreads or TroveItemId.QuickReload or TroveItemId.OrbitGuard or TroveItemId.HeavyShells or TroveItemId.TripleShot;
    // cleared rooms mostly leave hearts and shields, sometimes a stacking item or a Block Bust
    public static readonly TroveItemId[] DropPool = [
        TroveItemId.ExtraLife, TroveItemId.ExtraLife, TroveItemId.Shield, TroveItemId.Shield, TroveItemId.Shield,
        TroveItemId.BouncyShells, TroveItemId.ExtraShell, TroveItemId.MinePack, TroveItemId.BlockBust,
    ];

    public sealed class PlayerItems {
        public TroveItemId? Active;
        /// <summary>Rooms cleared since the active item was last used (it's ready at its Recharge).</summary>
        public int Charge;
        public bool HomingOn;        // until the floor ends
        public int SliderBombs;
        public int Shields;
        public double InvincibleUntil = -1;
        public bool WasInvincible;
        public readonly Dictionary<TroveItemId, int> Passives = [];
        public int Count(TroveItemId id) => Passives.TryGetValue(id, out var n) ? n : 0;
        public bool Has(TroveItemId id) => Count(id) > 0;
        public bool Has(TroveSynergy s) => Has(s.A) && Has(s.B);
        public bool ActiveReady => Active is { } a && Charge >= Def(a).Recharge;
    }

    public static readonly PlayerItems[] Players = [new(), new(), new(), new()];

    /// <summary>The last pickup, for the banner (name, description, how many you have now, when).</summary>
    public static (TroveItemDef Def, double Time, int Stack)? LastPickup;
    /// <summary>A synergy that just switched on, for its banner.</summary>
    public static (TroveSynergy Synergy, double Time)? LastSynergy;

    // run-wide effects
    public static double FrozenUntil = -1;
    public static AITank? DecoyTank;
    private static double _decoyUntil;
    public static readonly AITank?[] Buddies = new AITank?[4];

    private static bool _hooked;
    private static PlayerItems P(int id) => Players[Math.Clamp(id, 0, 3)];
    public static bool Has(int playerId, TroveSynergy s) => playerId is >= 0 and < 4 && Players[playerId].Has(s);
    private static TroveSynergy Syn(string name) => Synergies.First(s => s.Name == name);
    private static double Now => TroveRun.PlayTime;
    public static bool Frozen => TroveRun.Active && Now < FrozenUntil;

    public static void Reset() {
        for (int i = 0; i < 4; i++)
            Players[i] = new PlayerItems();
        LastPickup = null;
        LastSynergy = null;
        FrozenUntil = -1;
        DecoyTank = null;
        Array.Clear(Buddies);
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
        AITank.IsFrozen = t => Frozen && IsEnemy(t);
        Shell.IsFrozen = s => Frozen && s.Owner is AITank ai && IsEnemy(ai);
        AITank.TargetOverride = ai => TroveRun.Active && DecoyTank is { IsDestroyed: false } d && IsEnemy(ai) ? d : null;
        Tank.ExtraArmorDamage = (tank, ctx) => {
            if (!TroveRun.Active || !ReferenceEquals(tank, TroveRun.BossTank))
                return 0;
            if (ctx.Source is PlayerTank pt && P(pt.PlayerId).Has(TroveItemId.BossHunter))
                return 1;
            if (ctx.Source is AITank ai && Array.IndexOf(Buddies, ai) is int owner and >= 0 && Has(owner, Syn("Pack Hunter")))
                return 1;
            return 0;
        };
        // buddy tanks fire shells with their owner's items too
        Tank.OnShoot += (tank, shell) => {
            if (TroveRun.Active && tank is AITank ai && Array.IndexOf(Buddies, ai) is int owner and >= 0)
                ModifyShell(owner, shell);
        };
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
                p.Active = id; // replaces whatever was in the slot
                p.Charge = def.Recharge; // comes charged
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

    /// <summary>A room was cleared: active items recharge.</summary>
    public static void OnRoomCleared() {
        foreach (var p in Players)
            if (p.Active is { } a && p.Charge < Def(a).Recharge)
                p.Charge++;
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
                BustBlocks();
                SendAny(TroveRun.EV_BLOCK_BUST, 0);
                break;
            case TroveItemId.Stopwatch:
                StartFreeze(5);
                SendAny(TroveRun.EV_STOPWATCH, 5);
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
        if (Def(id).Recharge > 0)
            p.Charge = 0;
        else
            p.Active = null;
        SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_rocket.ogg", SoundContext.Effect, 0.7f);
        if (tank is { IsDestroyed: false })
            ApplyTo(tank);
    }

    private static void SendAny(int ev, int a) {
        if (Client.IsConnected())
            Client.SendTroveEvent(ev, a);
    }

    /// <summary>Block Bust: every breakable block in the room breaks (and stays broken if you come back).</summary>
    public static void BustBlocks() {
        int n = 0;
        foreach (var b in Block.AllBlocks.ToArray())
            if (b is not null && b.Properties.IsDestructible) {
                b.Destroy();
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
        P(playerId).InvincibleUntil = Now + 5;
        SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.5f, pitchOverride: 0.6f);
    }

    /// <summary>8 shells in a ring, each with all of this player's shell items.</summary>
    private static void ShellBurst(PlayerTank tank) {
        var props = tank.Properties;
        for (int i = 0; i < 8; i++) {
            var dir = Vector2.UnitY.Rotate(i * MathHelper.TwoPi / 8f);
            var shell = new Shell(tank.Position + dir * 18f, dir * props.ShellSpeed, props.ShellType, tank, props.RicochetCount, homing: props.ShellHoming);
            ModifyShell(tank.PlayerId, shell);
            if (Client.IsConnected())
                Client.SyncShellFire(shell);
        }
    }

    public static void Nuke(int playerId) {
        if (GameHandler.AllPlayerTanks[playerId] is not { IsDestroyed: false } tank)
            return;
        new Explosion(tank.Position, 15f * BlastSize(playerId), tank, 0.6f, -0.3f) { SparesOwner = true };
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
        _decoyUntil = Now + 6;
    }

    /// <summary>Each player's buddy tank (if they have one), next to them; call when a room is ready.</summary>
    public static void SpawnBuddies() {
        Array.Clear(Buddies);
        DecoyTank = null;
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
        foreach (var s in Shell.AllShells) {
            if (s is null || !ReferenceEquals(s.Owner, tank) || before.Contains(s))
                continue;
            ModifyShell(tank.PlayerId, s);
            if (sendExtras && !ReferenceEquals(s, tank.LastShotShell))
                Client.SyncShellFire(s);
        }
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
            s.ExplodeScale = 4f * BlastSize(playerId) * (p.Has(Syn("Fireworks")) ? 1.4f : 1f);
            s.SparesOwner = true;
            s.ExplodeOnPierce = p.Has(Syn("Wrecking Ball"));
        }
        if (p.Has(TroveItemId.ShellShield))
            s.ShellHitRadius = 16f;
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
        if (mines.Length == 0)
            return false;
        foreach (var m in mines)
            m!.Detonate();
        return true;
    }
    public static bool HasDetonator(PlayerTank tank) =>
        TroveRun.Active && tank.PlayerId is >= 0 and < 4 && Players[tank.PlayerId].Has(TroveItemId.Detonator);

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
            props.ShellHoming = new() { Radius = 200f, Speed = props.ShellSpeed, Power = 0.1f * props.ShellSpeed };
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
                        if (ai is { IsDestroyed: false } && IsEnemy(ai) && Vector2.Distance(ai.Position, t.Position) < 24f)
                            ai.Damage(new TankHurtContextOther(t, TankHurtContextOther.HurtContext.FromIngame, "Rammed"), true);
            }
            else if (p.WasInvincible) {
                t.Properties.Immortal = false;
                t.Properties.InvulnerableToMines = false;
            }
            p.WasInvincible = inv;

            // orbit guard: knock enemy shells out of the air (this phone's players)
            if (IsLocal(i) && p.Has(TroveItemId.OrbitGuard))
                foreach (var (pos, r) in Orbiters(i).ToArray())
                    foreach (var s in Shell.AllShells)
                        if (s is not null && s.Owner is not null && IsEnemy(s.Owner) && Vector2.Distance(s.Position, pos) < r + 5f)
                            s.Destroy(Shell.DestructionContext.WithShell);
        }
        if (DecoyTank is not null && Now > _decoyUntil) {
            if (!DecoyTank.IsDestroyed)
                DecoyTank.Remove(true);
            DecoyTank = null;
        }
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
        _ => Def(id).Name.ToUpperInvariant(),
    };

    /// <summary>What the on-screen ITEM button shows for this player, or null.</summary>
    public static (string Title, string Sub, bool Usable)? ButtonFor(int playerId) {
        if (!TroveRun.Active || playerId is < 0 or >= 4)
            return null;
        var p = Players[playerId];
        if (p.Active is { } a)
            return p.ActiveReady ? (ShortName(a), "tap to use", true) : (ShortName(a), $"charging {p.Charge}/{Def(a).Recharge}", false);
        if (MissileUpgrade.ShotsLeft(playerId) > 0)
            return ("ROCKETS", $"{MissileUpgrade.ShotsLeft(playerId)} shots", false);
        if (p.SliderBombs > 0)
            return ("BOMBS", $"{p.SliderBombs} left", false);
        if (p.HomingOn)
            return ("HOMING", "this floor", false);
        return null;
    }
}
