using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.Achievements;
using TanksRebirth.Enums;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// Saves which achievements are unlocked (achievements.json), so they stay unlocked and their popup only shows
/// once, and unlocks the event achievements the original game listed but never hooked up (meeting new tank
/// types, destroying yourself, finishing the campaign...).
/// </summary>
public static class AchievementStore {
    private static string FilePath => Path.Combine(TankGame.SaveDirectory, "achievements.json");
    private static int _lastCount = -1;
    private static bool _initialized;

    public static IList<IAchievement> All => VanillaAchievements.Repository.GetAchievements();

    public static void Initialize() {
        if (_initialized)
            return;
        _initialized = true;

        // pictures for the achievements that were listed without one
        var textures = new Dictionary<string, string> {
            ["Playing with fire"] = "seemarine", ["Double Whammy"] = "seegreen", ["Camouflage"] = "seewhite",
            ["Black Attack!"] = "seeblack", ["Doomed to failure"] = "selfmine", ["Just an accident"] = "selfshoot",
            ["I teleported bullets... For 3 days!"] = "tpbullet", ["See through the dragon's eyes"] = "thirdpersonvictory",
            ["The Power of Creation"] = "createcampaign", ["What are these?"] = "createmission",
            ["A winner is you"] = "vanillabeaten", ["Is winning this easy?"] = "custom5", ["All your base are belong to us"] = "custom10",
        };
        foreach (var a in All.OfType<Achievement>())
            if (a.Texture is null && textures.TryGetValue(a.Name, out var tex))
                try { a.Texture = GameResources.GetGameResource<Texture2D>($"Assets/textures/ui/achievement/{tex}"); } catch { }

        try {
            if (File.Exists(FilePath)) {
                var names = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [];
                foreach (var a in All)
                    if (names.Contains(a.Name))
                        a.Complete();
            }
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Couldn't read achievements.json: {e.Message}", LogType.Warn);
        }
        _lastCount = All.Count(a => a.IsComplete);

        CampaignGlobals.OnMissionStart += OnMissionStart;
        CampaignGlobals.OnMissionEnd += OnMissionEnd;
    }

    /// <summary>Saves when something new got unlocked (called every update).</summary>
    public static void Tick() {
        if (!_initialized)
            return;
        int count = All.Count(a => a.IsComplete);
        if (count == _lastCount)
            return;
        _lastCount = count;
        try {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(All.Where(a => a.IsComplete).Select(a => a.Name).ToList()));
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Couldn't save achievements.json: {e.Message}", LogType.Warn);
        }
    }

    /// <summary>Unlock by name (with its popup), if it isn't already.</summary>
    public static void Unlock(string name) {
        var list = All;
        for (int i = 0; i < list.Count; i++) {
            if (list[i].Name != name || list[i].IsComplete)
                continue;
            list[i].Complete();
            TankGame.VanillaAchievementPopupHandler?.SummonOrQueue(i);
            return;
        }
    }

    private static void OnMissionStart() {
        if (UI.MainMenu.MainMenuUI.IsActive)
            return;
        foreach (var t in GameHandler.AllAITanks) {
            if (t is null || t.IsDestroyed)
                continue;
            switch (t.AiTankType) {
                case TankID.Marine: Unlock("Playing with fire"); break;
                case TankID.Green: Unlock("Double Whammy"); break;
                case TankID.White: Unlock("Camouflage"); break;
                case TankID.Black: Unlock("Black Attack!"); break;
            }
        }
    }

    private static void OnMissionEnd(int delay, MissionEndContext context, bool result1up) {
        if (context is not (MissionEndContext.CampaignCompleteMajor or MissionEndContext.CampaignCompleteMinor))
            return;
        if (LevelProgress.CurrentCampaignKey == "Vanilla" && LevelProgress.Run is null) {
            Unlock("A winner is you");
            if (Difficulties.Types["POV"])
                Unlock("See through the dragon's eyes");
        }
    }

    /// <summary>A player tank was destroyed: self-destruction and teammate achievements.</summary>
    public static void PlayerDestroyed(PlayerTank victim, ITankHurtContext context) {
        if (context.Source is not PlayerTank killer)
            return;
        bool victimIsMe = NetPlay.IsClientMatched(victim.PlayerId);
        bool killerIsMe = NetPlay.IsClientMatched(killer.PlayerId);
        if (killer == victim) {
            if (!victimIsMe)
                return;
            if (context is TankHurtContextExplosion)
                Unlock("Doomed to failure");
            else if (context is TankHurtContextShell)
                Unlock("Just an accident");
            return;
        }
        if (victimIsMe)
            Unlock("TANKS a lot!");
        if (killerIsMe)
            Unlock("I cannon believe you've done this.");
    }
}
