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
    private LinearLayout? _buttons;
    // Tank Troves: your items (the ITEM-button ones to pick from, and everything you've collected)
    private LinearLayout? _troves, _activeRow, _passiveList;
    private TextView? _itemsHeader, _activeHint;
    private Button? _use;
    private readonly System.Collections.Generic.List<Button> _activeButtons = [];
    private string _itemsSig = "";
    private bool? _trovesShown;
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

        // Tank Troves: the items panel (hidden in other modes)
        _troves = new LinearLayout(ctx) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        root.AddView(_troves, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f) { TopMargin = 12 });
        _activeHint = new TextView(ctx) { TextSize = 13, Text = "ITEM BUTTON - tap one to pick it, then USE" };
        _activeHint.SetTextColor(Android.Graphics.Color.Rgb(240, 180, 60));
        _troves.AddView(_activeHint);
        var activeArea = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        _troves.AddView(activeArea, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        var hs = new HorizontalScrollView(ctx);
        _activeRow = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        hs.AddView(_activeRow);
        activeArea.AddView(hs, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        _use = new Button(ctx) { Text = "USE", TextSize = 22 };
        _use.Click += (_, _) => TanksRebirth.GameContent.Systems.Troves.TroveItems.RequestUse();
        activeArea.AddView(_use, new LinearLayout.LayoutParams(260, 170) { LeftMargin = 12 });
        _itemsHeader = new TextView(ctx) { TextSize = 13 };
        _itemsHeader.SetTextColor(Android.Graphics.Color.Rgb(240, 180, 60));
        _troves.AddView(_itemsHeader, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = 14 });
        var scroll = new ScrollView(ctx);
        _passiveList = new LinearLayout(ctx) { Orientation = Orientation.Vertical };
        scroll.AddView(_passiveList);
        _troves.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var buttons = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        _buttons = buttons;
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

            RefreshTroves(!inMenu && TanksRebirth.GameContent.Systems.Troves.TroveRun.Active, paused);

            _pause!.Text = paused ? "RESUME" : "PAUSE";
            _pause.Enabled = !inMenu;
            _mine!.Enabled = !inMenu && !paused;
        }
        catch {
            // game state not ready yet; try again on the next tick
        }
        _handler?.PostDelayed(Refresh, 250);
    }

    private static Android.Graphics.Color Col(Microsoft.Xna.Framework.Color c) => Android.Graphics.Color.Rgb(c.R, c.G, c.B);

    /// <summary>Tank Troves: show your items (and switch the layout when the mode changes).</summary>
    private void RefreshTroves(bool troves, bool paused) {
        if (_troves is null || _buttons is null || _activeRow is null || _passiveList is null)
            return;
        if (_trovesShown != troves) {
            _trovesShown = troves;
            _troves.Visibility = troves ? ViewStates.Visible : ViewStates.Gone;
            _hint!.Visibility = troves ? ViewStates.Gone : ViewStates.Visible;
            // the Pause / Mine buttons shrink to a strip at the bottom while the items show
            _buttons.LayoutParameters = troves
                ? new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 150) { TopMargin = 10 }
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f) { TopMargin = 24 };
            _pause!.TextSize = _mine!.TextSize = troves ? 18 : 26;
            _itemsSig = "";
        }
        if (!troves)
            return;
        var items = TanksRebirth.GameContent.Systems.Troves.TroveItems.Players[TanksRebirth.GameContent.Systems.Troves.TroveItems.MyPlayer];
        string sig = string.Join(",", items.Actives) + "|" + string.Join(",", items.Passives.OrderBy(k => (int)k.Key).Select(k => $"{k.Key}:{k.Value}"));
        if (sig != _itemsSig) {
            _itemsSig = sig;
            // the ITEM-button items: one big button each
            _activeRow.RemoveAllViews();
            _activeButtons.Clear();
            for (int i = 0; i < items.Actives.Count; i++) {
                int index = i;
                var b = new Button(Context) { TextSize = 15, Gravity = GravityFlags.Center };
                b.SetAllCaps(false);
                b.Click += (_, _) => TanksRebirth.GameContent.Systems.Troves.TroveItems.RequestSelect(index);
                _activeRow.AddView(b, new LinearLayout.LayoutParams(300, 170) { RightMargin = 10 });
                _activeButtons.Add(b);
            }
            if (items.Actives.Count == 0) {
                var none = new TextView(Context) { TextSize = 14, Text = "(none yet - item rooms have them)" };
                none.SetTextColor(Android.Graphics.Color.Gray);
                _activeRow.AddView(none);
            }
            // everything else you've collected, with what it does
            _passiveList.RemoveAllViews();
            foreach (var (id, count) in items.Passives.OrderBy(k => (int)k.Key)) {
                var def = TanksRebirth.GameContent.Systems.Troves.TroveItems.Def(id);
                var name = new TextView(Context) { TextSize = 17, Text = count > 1 ? $"{def.Name}  x{count}" : def.Name };
                name.SetTextColor(Col(Microsoft.Xna.Framework.Color.Lerp(def.Color, Microsoft.Xna.Framework.Color.White, 0.25f)));
                name.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
                _passiveList.AddView(name, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = 10 });
                var desc = new TextView(Context) { TextSize = 13, Text = def.Description };
                desc.SetTextColor(Android.Graphics.Color.Rgb(200, 192, 180));
                _passiveList.AddView(desc);
            }
            if (items.Passives.Count == 0) {
                var none = new TextView(Context) { TextSize = 14, Text = "Nothing collected yet." };
                none.SetTextColor(Android.Graphics.Color.Gray);
                _passiveList.AddView(none);
            }
            int total = items.Passives.Values.Sum();
            _itemsHeader!.Text = $"COLLECTED ({total})" + (items.Shields > 0 ? $"   -   shields: {items.Shields}" : "");
        }
        // the ITEM-button items' state: which is picked, uses left, charging or ready
        for (int i = 0; i < _activeButtons.Count && i < items.Actives.Count; i++) {
            var id = items.Actives[i];
            var def = TanksRebirth.GameContent.Systems.Troves.TroveItems.Def(id);
            bool ready = items.IsReady(id);
            string state = ready ? "READY" : def.RechargeSeconds > 0 ? "recharging..." : $"charging {items.ChargeOf(id)}/{def.Recharge}";
            bool picked = i == Math.Clamp(items.Selected, 0, items.Actives.Count - 1);
            _activeButtons[i].Text = $"{(picked ? "> " : "")}{def.Name}{(picked ? " <" : "")}\n{state}  -  {items.UsesOf(id)} use{(items.UsesOf(id) == 1 ? "" : "s")} left";
            _activeButtons[i].Background?.SetTint(picked ? Android.Graphics.Color.Rgb(255, 214, 120) : Android.Graphics.Color.Rgb(225, 218, 205));
        }
        bool canUse = items.ActiveReady && !paused && TanksRebirth.GameContent.Globals.CampaignGlobals.InMission;
        _use!.Enabled = canUse;
        _use.Text = items.Active is null ? "USE" : canUse ? "USE" : "...";
        _itemsHeader!.Text = $"COLLECTED ({items.Passives.Values.Sum()})" + (items.Shields > 0 ? $"   -   shields: {items.Shields}" : "");
    }

    public override void OnDetachedFromWindow() {
        _running = false;
        TouchControls.SecondScreenActive = false;
        base.OnDetachedFromWindow();
    }
}
