using System;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using Android.Widget;
using TanksRebirth.Internals.Common.Framework.Input;

namespace TanksRebirth.Droid;

/// <summary>
/// A control panel shown on a second display, such as the AYN Thor's lower screen: big Pause and Mine buttons
/// plus the current mission, lives, and enemies left. Uses Android's Presentation API, which gives us a
/// separate window (with its own touch input) on the other display.
/// </summary>
internal sealed class SecondScreenPanel : Presentation {
    private TextView? _mission, _lives, _enemies, _hint;
    private Button? _pause, _mine;
    private Handler? _handler;
    private bool _running;

    private SecondScreenPanel(Context context, Display display) : base(context, display) { }

    /// <summary>Shows the panel on the first secondary display, if the device has one. Returns null otherwise.</summary>
    public static SecondScreenPanel? TryShow(Activity activity) {
        try {
            var dm = (DisplayManager?)activity.GetSystemService(Context.DisplayService);
            if (dm is null)
                return null;
            var display = dm.GetDisplays(DisplayManager.DisplayCategoryPresentation)?.FirstOrDefault()
                ?? dm.GetDisplays()?.FirstOrDefault(d => d.DisplayId != Display.DefaultDisplay);
            if (display is null)
                return null;

            var panel = new SecondScreenPanel(activity, display);
            panel.Show();
            TouchControls.SecondScreenActive = true;
            return panel;
        }
        catch (Exception e) {
            Android.Util.Log.Warn("TanksRebirth", "No second-screen panel: " + e.Message);
            TouchControls.SecondScreenActive = false;
            return null;
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState) {
        base.OnCreate(savedInstanceState);
        var ctx = Context;

        var root = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
        root.SetGravity(GravityFlags.Center);
        root.SetBackgroundColor(Android.Graphics.Color.Rgb(30, 24, 18));
        root.SetPadding(40, 32, 40, 32);

        TextView Label(float size, Android.Graphics.Color color) {
            var t = new TextView(ctx) { TextSize = size, Gravity = GravityFlags.Center };
            t.SetTextColor(color);
            root.AddView(t, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            return t;
        }

        var title = Label(16, Android.Graphics.Color.Rgb(240, 180, 60));
        title.Text = "TANKS! REBIRTH";
        _mission = Label(24, Android.Graphics.Color.White);
        var stats = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        stats.SetGravity(GravityFlags.Center);
        root.AddView(stats, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        _lives = new TextView(ctx) { TextSize = 20, Gravity = GravityFlags.Center };
        _lives.SetTextColor(Android.Graphics.Color.Rgb(120, 170, 255));
        _enemies = new TextView(ctx) { TextSize = 20, Gravity = GravityFlags.Center };
        _enemies.SetTextColor(Android.Graphics.Color.Rgb(255, 140, 120));
        stats.AddView(_lives, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        stats.AddView(_enemies, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));

        var buttons = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        var bp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f) { TopMargin = 24 };
        root.AddView(buttons, bp);
        _pause = new Button(ctx) { Text = "PAUSE", TextSize = 26 };
        _pause.Click += (_, _) => TouchControls.RequestPause();
        _mine = new Button(ctx) { Text = "MINE", TextSize = 26 };
        _mine.Click += (_, _) => TouchControls.RequestMine();
        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f) { LeftMargin = 12, RightMargin = 12 };
        buttons.AddView(_pause, lp);
        buttons.AddView(_mine, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f) { LeftMargin = 12, RightMargin = 12 });

        _hint = Label(12, Android.Graphics.Color.Gray);
        _hint.Text = "Controller: left stick drive · right stick aim · RB shoot · LB mine · Start pause";

        SetContentView(root);

        _running = true;
        _handler = new Handler(Looper.MainLooper!);
        _handler.Post(Refresh);
    }

    // Runs on the UI thread, the same thread as the game loop, so reading game state here is safe.
    private void Refresh() {
        if (!_running)
            return;
        try {
            bool inMenu = TanksRebirth.GameContent.UI.MainMenu.MainMenuUI.IsActive;
            bool paused = TanksRebirth.GameContent.UI.GameUI.Paused;

            if (inMenu) {
                _mission!.Text = "Main menu";
                _lives!.Text = "";
                _enemies!.Text = "";
            }
            else {
                var campaign = TanksRebirth.GameContent.Globals.CampaignGlobals.LoadedCampaign;
                var name = campaign?.CurrentMission.Name;
                var number = (campaign?.CurrentMissionId ?? 0) + 1;
                _mission!.Text = string.IsNullOrWhiteSpace(name) ? $"Mission {number}" : $"Mission {number}: {name}";
                _lives!.Text = $"Lives: {TanksRebirth.GameContent.PlayerTank.GetMyLives()}";
                _enemies!.Text = $"Enemies: {TanksRebirth.GameContent.Systems.AI.AIManager.CountAll()}";
            }

            _pause!.Text = paused ? "RESUME" : "PAUSE";
            _pause.Enabled = !inMenu;
            _mine!.Enabled = !inMenu && !paused;
        }
        catch {
            // game state not ready yet; try again on the next tick
        }
        _handler?.PostDelayed(Refresh, 250);
    }

    public override void OnDetachedFromWindow() {
        _running = false;
        TouchControls.SecondScreenActive = false;
        base.OnDetachedFromWindow();
    }
}
