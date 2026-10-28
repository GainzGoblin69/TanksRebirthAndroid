using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.Framework.Input;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.Troves;

public enum TroveItemId { Rocket, Homing, SliderBomb, ExtraLife, Shield, SpeedTreads, BouncyShells, ExtraShell, MinePack, Detonator, BlockBust }

/// <summary>How an item works: used with the ITEM button, takes effect when picked up, or lasts the whole run.</summary>
public enum TroveItemKind { Active, Instant, Passive }

public sealed record TroveItemDef(TroveItemId Id, string Name, string Description, TroveItemKind Kind, Color Color);

/// <summary>
/// Tank Troves items. Each floor has an item room (an item on a pedestal), and cleared rooms sometimes leave a
/// heart or a shield behind. Active items sit in your ITEM slot (one at a time) until you use them with the
/// ITEM button (or Y); instant ones apply right away; passive ones last the whole run.
/// Items belong to the player who picked them up; each phone keeps its own players' items.
/// </summary>
public static class TroveItems {
    public static readonly TroveItemDef[] All = [
        new(TroveItemId.Rocket, "Rockets", "ITEM button: your next 5 shots are rockets", TroveItemKind.Active, new Color(240, 120, 30)),
        new(TroveItemId.Homing, "Homing Shells", "ITEM button: your shots home in, for the rest of the floor", TroveItemKind.Active, new Color(60, 140, 230)),
        new(TroveItemId.SliderBomb, "Slider Bombs", "ITEM button: your next 5 mines slide forward and explode", TroveItemKind.Active, new Color(60, 60, 70)),
        new(TroveItemId.ExtraLife, "Extra Life", "+1 life", TroveItemKind.Instant, new Color(220, 50, 60)),
        new(TroveItemId.Shield, "Shield", "Blocks the next hit", TroveItemKind.Instant, new Color(150, 170, 200)),
        new(TroveItemId.SpeedTreads, "Speed Treads", "Drive faster (whole run)", TroveItemKind.Passive, new Color(240, 200, 40)),
        new(TroveItemId.BouncyShells, "Bouncy Shells", "+1 bounce on every shot (whole run)", TroveItemKind.Passive, new Color(60, 200, 210)),
        new(TroveItemId.ExtraShell, "Extra Shell", "+1 shell on screen at once (whole run)", TroveItemKind.Passive, new Color(150, 110, 200)),
        new(TroveItemId.MinePack, "Mine Pack", "+1 mine at once (whole run)", TroveItemKind.Passive, new Color(230, 190, 40)),
        new(TroveItemId.Detonator, "Detonator", "Mine button again blows up your mines (whole run)", TroveItemKind.Passive, new Color(200, 50, 40)),
        new(TroveItemId.BlockBust, "Block Bust", "ITEM button: breaks every breakable block in the room", TroveItemKind.Active, new Color(205, 140, 80)),
    ];
    public static TroveItemDef Def(TroveItemId id) => All[(int)id];

    // item rooms draw from everything; rooms only drop hearts and shields
    public static readonly TroveItemId[] PedestalPool = [
        TroveItemId.Rocket, TroveItemId.Rocket, TroveItemId.Homing, TroveItemId.Homing, TroveItemId.SliderBomb, TroveItemId.SliderBomb,
        TroveItemId.SpeedTreads, TroveItemId.BouncyShells, TroveItemId.BouncyShells, TroveItemId.ExtraShell, TroveItemId.ExtraShell,
        TroveItemId.MinePack, TroveItemId.MinePack, TroveItemId.Detonator, TroveItemId.BlockBust, TroveItemId.BlockBust,
        TroveItemId.ExtraLife, TroveItemId.Shield,
    ];
    /// <summary>Items that stack: every extra one adds another +1.</summary>
    public static bool Stacks(TroveItemId id) => id is TroveItemId.BouncyShells or TroveItemId.ExtraShell or TroveItemId.MinePack or TroveItemId.SpeedTreads;
    // cleared rooms mostly leave hearts and shields, sometimes a stacking item or a Block Bust
    public static readonly TroveItemId[] DropPool = [
        TroveItemId.ExtraLife, TroveItemId.ExtraLife, TroveItemId.Shield, TroveItemId.Shield, TroveItemId.Shield,
        TroveItemId.BouncyShells, TroveItemId.ExtraShell, TroveItemId.MinePack, TroveItemId.BlockBust,
    ];

