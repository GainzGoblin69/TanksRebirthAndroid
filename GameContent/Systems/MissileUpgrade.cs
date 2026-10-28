using System;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.Framework.Input;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// Gears of War: the missions that would give a bonus life give a missile upgrade instead. Using one (the
/// MISSILE button, or Y on a controller) turns your next full load of shots into missiles: they don't bounce,
/// break through destructible blocks, and can't be shot down.
/// </summary>
public static class MissileUpgrade {
    private static readonly int[] _charges = new int[4];
    private static readonly int[] _shotsLeft = new int[4];

    public static int Charges(int playerId) => playerId is >= 0 and < 4 ? _charges[playerId] : 0;
    public static int ShotsLeft(int playerId) => playerId is >= 0 and < 4 ? _shotsLeft[playerId] : 0;

    /// <summary>A new campaign or run: no upgrades yet.</summary>
    public static void Reset() {
        Array.Clear(_charges);
        Array.Clear(_shotsLeft);
    }

    /// <summary>The bonus mission was won: every player on this phone gets an upgrade.</summary>
    public static void Grant() {
        if (LocalCoop.Active) {
            for (int i = 0; i < LocalCoop.PlayerCount; i++)
                _charges[i]++;
        }
        else
            _charges[NetPlay.GetMyClientId()]++;
    }

    public static void Activate(int playerId) {
        if (playerId is < 0 or >= 4 || _charges[playerId] <= 0 || _shotsLeft[playerId] > 0)
            return;
        var tank = GameHandler.AllPlayerTanks[playerId];
        if (tank is null || tank.IsDestroyed)
            return;
        _charges[playerId]--;
        _shotsLeft[playerId] = Math.Max(1, tank.Properties.ShellLimit);
        SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_shoot_rocket.ogg", SoundContext.Effect, 0.8f);
    }

    public static void UsedShot(int playerId) {
        if (playerId is >= 0 and < 4 && _shotsLeft[playerId] > 0)
            _shotsLeft[playerId]--;
    }

    /// <summary>Rockets from a Tank Troves item: the next shots are missiles.</summary>
    public static void StartShots(int playerId, int shots) {
        if (playerId is >= 0 and < 4)
            _shotsLeft[playerId] += shots;
    }

    /// <summary>The ITEM/MISSILE button and controller Y, each update (for the players on this phone).</summary>
    public static void Update() {
        int me = LocalCoop.Active ? 0 : NetPlay.GetMyClientId();
        // what the button shows: a Tank Troves item first, else Gears of War missile upgrades
        if (Troves.TroveItems.ButtonFor(me) is { } tb)
            TouchControls.MissileButton = tb;
        else if (Charges(me) > 0 || ShotsLeft(me) > 0)
            TouchControls.MissileButton = ("MISSILE", ShotsLeft(me) > 0 ? $"{ShotsLeft(me)} shots" : $"x{Charges(me)}", ShotsLeft(me) == 0);
        else
            TouchControls.MissileButton = null;
        if (LocalCoop.HidesTouch)
            TouchControls.MissileButton = null;
        if (!CampaignGlobals.InMission)
            return;
        if (LocalCoop.Active) {
            for (int i = 0; i < LocalCoop.PlayerCount; i++)
                if (LocalCoop.MissilePressed(i))
                    Use(i);
        }
        else if (TouchControls.MissileJustPressed || PadJustPressedY())
            Use(me);
    }

    private static void Use(int playerId) {
        if (Troves.TroveItems.HasActive(playerId))
            Troves.TroveItems.UseActive(playerId);
        else
            Activate(playerId);
    }

    private static bool PadJustPressedY() =>
        Internals.Common.InputUtils.CurrentGamePadSnapshot.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.Y)
        && !Internals.Common.InputUtils.OldGamePadSnapshot.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.Y);
}
