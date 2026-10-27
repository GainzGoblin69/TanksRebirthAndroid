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

    /// <summary>Checks the button / controller each update (for the players on this phone).</summary>
    public static void Update() {
        bool any = false;
        for (int i = 0; i < 4; i++)
            any |= _charges[i] > 0 || _shotsLeft[i] > 0;
        int me = NetPlay.GetMyClientId();
        TouchControls.MissileButton = any && !LocalCoop.HidesTouch
            ? (_charges[LocalCoop.Active ? 0 : me], _shotsLeft[LocalCoop.Active ? 0 : me]) : null;
        if (!any || !CampaignGlobals.InMission)
            return;
        if (LocalCoop.Active) {
            for (int i = 0; i < LocalCoop.PlayerCount; i++)
                if (LocalCoop.MissilePressed(i))
                    Activate(i);
        }
        else if (TouchControls.MissileJustPressed || PadJustPressedY())
            Activate(me);
    }

    private static bool PadJustPressedY() =>
        Internals.Common.InputUtils.CurrentGamePadSnapshot.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.Y)
        && !Internals.Common.InputUtils.OldGamePadSnapshot.IsButtonDown(Microsoft.Xna.Framework.Input.Buttons.Y);
}
