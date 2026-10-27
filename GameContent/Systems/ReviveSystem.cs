using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.UI;
using TanksRebirth.Internals.Common.Framework.Input;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// Gears of War difficulty: a player who is hit goes "down" where they fell. A teammate who stays on that
/// spot for <see cref="REVIVE_SECONDS"/> brings them back; a green ring around the spot fills up meanwhile.
/// Online, every phone shows the ring, and the downed player's own phone does the revive (and tells the
/// others), so it only happens once.
/// </summary>
public static class ReviveSystem {
    public const float REVIVE_SECONDS = 5f;
    /// <summary>How close (game units) a teammate has to be to the spot.</summary>
    public const float REVIVE_RADIUS = 26f;

    private sealed class Downed {
        public int PlayerId, PlayerType, Team;
        public Vector2 Position;
        public float Rotation, Progress;
    }

    private static readonly Dictionary<int, Downed> _downed = [];

    public static void PlayerDown(PlayerTank tank) {
        _downed[tank.PlayerId] = new Downed {
            PlayerId = tank.PlayerId,
            PlayerType = tank.PlayerType,
            Team = tank.Team,
            Position = tank.Position,
            Rotation = tank.ChassisRotation,
        };
    }

    public static void Clear() => _downed.Clear();

    /// <summary>Called every game update.</summary>
    public static void Update() {
        if (_downed.Count == 0)
            return;
        if (!Difficulties.Types["GearsOfWar"] || !CampaignGlobals.InMission || IntermissionSystem.IsAwaitingNewMission) {
            if (!CampaignGlobals.InMission)
                _downed.Clear();
            return;
        }
        if (GameUI.Paused)
            return;

        float dt = (float)TankGame.LastGameTime.ElapsedGameTime.TotalSeconds;
        List<int>? revive = null;
        foreach (var d in _downed.Values) {
            // revived elsewhere (e.g. the packet from another phone arrived): forget the spot
            var current = GameHandler.AllPlayerTanks[d.PlayerId];
            if (current is not null && !current.IsDestroyed) {
                (revive ??= []).Add(-1 - d.PlayerId); // marker: just remove
                continue;
            }
            bool someoneThere = false;
            foreach (var p in GameHandler.AllPlayerTanks) {
                if (p is null || p.IsDestroyed || p.PlayerId == d.PlayerId)
                    continue;
                if (Vector2.Distance(p.Position, d.Position) <= REVIVE_RADIUS) {
                    someoneThere = true;
                    break;
                }
            }
            d.Progress = someoneThere ? d.Progress + dt / REVIVE_SECONDS : Math.Max(0, d.Progress - dt / REVIVE_SECONDS * 2);
            if (d.Progress >= 1f)
                (revive ??= []).Add(d.PlayerId);
        }
        if (revive is null)
            return;
        foreach (var id in revive) {
            if (id < 0) {
                _downed.Remove(-1 - id);
                continue;
            }
            var d = _downed[id];
            // online: only the downed player's own phone brings them back, then tells everyone
            bool mine = !Client.IsConnected() || NetPlay.IsClientMatched(d.PlayerId);
            if (!mine) {
                d.Progress = 1f; // full ring until their phone respawns them
                continue;
            }
            _downed.Remove(id);
            Revive(d);
        }
    }

    private static void Revive(Downed d) {
        var old = GameHandler.AllPlayerTanks[d.PlayerId];
        old?.Remove(true);
        var template = new TankTemplate {
            IsPlayer = true,
            Position = d.Position,
            Rotation = d.Rotation,
            Team = d.Team,
            PlayerType = d.PlayerType,
        };
        var tank = template.GetPlayerTank();
        if (Client.IsConnected())
            Client.RequestPlayerTankSpawn(tank);
        Internals.Common.Framework.Audio.SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", Internals.Common.Framework.Audio.SoundContext.Effect, 0.8f);
    }

    /// <summary>The green rings (screen space), drawn over the game.</summary>
    public static void Draw(SpriteBatch sb) {
        if (_downed.Count == 0 || !CampaignGlobals.InMission)
            return;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied);
        float scale = WindowUtils.WindowHeight / 1080f;
        foreach (var d in _downed.Values) {
            var center = MatrixUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(d.Position.X, 0.5f, d.Position.Y), CameraGlobals.GameView, CameraGlobals.GameProjection);
            float radius = 46f * scale;
            const int SEGMENTS = 48;
            float thickness = 9f * scale;
            var bg = Color.Black * 0.35f;
            var fill = new Color(52, 210, 90);
            int filled = (int)(SEGMENTS * Math.Clamp(d.Progress, 0f, 1f));
            for (int i = 0; i < SEGMENTS; i++) {
                float a0 = -MathHelper.PiOver2 + MathHelper.TwoPi * i / SEGMENTS;
                float a1 = -MathHelper.PiOver2 + MathHelper.TwoPi * (i + 1) / SEGMENTS;
                var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * radius;
                var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * radius;
                ToggleSwitch.DrawLine(sb, p0, p1, thickness, i < filled ? fill : bg);
            }
            var col = PlayerID.PlayerTankColors.TryGetValue(d.PlayerType, out var c) ? c : Color.White;
            ToggleSwitch.DrawDisc(sb, center, radius * 0.32f, Color.Lerp(col, Color.White, 0.3f));
        }
        sb.End();
    }
}
