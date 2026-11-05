using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals.Common.Framework.Audio;

namespace TanksRebirth.GameContent.Systems.Troves;

/// <summary>
/// Enemies that arrive during a fight (challenge waves, boss helpers) come down on parachutes: a shadow and a warning
/// ring show where each one will land for a couple of seconds, then the tank touches down. Every phone runs the same
/// drops from the same events, so they land together.
/// </summary>
public static class TroveDrops {
    public const double FALL_SECONDS = 2.4;

    public sealed class Drop {
        public Vector2 At;
        public int Tier;
        public double Start, LandAt;
        public Action<AITank>? After;
    }

    public static readonly List<Drop> Pending = [];
    public static bool Any => Pending.Count > 0;

    private static double Now => TroveRun.PlayTime;

    public static void Add(Vector2 at, int tier, Action<AITank>? after = null, double delay = 0) {
        Pending.Add(new Drop { At = at, Tier = tier, Start = Now + delay, LandAt = Now + delay + FALL_SECONDS, After = after });
        if (delay <= 0)
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", SoundContext.Effect, 0.4f, pitchOverride: -0.7f);
    }

    public static void Clear() => Pending.Clear();

    /// <summary>Lands the drops whose time has come.</summary>
    public static void Update() {
        if (!TroveRun.Active || !CampaignGlobals.InMission || Pending.Count == 0)
            return;
        for (int i = Pending.Count - 1; i >= 0; i--) {
            var d = Pending[i];
            if (Now < d.LandAt)
                continue;
            Pending.RemoveAt(i);
            var t = new TankTemplate { IsPlayer = false, AiTier = d.Tier, Position = d.At, Team = TeamID.Blue }.GetAiTank();
            t.Team = TeamID.Blue;
            d.After?.Invoke(t);
            GameHandler.Particles.MakeSmallExplosion(new Vector3(d.At.X, 2, d.At.Y), 8, 6, 0.9f, 8);
            SoundPlayer.PlaySoundInstance("Assets/sounds/tnk_destroy.ogg", SoundContext.Effect, 0.15f, pitchOverride: 0.4f);
        }
    }

    /// <summary>How far down a drop is (0 = just appeared high up, 1 = landed).</summary>
    public static float Progress(Drop d) => (float)Math.Clamp((Now - d.Start) / (d.LandAt - d.Start), 0, 1);
}
