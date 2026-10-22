using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TanksRebirth.Enums;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.UI;
using TanksRebirth.Internals;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems;

/// <summary>What the player has done on one mission of one campaign.</summary>
public sealed class MissionRecord {
    public bool Completed { get; set; }
    /// <summary>Fastest clear in seconds; 0 = none yet.</summary>
    public double BestSolo { get; set; }
    public double BestCoop { get; set; }
    public bool CoopAttempted { get; set; }
    /// <summary>Difficulty modifiers (by <see cref="LevelProgress.Modifier.Key"/>) this mission was cleared with.</summary>
    public List<string> Modifiers { get; set; } = [];
}

public sealed class ProgressData {
    public Dictionary<string, Dictionary<int, MissionRecord>> Campaigns { get; set; } = [];
}

/// <summary>
/// Tracks mission clears, best times (solo and co-op) and modifier clears, saved to progress.json next to the
/// other save data. Also runs "level select" attempts: one mission, one life, optionally one modifier, then
/// straight back to the level grid.
/// </summary>
public static class LevelProgress {
    public sealed record Modifier(string Key, string Name);

    /// <summary>Every difficulty modifier, ordered roughly easiest to hardest.</summary>
    public static readonly Modifier[] Modifiers = [
        new("InfiniteLives", "Infinite Lives"),
        new("AllStationary", "All Stationary"),
        new("AiCompanion", "AI Companion"),
        new("FFA", "Free-for-all"),
        new("Monochrome", "Monochrome"),
        new("RandomPlayer", "Randomized Player"),
        new("POV", "POV Mode"),
        new("ThunderMode", "Thunder Mode"),
        new("BulletBlocking", "Bullet Blocking"),
        new("Predictions", "Predictions"),
        new("TanksAreCalculators", "Tanks are Calculators"),
        new("Armored", "Armored"),
        new("BumpUp", "Bump Up"),
        new("Shotguns", "Shotguns"),
        new("UltraMines", "Ultra Mines"),
        new("PieFactory", "Lemon Pie Factory"),
        new("MachineGuns", "Machine Guns"),
        new("TacticalPlanes", "Tactical Planes"),
        new("RandomizedTanks", "Randomized Tanks"),
        new("BulletHell", "Bullet Hell"),
        new("AllHoming", "Seekers"),
        new("LanternMode", "Lantern Mode"),
        new("AllInvisible", "All Invisible"),
        new("MasterModBuff", "Master Mode"),
    ];

    private static ProgressData? _data;
    private static string FilePath => Path.Combine(TankGame.SaveDirectory, "progress.json");

    /// <summary>The campaign being played (its file name, e.g. "Vanilla"); set when a campaign starts.</summary>
    public static string CurrentCampaignKey { get; set; } = "Vanilla";

    // ---- mission timing ----
    private static bool _timing;
    private static int _timedMission = -1;
    private static double _elapsed;
    private static bool _initialized;

    public static void Initialize() {
        if (_initialized)
            return;
        _initialized = true;
        CampaignGlobals.OnMissionStart += OnMissionStart;
        CampaignGlobals.OnMissionEnd += OnMissionEnd;
    }

    private static ProgressData Data {
        get {
            if (_data is not null)
                return _data;
            try {
                _data = File.Exists(FilePath) ? JsonSerializer.Deserialize<ProgressData>(File.ReadAllText(FilePath)) : null;
            }
            catch (Exception e) {
                TankGame.ClientLog.Write($"Couldn't read progress.json: {e.Message}", LogType.Warn);
            }
            return _data ??= new ProgressData();
        }
    }

    private static void Save() {
        try {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Couldn't save progress.json: {e.Message}", LogType.Warn);
        }
    }

    public static MissionRecord Get(string campaign, int mission) {
        if (!Data.Campaigns.TryGetValue(campaign, out var missions))
            Data.Campaigns[campaign] = missions = [];
        if (!missions.TryGetValue(mission, out var rec))
            missions[mission] = rec = new MissionRecord();
        return rec;
    }

    public static bool IsCompleted(string campaign, int mission) =>
        Data.Campaigns.TryGetValue(campaign, out var m) && m.TryGetValue(mission, out var r) && r.Completed;

    public static bool AllModifiersCleared(string campaign, int mission) {
        if (!IsCompleted(campaign, mission))
            return false;
        var mods = Get(campaign, mission).Modifiers;
        return Modifiers.All(x => mods.Contains(x.Key));
    }

    /// <summary>The furthest mission a campaign may be started from: one past the furthest cleared mission.</summary>
    public static int HighestUnlocked(string campaign) {
        if (!Data.Campaigns.TryGetValue(campaign, out var m))
            return 0;
        var cleared = m.Where(x => x.Value.Completed).Select(x => x.Key).ToList();
        return cleared.Count == 0 ? 0 : cleared.Max() + 1;
    }

    public static int CompletedCount(string campaign) =>
        Data.Campaigns.TryGetValue(campaign, out var m) ? m.Count(x => x.Value.Completed) : 0;

