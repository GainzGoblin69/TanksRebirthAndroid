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
            // 1.9.1: the menu maps ship with the game (no internet needed)
            try {
                var bundled = Path.Combine("Content", "MenuMissions");
                if (Directory.Exists(bundled))
                    foreach (var f in Directory.GetFiles(bundled, "Menu*.mission").OrderBy(f => f.Length).ThenBy(f => f)) {
                        try { _cachedMissions.Add(ReadMenuMission(File.ReadAllBytes(f))); }
                        catch (Exception e) { TankGame.ClientLog.Write($"Menu map {Path.GetFileName(f)}: {e.Message}", LogType.Warn); }
                    }
                if (_cachedMissions.Count > 0)
                    TankGame.ClientLog.Write($"Loaded {_cachedMissions.Count} menu maps from the game files.", LogType.Info);
            }
            catch (Exception e) {
                TankGame.ClientLog.Write($"Couldn't read the built-in menu maps: {e.Message}", LogType.Warn);
                _cachedMissions.Clear();
            }
            if (_cachedMissions.Count > 0)
                goto ready;
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
        ready:
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

    /// <summary>A menu map: the older binary format, or 1.9.1's compressed JSON ({"tanks":[{x,y,facing,tier,team}], "blocks":[[type,stack,x,y]]}).</summary>
    static Mission ReadMenuMission(byte[] bytes) {
        if (bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b) {
            using var gz = new System.IO.Compression.GZipStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress);
            using var doc = System.Text.Json.JsonDocument.Parse(gz);
            var root = doc.RootElement;
            var tanks = new List<TankTemplate>();
            foreach (var t in root.GetProperty("tanks").EnumerateArray()) {
                int x = t.GetProperty("x").GetInt32(), y = t.GetProperty("y").GetInt32();
                string facing = t.TryGetProperty("facing", out var fv) ? fv.GetString() ?? "Down" : "Down";
                float rot = facing switch { "Left" => 1, "Up" => 2, "Right" => 3, _ => 0 } * Microsoft.Xna.Framework.MathHelper.PiOver2;
                string tier = t.TryGetProperty("tier", out var tv) ? tv.GetString() ?? "Brown" : "Brown";
                string team = t.TryGetProperty("team", out var tm) ? tm.GetString() ?? "NoTeam" : "NoTeam";
                bool player = t.TryGetProperty("player", out var pv) && pv.ValueKind != System.Text.Json.JsonValueKind.Null && pv.ValueKind != System.Text.Json.JsonValueKind.False;
                tanks.Add(new TankTemplate {
                    IsPlayer = player,
                    AiTier = ID.TankID.Collection.GetValue(tier) ?? ID.TankID.Brown,
                    Team = ID.TeamID.Collection.GetValue(team) ?? ID.TeamID.NoTeam,
                    Position = new BlockMapPosition(x, y),
                    Rotation = rot,
                });
            }
            var blocks = new List<BlockTemplate>();
            foreach (var b in root.GetProperty("blocks").EnumerateArray()) {
                var a = b.EnumerateArray().ToArray();
                if (a.Length < 4) continue;
                blocks.Add(new BlockTemplate {
                    Type = ID.BlockID.Collection.GetValue(a[0].GetString() ?? "Wood") ?? ID.BlockID.Wood,
                    Stack = (byte)Math.Clamp(a[1].GetInt32(), 1, 7),
                    Position = new BlockMapPosition(a[2].GetInt32(), a[3].GetInt32()),
                    TpLink = -1,
                });
            }
            return new Mission(tanks.ToArray(), blocks.ToArray()) { Name = root.TryGetProperty("name", out var nv) ? nv.GetString() ?? "" : "", Note = "" };
        }
        using var r = new BinaryReader(new MemoryStream(bytes));
        return Mission.Read(r);
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
