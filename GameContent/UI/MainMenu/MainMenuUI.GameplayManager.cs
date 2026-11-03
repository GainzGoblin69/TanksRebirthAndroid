using System.Linq;
using System.Collections.Generic;
using System;
using System.IO;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.RebirthUtils;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals;
using TanksRebirth.GameContent.Systems.Coordinates;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.UI.MainMenu;

#pragma warning disable
public static partial class MainMenuUI {
    static bool _firstTime = true;
    public static void UpdateGameplay() {
        if (IntermissionSystem.IsAwaitingNewMission || IntermissionSystem.BlackAlpha > 0) return;
        if (curMenuMission.Blocks is null) {
            LoadTemplateMission();
            return;
        }
        // do not count player tanks into the check
        var missionComplete = IntermissionHandler.NothingCanHappenAnymore(curMenuMission, out _, (t) => t is not PlayerTank);

        if (missionComplete)
            LoadTemplateMission();
    }
    public static void OpenGP() {
        SceneManager.CleanupScene();
        PlayerTank.TankKills.Clear();

        LoadTemplateMission();
    }
    public static void LeaveGP() {
        SceneManager.StartTnkScene();
        PlayerTank.SetLives(PlayerTank.StartingLives);
        SceneManager.CleanupEntities();
        PlacementSquare.ResetSquares();
        SceneManager.CleanupScene();
        Theme.Stop();
    }
    static bool _failedFetch;
    static volatile List<Mission>? _fetchedMissions;
    static string MenuCacheDir => Path.Combine(TankGame.SaveDirectory, "menu_missions");

    /// <summary>The menu's background battles. They come from GitHub, but the menu never waits for the internet:
    /// the copies saved last time are used straight away, and the download happens in the background
    /// (and is saved for next time). With no copies and no internet the menu board is simply empty.</summary>
    private static void LoadTemplateMission(bool autoSetup = true, bool loadForMenu = true) {
        if (_firstTime) {
            _firstTime = false;
            try {
                if (Directory.Exists(MenuCacheDir))
                    foreach (var f in Directory.GetFiles(MenuCacheDir, "Menu*.mission").OrderBy(f => f)) {
                        using var r = new BinaryReader(File.OpenRead(f));
                        _cachedMissions.Add(Mission.Read(r));
                    }
            }
            catch (Exception e) {
                TankGame.ClientLog.Write($"Couldn't read the saved menu maps: {e.Message}", LogType.Warn);
                _cachedMissions.Clear();
            }
            System.Threading.Tasks.Task.Run(FetchMenuMissions);
        }
        // the download finished: use the fresh maps from now on
        if (_fetchedMissions is { Count: > 0 } fresh) {
            _fetchedMissions = null;
            _cachedMissions = fresh;
        }
        if (_cachedMissions.Count == 0)
            return; // nothing yet (UpdateGameplay tries again each frame)

        try {
            var mission = _cachedMissions[Client.ClientRandom.Next(0, _cachedMissions.Count)];

            if (autoSetup) {
                CampaignGlobals.LoadedCampaign.LoadMission(mission);
                CampaignGlobals.LoadedCampaign.SetupLoadedMission(true);
            }
            if (loadForMenu)
                curMenuMission = mission;
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Couldn't set up a menu map: {e.Message}", LogType.Warn);
        }
    }

    private static void FetchMenuMissions() {
        var list = new List<Mission>();
        var raw = new List<byte[]>();
        try {
            for (int attempt = 1; attempt < 50; attempt++) {
                var link = $"https://github.com/RighteousRyan1/tanks_rebirth_motds/blob/master/menu_missions/Menu{attempt}.mission?raw=true";
                var bytes = WebUtils.DownloadWebFile(link, out _, out var status);
                if (status != System.Net.HttpStatusCode.OK || bytes is null) {
                    if (attempt == 1)
                        TankGame.ClientLog.Write($"Unable to fetch menu maps via the internet. Status: {status}", LogType.Warn);
                    break;
                }
                using var reader = new BinaryReader(new MemoryStream(bytes));
                list.Add(Mission.Read(reader));
                raw.Add(bytes);
            }
            if (list.Count == 0) {
                _failedFetch = true;
                return;
            }
            // saved for next time (offline starts get them too)
            try {
                Directory.CreateDirectory(MenuCacheDir);
                for (int i = 0; i < raw.Count; i++)
                    File.WriteAllBytes(Path.Combine(MenuCacheDir, $"Menu{i + 1:00}.mission"), raw[i]);
            }
            catch { }
            _fetchedMissions = list;
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Unable to fetch menu maps: {e.Message}", LogType.Warn);
        }
    }

}
