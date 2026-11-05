using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;

namespace TanksRebirth.Internals;

/// <summary>
/// Phones: updating from inside the app. Every build CI publishes the APK and a small version file to the repo's
/// "latest" release; the main menu checks that file in the background and, when there's a newer build, shows a
/// button in the bottom-left corner. Tapping it downloads the APK and hands it to Android's installer (which asks
/// you to confirm, and the first time, to allow installs from this app).
/// </summary>
public static class AppUpdate {
    public const string ReleaseBase = "https://github.com/GainzGoblin69/TanksRebirthAndroid/releases/download/latest/";

    public enum Stage { Idle, Checking, UpToDate, Available, Downloading, Installing, NeedsPermission, Failed }

    /// <summary>This APK's build number (set by the Android launcher).</summary>
    public static int CurrentBuild;
    public static int LatestBuild { get; private set; }
    public static Stage Now { get; private set; }
    public static float Progress { get; private set; }
    public static string Message { get; private set; } = "";
    /// <summary>Hands a downloaded APK to the system installer (set by the Android launcher).</summary>
    public static Action<string>? Install;

    static bool _checked;
    static string ApkPath => Path.Combine(TankGame.SaveDirectory, "update.apk");

    static HttpClient NewClient() => new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Looks for a newer build once (in the background; the menu never waits for it).</summary>
    public static void CheckOnce() {
        if (_checked || Install is null || CurrentBuild <= 0)
            return;
        _checked = true;
        Now = Stage.Checking;
        _ = Task.Run(async () => {
            try {
                // an old download from before is no longer needed
                try { if (File.Exists(ApkPath)) File.Delete(ApkPath); } catch { }
                using var http = NewClient();
                var text = (await http.GetStringAsync(ReleaseBase + "version.txt")).Trim();
                if (!int.TryParse(text, out var latest)) {
                    Now = Stage.Idle;
                    return;
                }
                LatestBuild = latest;
                Now = latest > CurrentBuild ? Stage.Available : Stage.UpToDate;
                TankGame.ClientLog.Write($"Update check: this is build {CurrentBuild}, the newest is {latest}.", LogType.Info);
            }
            catch (Exception e) {
                Now = Stage.Idle; // no internet: say nothing
                TankGame.ClientLog.Write($"Update check failed: {e.Message}", LogType.Warn);
            }
        });
    }

    /// <summary>Downloads the newest APK and starts installing it.</summary>
    public static void Start() {
        if (Now is Stage.Downloading or Stage.Installing || Install is null)
            return;
        if (Now == Stage.NeedsPermission && File.Exists(ApkPath)) {
            // they've been to the settings: try installing the one we already have
            Now = Stage.Installing;
            Install(ApkPath);
            return;
        }
        Now = Stage.Downloading;
        Progress = 0;
        _ = Task.Run(async () => {
            try {
                using var http = NewClient();
                http.Timeout = TimeSpan.FromMinutes(10);
                using var resp = await http.GetAsync(ReleaseBase + "TanksRebirth.apk", HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1;
                var tmp = ApkPath + ".part";
                await using (var input = await resp.Content.ReadAsStreamAsync())
                await using (var output = File.Create(tmp)) {
                    var buf = new byte[1 << 16];
                    long got = 0;
                    int n;
                    while ((n = await input.ReadAsync(buf)) > 0) {
                        await output.WriteAsync(buf.AsMemory(0, n));
                        got += n;
                        if (total > 0) Progress = (float)got / total;
                    }
                }
                if (File.Exists(ApkPath)) File.Delete(ApkPath);
                File.Move(tmp, ApkPath);
                Now = Stage.Installing;
                Install(ApkPath);
            }
            catch (Exception e) {
                Fail(e.Message);
            }
        });
    }

    // ---- what the Android installer reports back
    public static void Fail(string message) {
        Now = Stage.Failed;
        Message = message;
        TankGame.ClientLog.Write($"Update failed: {message}", LogType.Warn);
    }
    public static void NeedPermission() => Now = Stage.NeedsPermission;
    public static void Cancelled() => Now = LatestBuild > CurrentBuild ? Stage.Available : Stage.Idle;

    // ---- the button in the main menu's bottom-left corner
    static bool _down, _wasDown;
    static Vector2 _press = new(-1);

    /// <summary>Draws the update button (main menu, phones) and handles taps on it. The sprite batch is already begun.</summary>
    public static void DrawButton(SpriteBatch sb) {
        CheckOnce();
        _wasDown = _down;
        _down = InputUtils.CurrentMouseSnapshot.LeftButton == ButtonState.Pressed;
        if (_down && !_wasDown)
            _press = MouseUtils.MousePosition;
        string? text = Now switch {
            Stage.Available => $"Update to Build {LatestBuild}",
            Stage.Downloading => $"Downloading... {(int)(Progress * 100)}%",
            Stage.Installing => "Installing...",
            Stage.NeedsPermission => "Allow installs, then tap here",
            Stage.Failed => "Update failed - tap to retry",
            _ => null,
        };
        if (text is null)
            return;
        float s = WindowUtils.WindowHeight / 1080f;
        var r = new Rectangle((int)(24 * s), (int)(WindowUtils.WindowHeight - 140 * s), (int)(470 * s), (int)(84 * s));
        bool pressing = _down && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        float pulse = Now == Stage.Available ? 0.85f + 0.15f * MathF.Sin((float)DateTime.UtcNow.TimeOfDay.TotalSeconds * 4f) : 1f;
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X, r.Y + (int)(6 * s), r.Width, r.Height), 22 * s, new Color(40, 90, 30));
        ToggleSwitch.DrawRoundedRect(sb, pressing ? new Rectangle(r.X, r.Y + (int)(4 * s), r.Width, r.Height - (int)(4 * s)) : r, 22 * s,
            Color.Lerp(new Color(90, 200, 80), Color.White, pressing ? 0.3f : 0f) * pulse);
        if (Now == Stage.Downloading)
            ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X + (int)(10 * s), r.Bottom - (int)(16 * s), (int)((r.Width - 20 * s) * Progress), (int)(8 * s)), 4 * s, Color.White);
        var font = FontGlobals.RebirthFont;
        var m = font.MeasureString(text);
        float sc = Math.Min(0.62f * s, (r.Width - 30 * s) / Math.Max(1f, m.X));
        sb.DrawString(font, text, r.Center.ToVector2() - new Vector2(0, 4 * s), new Color(20, 40, 15), new Vector2(sc), 0f, m / 2f);
        bool tapped = !_down && _wasDown && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        if (tapped && Now is Stage.Available or Stage.NeedsPermission or Stage.Failed) {
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);
            Common.Framework.Input.Haptics.Tap();
            Start();
        }
    }
}
