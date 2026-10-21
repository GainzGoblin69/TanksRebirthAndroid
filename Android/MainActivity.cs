using System;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
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
                         | ConfigChanges.UiMode | ConfigChanges.Navigation)]
public class MainActivity : AndroidGameActivity {
    private TankGame? _game;
    private TextView? _status;

    protected override void OnCreate(Bundle? bundle) {
        base.OnCreate(bundle);
        HideSystemUi();

        // Shown while the game files are unpacked (a few seconds on first launch only).
        _status = new TextView(this) {
            Text = "Preparing game files…",
            Gravity = GravityFlags.Center,
            TextSize = 22,
        };
        _status.SetTextColor(Android.Graphics.Color.White);
        _status.SetBackgroundColor(Android.Graphics.Color.Black);
        SetContentView(_status);

        var internalDir = FilesDir!.AbsolutePath;
        var gameDir = Path.Combine(internalDir, "game");
        // Saves, logs and campaigns go to the app's external folder when there is one
        // (Android/data/com.tanksrebirth.unofficial/files), so you can drop custom .campaign files in.
        var saveDir = Path.Combine(GetExternalFilesDir(null)?.AbsolutePath ?? internalDir, "Tanks Rebirth");

        Task.Run(() => {
            try {
                AssetExtractor.ExtractIfNeeded(this, gameDir, saveDir,
                    msg => RunOnUiThread(() => { if (_status != null) _status.Text = msg; }));
                RunOnUiThread(() => StartGame(gameDir, saveDir));
            }
            catch (Exception e) {
                RunOnUiThread(() => { if (_status != null) _status.Text = "Couldn't unpack the game files:\n" + e.Message; });
            }
        });
    }

    private void StartGame(string gameDir, string saveDir) {
        if (_game != null)
            return;

        // The game opens its files relative to the working directory, and keeps saves in TANKSREBIRTH_SAVE_DIR.
        Directory.SetCurrentDirectory(gameDir);
        System.Environment.SetEnvironmentVariable("TANKSREBIRTH_SAVE_DIR", saveDir);
        Directory.CreateDirectory(saveDir);

        TouchControls.Enabled = true;

        _game = new TankGame();
        var view = (View)_game.Services.GetService(typeof(View))!;
        SetContentView(view);
        _status = null;
        _game.Run();
    }

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
