using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Microsoft.Xna.Framework;
using TanksRebirth.Internals.Common.Framework.Input;

namespace TanksRebirth.Droid;

[Activity(
    Label = "Tanks! Rebirth",
    MainLauncher = true,
    Icon = "@drawable/icon",
    Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    AlwaysRetainTaskState = true,
    LaunchMode = LaunchMode.SingleInstance,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden
                         | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize
                         | ConfigChanges.UiMode | ConfigChanges.Navigation | ConfigChanges.Density
                         | ConfigChanges.FontScale | ConfigChanges.Locale | ConfigChanges.LayoutDirection
                         | ConfigChanges.Touchscreen | ConfigChanges.Mcc | ConfigChanges.Mnc | ConfigChanges.ColorMode)]
public class MainActivity : AndroidGameActivity {
    // Process-wide launch state. Android can replace this activity with a new instance (e.g. on a configuration
    // change) while files are still unpacking on a background thread; the new instance must pick that up rather
    // than treat it as a failed launch.
    private static MainActivity? s_current;
    private static bool s_unpacking;

    private TankGame? _game;
    private SecondScreenPanel? _panel;
    private TextView? _status;
    private TextView? _overlay;
    private Handler? _handler;
    private int _frames;
    private DateTime? _firstFrameAt;

    private string _gameDir = "";
    private string _saveDir = "";

    // Diagnostics: a launch writes this marker and removes it once the first frame is drawn.
    // If it is still there on the next launch, that launch never got going, so we show what happened.
    private string MarkerPath => Path.Combine(_saveDir, "launch_in_progress");
    private string CrashPath => Path.Combine(_saveDir, "crash.txt");
    private string StartupPath => Path.Combine(_saveDir, "startup.txt");

    protected override void OnCreate(Bundle? bundle) {
        base.OnCreate(bundle);
        HideSystemUi();

        var internalDir = FilesDir!.AbsolutePath;
        _gameDir = Path.Combine(internalDir, "game");
        // Saves, logs and campaigns go to the app's external folder when there is one
        // (Android/data/com.tanksrebirth.unofficial/files), so you can drop custom .campaign files in.
        _saveDir = Path.Combine(GetExternalFilesDir(null)?.AbsolutePath ?? internalDir, "Tanks Rebirth");
        Directory.CreateDirectory(_saveDir);

        HookCrashLogging();
        s_current = this;

        if (s_unpacking) {
            // a previous instance of this activity is still unpacking; it will start the game on this one
            ShowStatus("Preparing game files…");
            return;
        }

        if (File.Exists(MarkerPath)) {
            ShowReport();
            return;
        }

        BeginLaunch();
    }

    private void BeginLaunch() {
        File.WriteAllText(MarkerPath, DateTime.Now.ToString("O"));
        File.WriteAllText(StartupPath, "");
        Breadcrumb("launch started");

        // Usual case: files already unpacked, so start the game right here in OnCreate. MonoGame then gets the
        // activity's normal OnResume, which is what activates the game and starts its render loop.
        if (AssetExtractor.IsUpToDate(this, _gameDir)) {
            try {
                AssetExtractor.ExtractIfNeeded(this, _gameDir, _saveDir); // only seeds the campaign if missing
            }
            catch (Exception e) {
                LogCrash("checking game files", e);
            }
            Breadcrumb("game files already unpacked");
            StartGame();
            return;
        }

        // Shown while the game files are unpacked (a few seconds on first launch only).
        ShowStatus("Preparing game files…");

        s_unpacking = true;
        var context = ApplicationContext!;
        string gameDir = _gameDir, saveDir = _saveDir;
        Task.Run(() => {
            try {
                AssetExtractor.ExtractIfNeeded(context, gameDir, saveDir,
                    msg => OnCurrent(a => { if (a._status != null) a._status.Text = msg; }));
                Breadcrumb("game files ready");
                s_unpacking = false;
                OnCurrent(a => a.StartGame()); // whichever activity instance is current by now
            }
            catch (Exception e) {
                s_unpacking = false;
                LogCrash("unpacking game files", e);
                OnCurrent(a => a.ShowReport());
            }
        });
    }

    private static void OnCurrent(Action<MainActivity> action) {
        var a = s_current;
        a?.RunOnUiThread(() => action(a));
    }

    private void ShowStatus(string text) {
        _status = new TextView(this) {
            Text = text,
            Gravity = GravityFlags.Center,
            TextSize = 22,
        };
        _status.SetTextColor(Android.Graphics.Color.White);
        _status.SetBackgroundColor(Android.Graphics.Color.Black);
        SetContentView(_status);
    }

