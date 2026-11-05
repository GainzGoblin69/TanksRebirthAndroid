using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.Internals;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// Special tank skins earned by finishing the big things (the campaign, a Tank Troves run, a Battle...).
/// Saved to unlocks.json next to the other save data, with a few running counters for the "do it N times" ones.
/// </summary>
public static class Unlocks {
    public sealed record Goal(string Key, string Task, int Target = 1);

    /// <summary>Every unlock, keyed like <see cref="PlayerColors.Skin.Unlock"/>.</summary>
    public static readonly Goal[] Goals = [
        new("campaign", "Beat the campaign"),
        new("troves", "Win a Tank Troves run"),
        new("battle", "Win a Battle"),
        new("secret", "Find a secret room in Tank Troves"),
        new("bosses10", "Defeat 10 Tank Troves bosses", 10),
        new("troves3", "Win 3 Tank Troves runs", 3),
        new("rooms100", "Clear 100 Tank Troves rooms", 100),
        new("levels25", "Clear 25 different missions", 25),
        new("kills1000", "Destroy 1,000 tanks", 1000),
        new("achievements15", "Unlock 15 achievements", 15),
    ];

    sealed class Data {
        public List<string> Unlocked { get; set; } = [];
        public Dictionary<string, int> Counters { get; set; } = [];
    }

    static Data _data = new();
    static bool _loaded;
    static string FilePath => Path.Combine(TankGame.SaveDirectory, "unlocks.json");

    static void Load() {
        if (_loaded)
            return;
        _loaded = true;
        try {
            if (File.Exists(FilePath))
                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Couldn't read unlocks.json: {e.Message}", LogType.Warn);
        }
    }

    static void Save() {
        try {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data));
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Couldn't save unlocks.json: {e.Message}", LogType.Warn);
        }
    }

    public static Goal? GoalFor(string key) => Goals.FirstOrDefault(g => g.Key == key);

    public static bool Has(string key) {
        Load();
        return _data.Unlocked.Contains(key);
    }

    /// <summary>How far along a counted goal is (0 for one-off goals that aren't done).</summary>
    public static int Progress(string key) {
        Load();
        if (_data.Unlocked.Contains(key))
            return GoalFor(key)?.Target ?? 1;
        return _data.Counters.TryGetValue(key, out var n) ? n : 0;
    }

    public static int UnlockedCount {
        get { Load(); return Goals.Count(g => _data.Unlocked.Contains(g.Key)); }
    }

    /// <summary>The goal is done: unlocks its skin (with a popup the first time).</summary>
    public static void Grant(string key) {
        Load();
        if (_data.Unlocked.Contains(key) || GoalFor(key) is null)
            return;
        _data.Unlocked.Add(key);
        Save();
        var skin = PlayerColors.Skins.FirstOrDefault(s => s.Unlock == key);
        TankGame.ClientLog.Write($"Unlocked '{key}' ({skin?.Name})", LogType.Info);
        if (skin is not null)
            _toasts.Enqueue(skin);
    }

    /// <summary>One more toward a counted goal (unlocks it at its target).</summary>
    public static void Count(string key, int amount = 1) => SetAtLeast(key, Progress(key) + amount);

    /// <summary>Progress toward a counted goal from a number kept elsewhere (kills, achievements...).</summary>
    public static void SetAtLeast(string key, int value) {
        Load();
        if (_data.Unlocked.Contains(key) || GoalFor(key) is not { } goal)
            return;
        int now = _data.Counters.TryGetValue(key, out var n) ? n : 0;
        if (value <= now)
            return;
        _data.Counters[key] = value;
        if (value >= goal.Target)
            Grant(key); // (saves)
        else
            Save();
    }

    static int _tick;
    /// <summary>Checks the goals that follow numbers kept elsewhere (every second or so).</summary>
    public static void Tick() {
        if (++_tick % 60 != 0)
            return;
        try {
            SetAtLeast("kills1000", (int)Math.Min(int.MaxValue, TankGame.SaveFile.TotalKills));
            SetAtLeast("achievements15", AchievementStore.All.Count(a => a.IsComplete));
        }
        catch { }
    }

    // ---- "Unlocked: Rainbow tank!" popup
    static readonly Queue<PlayerColors.Skin> _toasts = new();
    static PlayerColors.Skin? _toast;
    static float _toastTime;
    public static PlayerColors.Skin? ShowingToast => _toast;

    /// <summary>Draws the unlock popup at the top of the screen (its own sprite batch).</summary>
    public static void DrawToast(SpriteBatch sb) {
        if (_toast is null) {
            if (!_toasts.TryDequeue(out var next))
                return;
            _toast = next;
            _toastTime = 0;
            SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.7f);
            Internals.Common.Framework.Input.Haptics.Pulse(180, 160);
        }
        _toastTime += RuntimeData.DeltaTime / 60f;
        const float Show = 4.5f;
        if (_toastTime > Show) {
            _toast = null;
            return;
        }
        float a = MathHelper.Clamp(MathF.Min(_toastTime / 0.3f, (Show - _toastTime) / 0.4f), 0f, 1f);
        float slide = (1f - EaseOut(MathHelper.Clamp(_toastTime / 0.35f, 0f, 1f))) * 140f;
        float s = WindowUtils.WindowHeight / 1080f;
        int w = (int)(720 * s), h = (int)(150 * s);
        var r = new Rectangle(WindowUtils.WindowWidth / 2 - w / 2, (int)((30 - slide) * s), w, h);

        int index = PlayerColors.Palette.Length + Array.IndexOf(PlayerColors.Skins, _toast);
        var accent = PlayerColors.MainOf(index);

        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X, r.Y + (int)(8 * s), r.Width, r.Height), 30 * s, new Color(0, 0, 0, (int)(90 * a)));
        ToggleSwitch.DrawRoundedRect(sb, r, 30 * s, new Color(36, 34, 46) * a);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X + (int)(4 * s), r.Y + (int)(4 * s), r.Width - (int)(8 * s), r.Height - (int)(8 * s)), 26 * s, new Color(52, 50, 66) * a);
        // the skin's colors as a swatch
        var sw = new Rectangle(r.X + (int)(22 * s), r.Y + (int)(22 * s), h - (int)(44 * s), h - (int)(44 * s));
        ToggleSwitch.DrawRoundedRect(sb, sw, 18 * s, accent * a);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(sw.X + (int)(10 * s), sw.Y + (int)(10 * s), sw.Width - (int)(20 * s), sw.Height - (int)(20 * s)), 12 * s,
            Color.Lerp(accent, Color.White, 0.25f) * a);
        var font = FontGlobals.RebirthFont;
        float tx = sw.Right + 26 * s;
        sb.DrawString(font, "NEW TANK SKIN UNLOCKED", new Vector2(tx, r.Y + 26 * s), new Color(255, 214, 90) * a, new Vector2(0.55f * s), 0f, Vector2.Zero);
        sb.DrawString(font, _toast.Name, new Vector2(tx, r.Y + 58 * s), Color.White * a, new Vector2(0.95f * s), 0f, Vector2.Zero);
        var task = GoalFor(_toast.Unlock)?.Task ?? "";
        sb.DrawString(font, task + " - pick it in Tank Color", new Vector2(tx, r.Y + 108 * s), new Color(200, 200, 215) * a, new Vector2(0.45f * s), 0f, Vector2.Zero);
        sb.End();
    }

    static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);
}