    public static string FormatTime(double seconds) {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.ff") : t.ToString(@"m\:ss\.ff");
    }

    private static bool IsCoop => (Client.IsConnected() && Server.CurrentClientCount > 1) || LocalCoop.Active;

    private static void OnMissionStart() {
        _timedMission = CampaignGlobals.LoadedCampaign.CurrentMissionId;
        _elapsed = 0;
        _timing = true;
        if (IsCoop) {
            Get(CurrentCampaignKey, _timedMission).CoopAttempted = true;
            Save();
        }
    }

    /// <summary>Called every frame (from TankGame's update) to time missions and to finish level-select runs.</summary>
    public static void Tick(double dt) {
        if (_timing && CampaignGlobals.InMission && !GameUI.Paused)
            _elapsed += dt;

        if (Run is not null && _returnCountdown > 0) {
            _returnCountdown -= dt;
            if (_returnCountdown <= 0)
                FinishRun();
        }
    }

    private static void OnMissionEnd(int delay, MissionEndContext context, bool result1up) {
        if (!_timing)
            return;
        _timing = false;
        bool won = context is MissionEndContext.Win or MissionEndContext.CampaignCompleteMajor or MissionEndContext.CampaignCompleteMinor;

        if (won && _timedMission >= 0) {
            var rec = Get(CurrentCampaignKey, _timedMission);
            rec.Completed = true;
            if (IsCoop) {
                rec.CoopAttempted = true;
                if (rec.BestCoop <= 0 || _elapsed < rec.BestCoop)
                    rec.BestCoop = _elapsed;
            }
            else if (rec.BestSolo <= 0 || _elapsed < rec.BestSolo)
                rec.BestSolo = _elapsed;

            if (Run is { Modifier: { } mod } && !rec.Modifiers.Contains(mod.Key))
                rec.Modifiers.Add(mod.Key);
            Save();
        }

        // level-select runs go back to the grid after a moment, win or lose
        if (Run is not null) {
            SuppressResults = true;
            _returnCountdown = won ? 3.0 : 2.5;
        }
    }

    // ---- level-select runs ----
    public sealed record LevelRun(string Campaign, int Mission, Modifier? Modifier, bool FromModifierGrid);
    public static LevelRun? Run { get; private set; }
    /// <summary>Set when a level-select run ends, so the campaign results screen isn't shown.</summary>
    public static bool SuppressResults { get; set; }
    /// <summary>Raised when a run is over and the game is back in the menu; the level grid reopens itself.</summary>
    public static event Action<LevelRun>? OnRunFinished;

    private static double _returnCountdown;
    private static Dictionary<string, bool>? _savedTypes;
    private static (int mono, int disguise, int lower, int upper) _savedValues;

    public static void BeginRun(LevelRun run) {
        Run = run;
        _returnCountdown = 0;
        SuppressResults = false;

        // the run uses exactly its own modifier (or none); the player's own difficulty choices come back afterwards
        _savedTypes = new Dictionary<string, bool>(Difficulties.Types);
        _savedValues = (Difficulties.MonochromeValue, Difficulties.DisguiseValue, Difficulties.RandomTanksLower, Difficulties.RandomTanksUpper);
        foreach (var key in Difficulties.Types.Keys.ToList())
            Difficulties.Types[key] = false;
        Difficulties.MonochromeValue = TankID.None;
        Difficulties.DisguiseValue = TankID.None;
        Difficulties.RandomTanksLower = TankID.None;
        Difficulties.RandomTanksUpper = TankID.None;

        if (run.Modifier is { } m) {
            Difficulties.Types[m.Key] = true;
            switch (m.Key) {
                case "Monochrome": Difficulties.MonochromeValue = TankID.Violet; break;
                case "RandomizedTanks":
                    Difficulties.RandomTanksLower = TankID.Brown;
                    Difficulties.RandomTanksUpper = TankID.Black;
                    break;
            }
        }
    }

    /// <summary>Undo <see cref="BeginRun"/> when the run couldn't start.</summary>
    public static void AbortRun() {
        RestoreDifficulties();
        Run = null;
        _returnCountdown = 0;
        SuppressResults = false;
    }

    private static void RestoreDifficulties() {
        if (_savedTypes is null)
            return;
        foreach (var kv in _savedTypes)
            Difficulties.Types[kv.Key] = kv.Value;
        (Difficulties.MonochromeValue, Difficulties.DisguiseValue, Difficulties.RandomTanksLower, Difficulties.RandomTanksUpper) = _savedValues;
        _savedTypes = null;
    }

    private static void FinishRun() {
        var run = Run!;
        Run = null;
        _returnCountdown = 0;

        RestoreDifficulties();

        CampaignCompleteUI.IsViewingResults = false;
        if (!CampaignCompleteUI.ResultsFanfare.IsStopped())
            CampaignCompleteUI.ResultsFanfare.Stop();
        TankMusicSystem.StopAll();
        IntermissionSystem.IsAwaitingNewMission = false;
        GameUI.Paused = false;
        GameUI.QuitOut(netSent: true);

        OnRunFinished?.Invoke(run);
    }
}
