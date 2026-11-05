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
        (string Title, string Sub)? text = Now switch {
            Stage.Available => ("Update available", $"Tap to install Build {LatestBuild}"),
            Stage.Downloading => ($"Downloading  {(int)(Progress * 100)}%", $"Build {LatestBuild}"),
            Stage.Installing => ("Installing...", "Android will ask to confirm"),
            Stage.NeedsPermission => ("Allow installs", "Then come back and tap here"),
            Stage.Failed => ("Update failed", "Tap to try again"),
            _ => null,
        };
        if (text is null)
            return;
        float s = WindowUtils.WindowHeight / 1080f;
        var r = new Rectangle((int)(28 * s), (int)(WindowUtils.WindowHeight - 150 * s), (int)(500 * s), (int)(104 * s));
        bool pressing = _down && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        bool failed = Now == Stage.Failed;
        var accent = failed ? new Color(232, 84, 72) : new Color(64, 196, 104);
        float t = (float)DateTime.UtcNow.TimeOfDay.TotalSeconds;
        float radius = r.Height / 2f;
        var body = pressing ? new Rectangle(r.X, r.Y + (int)(3 * s), r.Width, r.Height) : r;

        // (the menu's batch is premultiplied; the rounded shapes are straight alpha)
        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        // soft shadow, then a dark card with a colored edge
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X + (int)(2 * s), r.Y + (int)(10 * s), r.Width, r.Height), radius, new Color(0, 0, 0, 70));
        if (Now == Stage.Available) {
            // a gentle glow breathing around it
            float g = 0.5f + 0.5f * MathF.Sin(t * 3f);
            int grow = (int)((4 + 6 * g) * s);
            ToggleSwitch.DrawRoundedRect(sb, new Rectangle(body.X - grow, body.Y - grow, body.Width + grow * 2, body.Height + grow * 2), radius + grow, accent * (0.18f + 0.12f * g));
        }
        ToggleSwitch.DrawRoundedRect(sb, body, radius, accent);
        int edge = (int)MathF.Max(2, 3 * s);
        var inner = new Rectangle(body.X + edge, body.Y + edge, body.Width - edge * 2, body.Height - edge * 2);
        ToggleSwitch.DrawRoundedRect(sb, inner, radius - edge, pressing ? new Color(44, 48, 58) : new Color(30, 33, 42));
        if (Now == Stage.Downloading && Progress > 0) {
            // the card fills up as the download goes
            int w = Math.Max(inner.Height, (int)(inner.Width * Progress));
            ToggleSwitch.DrawRoundedRect(sb, new Rectangle(inner.X, inner.Y, w, inner.Height), radius - edge, accent * 0.35f);
        }

        // icon: a round badge with a down arrow (or a spinner while installing)
        float iconR = inner.Height * 0.34f;
        var ic = new Vector2(inner.X + inner.Height * 0.5f, inner.Center.Y);
        ToggleSwitch.DrawDisc(sb, ic, iconR, accent);
        float th = MathF.Max(2.5f, iconR * 0.2f);
        if (Now == Stage.Installing) {
            for (int i = 0; i < 8; i++) {
                float ang = t * 6f + i * MathHelper.TwoPi / 8f;
                var d = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                ToggleSwitch.DrawDisc(sb, ic + d * iconR * 0.55f, th * 0.6f, Color.White * (0.25f + 0.75f * i / 7f));
            }
        }
        else if (failed) {
            ToggleSwitch.DrawLine(sb, ic + new Vector2(0, -iconR * 0.5f), ic + new Vector2(0, iconR * 0.15f), th, Color.White);
            ToggleSwitch.DrawDisc(sb, ic + new Vector2(0, iconR * 0.45f), th * 0.6f, Color.White);
        }
        else {
            float bob = Now == Stage.Downloading ? MathF.Sin(t * 8f) * iconR * 0.08f : 0f;
            var top = ic + new Vector2(0, -iconR * 0.5f + bob);
            var tip = ic + new Vector2(0, iconR * 0.42f + bob);
            ToggleSwitch.DrawLine(sb, top, tip, th, Color.White);
            ToggleSwitch.DrawLine(sb, tip + new Vector2(-iconR * 0.38f, -iconR * 0.38f), tip + new Vector2(th * 0.3f, th * 0.3f), th, Color.White);
            ToggleSwitch.DrawLine(sb, tip + new Vector2(iconR * 0.38f, -iconR * 0.38f), tip + new Vector2(-th * 0.3f, th * 0.3f), th, Color.White);
        }

        sb.End();
        sb.Begin();

        // title and subtitle
        var font = FontGlobals.RebirthFont;
        float textX = ic.X + iconR + 22 * s, maxW = inner.Right - textX - 24 * s;
        var mt = font.MeasureString(text.Value.Title);
        float st = Math.Min(0.6f * s, maxW / Math.Max(1f, mt.X));
        sb.DrawString(font, text.Value.Title, new Vector2(textX, inner.Center.Y - 14 * s), Color.White, new Vector2(st), 0f, new Vector2(0, mt.Y / 2f));
        var ms = font.MeasureString(text.Value.Sub);
        float ss = Math.Min(0.42f * s, maxW / Math.Max(1f, ms.X));
        sb.DrawString(font, text.Value.Sub, new Vector2(textX, inner.Center.Y + 20 * s), new Color(170, 178, 190), new Vector2(ss), 0f, new Vector2(0, ms.Y / 2f));
        bool tapped = !_down && _wasDown && r.Contains(_press) && r.Contains(MouseUtils.MousePosition);
        if (tapped && Now is Stage.Available or Stage.NeedsPermission or Stage.Failed) {
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);
            Common.Framework.Input.Haptics.Tap();
            Start();
        }
    }
}