    private void StartGame() {
        if (_game != null)
            return;

        try {
            // The game opens its files relative to the working directory, and keeps saves in TANKSREBIRTH_SAVE_DIR.
            Directory.SetCurrentDirectory(_gameDir);
            System.Environment.SetEnvironmentVariable("TANKSREBIRTH_SAVE_DIR", _saveDir);

            TouchControls.Enabled = true;

            Breadcrumb("creating game");
            _game = new TankGame();
            TankGame.PostDrawEverything += OnFirstFrame;
            TankGame.PostDrawEverything += CountFrame;

            var view = (View)_game.Services.GetService(typeof(View))!;

            // game view with a small status readout on top for the first seconds (shows where startup is)
            var frame = new FrameLayout(this);
            frame.AddView(view);
            _overlay = new TextView(this) {
                TextSize = 10,
                Typeface = Android.Graphics.Typeface.Monospace,
            };
            _overlay.SetTextColor(Android.Graphics.Color.Yellow);
            _overlay.SetBackgroundColor(Android.Graphics.Color.Argb(170, 0, 0, 0));
            _overlay.SetPadding(12, 8, 12, 8);
            frame.AddView(_overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            SetContentView(frame);
            _status = null;

            _handler = new Handler(Looper.MainLooper!);
            _handler.Post(UpdateOverlay);

            Breadcrumb("starting game loop");
            _game.Run();

            // After a first-launch unpack the game is created after this activity already resumed, so MonoGame
            // missed the resume event that marks the game active and starts its render loop (otherwise: black
            // screen forever). Deliver that event now.
            if (_resumed)
                DeliverMissedResume();

            // devices with a second display (e.g. the AYN Thor) get pause/mine buttons and stats there
            _panel ??= SecondScreenPanel.TryShow(this);
            Breadcrumb(_panel != null ? "second-screen panel shown" : "no second screen");
        }
        catch (Exception e) {
            LogCrash("starting the game", e);
            ShowReport();
        }
    }

    private bool _resumed;

    protected override void OnResume() {
        base.OnResume();
        _resumed = true;
    }

    protected override void OnDestroy() {
        try { _panel?.Dismiss(); } catch { }
        _panel = null;
        base.OnDestroy();
    }

    protected override void OnPause() {
        _resumed = false;
        base.OnPause();
    }

    private void DeliverMissedResume() {
        try {
            var field = typeof(AndroidGameActivity).GetField("Resumed",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field?.GetValue(null) is EventHandler handler) {
                handler(this, EventArgs.Empty);
                Breadcrumb("delivered resume to MonoGame");
            }
            else {
                // couldn't reach it: restart the activity; the files are unpacked now, so it takes the normal path
                Breadcrumb("resume event not found, restarting activity");
                try { File.Delete(MarkerPath); } catch { }
                Recreate();
            }
        }
        catch (Exception e) {
            LogCrash("delivering resume", e);
        }
    }

    private void CountFrame(GameTime _) => _frames++;

    private void UpdateOverlay() {
        if (_overlay is null)
            return;

        bool crashed = File.Exists(CrashPath);
        // hide 8 seconds after the first frame, unless something went wrong
        if (_firstFrameAt is { } t && (DateTime.Now - t).TotalSeconds > 8 && !crashed) {
            _overlay.Visibility = ViewStates.Gone;
            TankGame.PostDrawEverything -= CountFrame;
            return;
        }

        string newestLog = "(no game log yet)";
        try {
            var logs = new DirectoryInfo(Path.Combine(_saveDir, "Logs"));
            var newest = logs.Exists ? logs.GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
            if (newest != null) newestLog = Tail(newest.FullName, 8);
        }
        catch { }

        _overlay.Text = $"frames drawn: {_frames}\n" + Tail(StartupPath, 3) + "\n" + newestLog
            + (crashed ? "\n--- ERROR ---\n" + Tail(CrashPath, 15) : "");
        _handler?.PostDelayed(UpdateOverlay, 500);
    }

    private void OnFirstFrame(GameTime _) {
        TankGame.PostDrawEverything -= OnFirstFrame;
        _firstFrameAt = DateTime.Now;
        Breadcrumb("first frame drawn");
        try { File.Delete(MarkerPath); } catch { }
    }

    // ---------- diagnostics ----------

    private void HookCrashLogging() {
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => LogCrash("unhandled (Android)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash("background task", e.Exception);
    }

    private void LogCrash(string where, Exception? e) {
        Android.Util.Log.Error("TanksRebirth", $"{where}: {e}");
        try {
            File.AppendAllText(CrashPath, $"[{DateTime.Now:HH:mm:ss}] {where}:\n{e}\n\n");
        }
        catch { }
    }

    private void Breadcrumb(string what) {
        Android.Util.Log.Info("TanksRebirth", what);
        try {
            File.AppendAllText(StartupPath, $"[{DateTime.Now:HH:mm:ss.fff}] {what}\n");
        }
        catch { }
    }

    private static string Tail(string path, int lines) {
        try {
            if (!File.Exists(path))
                return "(none)";
            // the game keeps its log open for writing, so read with sharing allowed
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            var all = reader.ReadToEnd().Split('\n');
            return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
        }
        catch (Exception e) {
            return "(couldn't read: " + e.Message + ")";
        }
    }

    private string BuildReport() {
        var sb = new StringBuilder();
        sb.AppendLine("TANKS REBIRTH ANDROID — STARTUP REPORT");
        sb.AppendLine($"Device: {Build.Manufacturer} {Build.Model}, Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt})");
        try {
            sb.AppendLine($"Page size: {Android.Systems.Os.Sysconf(Android.Systems.OsConstants.ScPagesize)}, ABIs: {string.Join(",", Build.SupportedAbis ?? [])}");
        }
        catch { }
        sb.AppendLine();
        sb.AppendLine("== Launcher steps ==");
        sb.AppendLine(Tail(StartupPath, 30));
        sb.AppendLine();
        sb.AppendLine("== Crashes ==");
        sb.AppendLine(Tail(CrashPath, 120));
        sb.AppendLine();
        sb.AppendLine("== Game log (last lines) ==");
        try {
            var logs = new DirectoryInfo(Path.Combine(_saveDir, "Logs"));
            var newest = logs.Exists ? logs.GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault() : null;
            sb.AppendLine(newest is null ? "(no game log)" : newest.Name + "\n" + Tail(newest.FullName, 40));
        }
        catch (Exception e) {
            sb.AppendLine("(couldn't read logs: " + e.Message + ")");
        }
        return sb.ToString();
    }

    private void ShowReport() {
        var report = BuildReport();

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetBackgroundColor(Android.Graphics.Color.Black);
        layout.SetPadding(24, 24, 24, 24);

        var title = new TextView(this) {
            Text = "The game didn't start last time. Tap Copy and paste this into the chat (or screenshot it), then Try again.",
            TextSize = 15,
        };
        title.SetTextColor(Android.Graphics.Color.White);
        layout.AddView(title);

        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var copy = new Button(this) { Text = "Copy" };
        copy.Click += (_, _) => {
            var clipboard = (ClipboardManager?)GetSystemService(Android.Content.Context.ClipboardService);
            if (clipboard != null) {
                clipboard.PrimaryClip = ClipData.NewPlainText("Tanks Rebirth report", report);
                Toast.MakeText(this, "Copied", ToastLength.Short)?.Show();
            }
        };
        var retry = new Button(this) { Text = "Try again" };
        retry.Click += (_, _) => {
            try { File.Delete(MarkerPath); File.Delete(CrashPath); } catch { }
            BeginLaunch();
        };
        buttons.AddView(copy);
        buttons.AddView(retry);
        layout.AddView(buttons);

        var body = new TextView(this) {
            Text = report,
            TextSize = 11,
            Typeface = Android.Graphics.Typeface.Monospace,
        };
        body.SetTextColor(Android.Graphics.Color.LightGray);
        body.SetTextIsSelectable(true);
        var scroll = new ScrollView(this);
        scroll.AddView(body);
        layout.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        SetContentView(layout);
    }

    // ---------- fullscreen ----------

    public override void OnWindowFocusChanged(bool hasFocus) {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            HideSystemUi();
    }

    private void HideSystemUi() {
        if (Window is null)
            return;
#pragma warning disable CA1422, CS0618 // the immersive flags still work everywhere and avoid a version split
        Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
            SystemUiFlags.ImmersiveSticky | SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation
            | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable);
#pragma warning restore CA1422, CS0618
        if (Build.VERSION.SdkInt >= BuildVersionCodes.P && Window.Attributes is { } attrs) {
            // draw under the camera notch too
            attrs.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
            Window.Attributes = attrs;
        }
    }
}