    public sealed class PlayerItems {
        public TroveItemId? Active;
        public int RocketShotsGiven; // (rockets use the missile shots)
        public bool HomingOn;        // until the floor ends
        public int SliderBombs;
        public int Shields;
        public readonly Dictionary<TroveItemId, int> Passives = [];
        public int Count(TroveItemId id) => Passives.TryGetValue(id, out var n) ? n : 0;
    }

    public static readonly PlayerItems[] Players = [new(), new(), new(), new()];

    /// <summary>The last pickup, for the banner (name, description, how many you have now, when).</summary>
    public static (TroveItemDef Def, double Time, int Stack)? LastPickup;

    public static void Reset() {
        for (int i = 0; i < 4; i++)
            Players[i] = new PlayerItems();
        LastPickup = null;
    }

    public static void NewFloor() {
        foreach (var p in Players)
            p.HomingOn = false;
    }

    /// <summary>Players whose items this phone looks after.</summary>
    public static bool IsLocal(int playerId) =>
        LocalCoop.Active ? playerId < LocalCoop.PlayerCount : !Client.IsConnected() ? playerId == 0 : NetPlay.IsClientMatched(playerId);

    // ------------------------------------------------------------------ picking up

    public static void Give(int playerId, TroveItemId id) {
        if (playerId is < 0 or >= 4)
            return;
        var def = Def(id);
        int stack = def.Kind == TroveItemKind.Passive && IsLocal(playerId) ? Players[playerId].Count(id) + 1 : 1;
        LastPickup = (def, TroveRun.PlayTime, stack);
        SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.4f);
        if (!IsLocal(playerId))
            return;
        var p = Players[playerId];
        switch (def.Kind) {
            case TroveItemKind.Active:
                p.Active = id; // replaces whatever was in the slot
                break;
            case TroveItemKind.Instant:
                if (id == TroveItemId.ExtraLife) {
                    PlayerTank.Lives[playerId]++;
                    if (Client.IsConnected())
                        Client.SendLives();
                }
                else if (id == TroveItemId.Shield)
                    p.Shields++;
                break;
            case TroveItemKind.Passive:
                p.Passives[id] = p.Count(id) + 1;
                break;
        }
        if (GameHandler.AllPlayerTanks[playerId] is { IsDestroyed: false } tank)
            ApplyTo(tank);
    }

    // ------------------------------------------------------------------ using the ITEM slot

    /// <summary>True when this player has an item waiting in the ITEM slot (the button then uses it).</summary>
    public static bool HasActive(int playerId) => TroveRun.Active && playerId is >= 0 and < 4 && Players[playerId].Active is not null;

    public static void UseActive(int playerId) {
        if (!HasActive(playerId))
            return;
        var p = Players[playerId];
        var id = p.Active!.Value;
        p.Active = null;
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
                if (Client.IsConnected())
                    Client.SendTroveEvent(TroveRun.EV_BLOCK_BUST, 0);
                break;
        }
        SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_rocket.ogg", SoundContext.Effect, 0.7f);
        if (GameHandler.AllPlayerTanks[playerId] is { IsDestroyed: false } tank)
            ApplyTo(tank);
    }

    /// <summary>Block Bust: every breakable block in the room breaks (and stays broken if you come back).</summary>
    public static void BustBlocks() {
        int n = 0;
        foreach (var b in GameContent.Block.AllBlocks.ToArray())
            if (b is not null && b.Properties.IsDestructible) {
                b.Destroy();
                n++;
            }
        TroveRun.Room?.Blocks.RemoveAll(b => b.Type == BlockID.Cork);
        SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_destroy.ogg", SoundContext.Effect, 0.8f);
        TankGame.ClientLog.Write($"Troves: Block Bust broke {n} blocks", Internals.LogType.Info);
    }

    /// <summary>Detonator: while this player has mines out, the mine button blows them all up. True if it did.</summary>
    public static bool TryDetonate(PlayerTank tank) {
        if (!TroveRun.Active || tank.PlayerId is < 0 or >= 4 || Players[tank.PlayerId].Count(TroveItemId.Detonator) == 0)
            return false;
        var mines = Mine.AllMines.Where(m => m is not null && !m.Detonated && ReferenceEquals(m.Owner, tank)).ToArray();
        if (mines.Length == 0)
            return false;
        foreach (var m in mines)
            m!.Detonate();
        return true;
    }
    /// <summary>Detonator mines wait longer on their own, so you can pick the moment.</summary>
    public static bool HasDetonator(PlayerTank tank) =>
        TroveRun.Active && tank.PlayerId is >= 0 and < 4 && Players[tank.PlayerId].Count(TroveItemId.Detonator) > 0;

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
        var props = tank.Properties;
        // start from the normal player tank, then add the items
        tank.Properties.Armor?.Remove();
        tank.ApplyDefaults(ref tank.Properties);
        props = tank.Properties;
        props.MaxSpeed *= MathF.Pow(1.2f, p.Count(TroveItemId.SpeedTreads));
        props.RicochetCount += (uint)p.Count(TroveItemId.BouncyShells);
        props.ShellLimit += p.Count(TroveItemId.ExtraShell);
        props.MineLimit += (uint)p.Count(TroveItemId.MinePack);
        if (p.HomingOn)
            props.ShellHoming = new() { Radius = 200f, Speed = props.ShellSpeed, Power = 0.1f * props.ShellSpeed };
        int kidArmor = props.Armor?.HitPoints ?? 0; // Kid Mode's own armor (player 2)
        if (p.Shields > 0)
            props.Armor = new TankArmor(tank, kidArmor + p.Shields);
        _shieldBase[tank.PlayerId] = kidArmor;
    }

    private static readonly int[] _shieldBase = new int[4];

    /// <summary>Every update: shields used up by hits, and the ITEM button.</summary>
    public static void Update() {
        if (!TroveRun.Active)
            return;
        for (int i = 0; i < 4; i++) {
            if (!IsLocal(i))
                continue;
            var t = GameHandler.AllPlayerTanks[i];
            if (t is null)
                continue;
            if (t.IsDestroyed)
                Players[i].Shields = 0;
            else if (Players[i].Shields > 0)
                Players[i].Shields = Math.Max(0, (t.Properties.Armor?.HitPoints ?? 0) - _shieldBase[i]);
        }
    }

    /// <summary>What the on-screen ITEM button shows for this player, or null.</summary>
    public static (string Title, string Sub, bool Usable)? ButtonFor(int playerId) {
        if (!TroveRun.Active || playerId is < 0 or >= 4)
            return null;
        var p = Players[playerId];
        if (p.Active is { } a)
            return (a == TroveItemId.BlockBust ? "BUST" : Def(a).Name.Split(' ')[0].ToUpperInvariant(), "tap to use", true);
        if (MissileUpgrade.ShotsLeft(playerId) > 0)
            return ("ROCKETS", $"{MissileUpgrade.ShotsLeft(playerId)} shots", false);
        if (p.SliderBombs > 0)
            return ("BOMBS", $"{p.SliderBombs} left", false);
        if (p.HomingOn)
            return ("HOMING", "this floor", false);
        return null;
    }
}
