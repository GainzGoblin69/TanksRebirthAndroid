using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.Systems.TankSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals.UI;
using static StbVorbisSharp.StbVorbis;

namespace TanksRebirth.GameContent.UI;

public static class GraphicsUI {
    // Explicit static constructor: makes the static fields above/below initialize at first use (as on desktop).
    // Without it, Android's Mono runtime may run them early, before the fonts/textures/settings they read exist.
    static GraphicsUI() { }
    public static UITextButton PerPixelLightingButton;

    public static UIImage PerPixelLightingToggle;

    public static UITextButton VsyncButton;

    public static UIImage VsyncToggle;

    public static UITextButton FullScreenButton;

    public static UITextButton ResolutionButton;

    /// <summary>Team colours on/off (from 1.9).</summary>
    public static UITextButton TeamColoursButton;

    /// <summary>Dynamic lighting on/off (from 1.9).</summary>
    public static UITextButton LightingButton;

    /// <summary>Phones: how much around the board gets dynamic lighting.</summary>
    public static UITextButton LitSurroundingsButton;
    public static UITextButton BrightnessButton;
    private static bool _initialized;

    private static int _idxPair;

    public static KeyValuePair<int, int> CurrentRes = new(TankGame.Settings.ResWidth, TankGame.Settings.ResHeight);

    private static KeyValuePair<int, int>[] CommonResolutions = new KeyValuePair<int, int>[]
    {
        new(640, 480),
        new(1280, 720),
        new(1920, 1080),
        new(2560, 1440),
        new(2048, 1080),
        new(3840, 2160),
        new(7680, 4320)
    };

    public static bool BatchVisible { get; set; }

    public static void DrawBooleanIndicator(SpriteBatch spriteBatch, Rectangle hitbox, bool active) {
        spriteBatch.Draw(TextureGlobals.Pixels[Color.White], hitbox, active ? Color.Green : Color.Red);
    }

    // ---- on/off settings: the whole button is the switch (green = on, grey = off)
    public static readonly Color OnFill = new(124, 212, 118);
    public static readonly Color OffFill = new(204, 198, 188);

    private static void ToggleLook(UITextButton button, string name, Func<bool> isOn) {
        void Apply(UITextButton b) {
            bool on = isOn();
            b.Color = b.HoverColor = on ? OnFill : OffFill;
            b.Text = $"{name}: {(on ? "On" : "Off")}";
            b.TextOffset = Vector2.Zero;
            b.TextPadding = 0;
        }
        Apply(button);
        button.UniqueDraw = (elem, _) => Apply((UITextButton)elem);
    }

    // ---- number settings: - on the left, + on the right
    private static void Stepper(UITextButton button, Func<string> label, Action<int> step, Func<bool> atMin, Func<bool> atMax) {
        button.Text = label();
        button.OnLeftClick = (elem) => {
            var hb = elem.Hitbox;
            float x = MouseUtils.MousePosition.X;
            float zone = hb.Height * 1.0f;
            if (x < hb.X + zone && !atMin()) step(-1);
            else if (x > hb.Right - zone && !atMax()) step(1);
        };
        button.UniqueDraw = (elem, sb) => {
            var b = (UITextButton)elem;
            b.Text = label();
            b.TextOffset = Vector2.Zero;
            var hb = b.Hitbox;
            int lip = Math.Max(3, (int)(hb.Height * 0.07f));
            int size = (int)(hb.Height * 0.56f);
            int inset = (int)(hb.Height * 0.16f);
            b.TextPadding = size + inset;
            int cy = hb.Y + (hb.Height - lip) / 2;
            DrawStepKey(sb, new Rectangle(hb.X + inset, cy - size / 2, size, size), false, atMin());
            DrawStepKey(sb, new Rectangle(hb.Right - inset - size, cy - size / 2, size, size), true, atMax());
        };
    }

    private static void DrawStepKey(SpriteBatch sb, Rectangle r, bool plus, bool disabled) {
        var fill = disabled ? new Color(226, 220, 210) : new Color(255, 206, 84);
        var ink = disabled ? new Color(170, 160, 150) : new Color(40, 30, 22);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(r.X, r.Y + 3, r.Width, r.Height), r.Height * 0.3f, new Color(120, 96, 70));
        ToggleSwitch.DrawRoundedRect(sb, r, r.Height * 0.3f, fill);
        var c = r.Center.ToVector2();
        float arm = r.Width * 0.26f, thick = MathF.Max(3f, r.Width * 0.11f);
        ToggleSwitch.DrawLine(sb, c - new Vector2(arm, 0), c + new Vector2(arm, 0), thick, ink);
        if (plus)
            ToggleSwitch.DrawLine(sb, c - new Vector2(0, arm), c + new Vector2(0, arm), thick, ink);
    }

    private static void SetLighting(int level) {
        TankGame.Settings.DynamicLighting = level >= 0;
        if (level >= 0)
            TankGame.Settings.LightingQuality = level;
        try { TanksRebirth.Graphics.DynamicLighting.LightManager.SetEnabled(TankGame.Settings.DynamicLighting); }
        catch (Exception e) {
            TankGame.Settings.DynamicLighting = false;
            TankGame.ClientLog.Write($"[Lighting] couldn't turn on: {e.Message}", Internals.LogType.Warn);
        }
        if (TankGame.Settings.DynamicLighting && !TanksRebirth.Graphics.DynamicLighting.LightingSystem.IsAvailable) {
            TankGame.Settings.DynamicLighting = false; // this phone can't run it
            ChatSystem.SendMessage("Dynamic lighting isn't available on this device.", Color.Orange);
        }
        PlayerColors.SaveSettings();
    }

    public static void Initialize() {
        if (_initialized) {
            foreach (var field in typeof(GraphicsUI).GetFields()) {
                if (field.GetValue(null) is UIElement) {
                    ((UIElement)field.GetValue(null)).Remove();
                    field.SetValue(null, null);
                }
            }
        }
        _initialized = true;
        //Per-Pixel Lighting
        PerPixelLightingToggle = new(null, new(1), (uiImage, spriteBatch) => DrawBooleanIndicator(spriteBatch, uiImage.Hitbox, TankGame.Settings.PerPixelLighting)) {
            IsVisible = false,
            IgnoreMouseInteractions = true
        };
        PerPixelLightingToggle.SetDimensions(() => new Vector2(695, 95).ToResolution(), () => new Vector2(510, 160).ToResolution());

        PerPixelLightingButton = new(TankGame.GameLanguage.PerPxLight, FontGlobals.RebirthFont, Color.WhiteSmoke) {
            IsVisible = false,
            Tooltip = TankGame.GameLanguage.PerPxLightDesc
        };
        PerPixelLightingButton.SetDimensions(() => new Vector2(700, 100).ToResolution(), () => new Vector2(500, 150).ToResolution());
        PerPixelLightingButton.OnLeftClick = (uiElement) => {
            TankGame.Settings.PerPixelLighting = !TankGame.Settings.PerPixelLighting;
        };

        //Vsync
        VsyncToggle = new(null, new(1), (uiImage, spriteBatch) => DrawBooleanIndicator(spriteBatch, uiImage.Hitbox, TankGame.Settings.Vsync)) {
            IsVisible = false,
            IgnoreMouseInteractions = true
        };
        VsyncToggle.SetDimensions(() => new Vector2(695, 345).ToResolution(), () => new Vector2(510, 160).ToResolution());

        VsyncButton = new(TankGame.GameLanguage.VSync, FontGlobals.RebirthFont, Color.WhiteSmoke) {
            IsVisible = false,
            Tooltip = TankGame.GameLanguage.VSyncDesc
        };
        VsyncButton.SetDimensions(() => new Vector2(700, 350).ToResolution(), () => new Vector2(500, 150).ToResolution());
        VsyncButton.OnLeftClick = (uiElement) => {
            TankGame.Instance.Graphics.SynchronizeWithVerticalRetrace = TankGame.Settings.Vsync = !TankGame.Settings.Vsync;
            TankGame.Instance.Graphics.ApplyChanges();
        };

        FullScreenButton = new($"{TankGame.GameLanguage.WindowKind}: {StringUtils.SplitByCamel(Enum.GetName(TankGame.Settings.WindowKind)!)}", FontGlobals.RebirthFont, Color.WhiteSmoke) {
            IsVisible = false,
            Tooltip = TankGame.GameLanguage.WindowKindDesc
        };
        FullScreenButton.SetDimensions(() => new Vector2(700, 600).ToResolution(), () => new Vector2(500, 150).ToResolution());
        if (Internals.Common.Framework.Input.TouchControls.Enabled) {
            // phones always sync to the display, so the VSync slot becomes the debug readout setting
            VsyncButton.Text = "Show Debug Info";
            VsyncButton.Tooltip = "The yellow readout (frames drawn, game log) over the game.";
            VsyncButton.HoverColor = Color.White;
            VsyncButton.Color = Color.WhiteSmoke;
            VsyncButton.OnLeftClick = (_) => {
                TankGame.Settings.ShowStartupInfo = !TankGame.Settings.ShowStartupInfo;
                PlayerColors.SaveSettings();
            };
            ToggleLook(VsyncButton, "Debug Info", () => TankGame.Settings.ShowStartupInfo);
            // phones are always fullscreen at native resolution, so this slot becomes the aim dots setting
            FullScreenButton.Text = "Always Show Aim Dots";
            FullScreenButton.Tooltip = "Draw the dotted line from your tank to the aim cursor at any distance.";
            FullScreenButton.HoverColor = Color.White;
            FullScreenButton.Color = Color.WhiteSmoke;
            FullScreenButton.OnLeftClick = (_) => {
                TankGame.Settings.AlwaysShowAimDots = !TankGame.Settings.AlwaysShowAimDots;
                PlayerColors.SaveSettings();
            };
            ToggleLook(FullScreenButton, "Aim Dots", () => TankGame.Settings.AlwaysShowAimDots);
        }
        else FullScreenButton.OnLeftClick = (uiElement) => {
            if (TankGame.Settings.WindowKind == WindowKind.Fullscreen) {
                TankGame.Instance.Graphics.PreferredBackBufferHeight -= 50;
            }
            else {
                TankGame.Instance.Graphics.PreferredBackBufferHeight += 50;
            }
            TankGame.Settings.WindowKind++;
            if (TankGame.Settings.WindowKind > WindowKind.FullscreenBorderless) {
                TankGame.Settings.WindowKind = WindowKind.Windowed;
            }

            FullScreenButton.Text = $"{TankGame.GameLanguage.WindowKind}: {StringUtils.SplitByCamel(Enum.GetName(TankGame.Settings.WindowKind)!)}";
            WindowUtils.ChangeWindowKind(TankGame.Settings.WindowKind);
        };

        //Resolution
        ResolutionButton = new($"{TankGame.GameLanguage.Resolution}: {CurrentRes.Key}x{CurrentRes.Value}", FontGlobals.RebirthFont, Color.WhiteSmoke) {
            IsVisible = false,
            Tooltip = TankGame.GameLanguage.ResolutionDesc
        };
        ResolutionButton.SetDimensions(() => new Vector2(700, 850).ToResolution(), () => new Vector2(500, 150).ToResolution());
        ResolutionButton.OnLeftClick = (uiElement) => {
            var tryFind = CommonResolutions.FirstOrDefault(x => x.Key == CurrentRes.Key);

            if (Array.IndexOf(CommonResolutions, tryFind) > -1)
                _idxPair = Array.IndexOf(CommonResolutions, tryFind);

            _idxPair++;

            if (_idxPair >= CommonResolutions.Length)
                _idxPair = 0;

            CurrentRes = CommonResolutions[_idxPair];

            ResolutionButton.Text = $"{TankGame.GameLanguage.Resolution}: {CurrentRes.Key}x{CurrentRes.Value}";
        };
        ResolutionButton.OnRightClick = (uiElement) => {
            var tryFind = CommonResolutions.FirstOrDefault(x => x.Key == CurrentRes.Key);

            if (Array.IndexOf(CommonResolutions, tryFind) > -1) {
                _idxPair = Array.IndexOf(CommonResolutions, tryFind);
            }

            _idxPair--;

            if (_idxPair < 0)
                _idxPair = CommonResolutions.Length - 1;

            CurrentRes = CommonResolutions[_idxPair];

            ResolutionButton.Text = $"{TankGame.GameLanguage.Resolution}: {CurrentRes.Key}x{CurrentRes.Value}";
        };
        TeamColoursButton = new("Team Colours", FontGlobals.RebirthFont, Color.WhiteSmoke) {
            IsVisible = false,
            Tooltip = "Tint every tank with its team's colour, so teams are easy to tell apart.",
            HoverColor = Color.White,
        };
        TeamColoursButton.SetDimensions(() => new Vector2(1250, 100).ToResolution(), () => new Vector2(500, 150).ToResolution());
        TeamColoursButton.OnLeftClick = (_) => {
            TankGame.Settings.TeamColours = !TankGame.Settings.TeamColours;
            Tank.ShowTeamVisuals = TankGame.Settings.TeamColours;
            PlayerColors.SaveSettings();
        };
        ToggleLook(TeamColoursButton, "Team Colours", () => TankGame.Settings.TeamColours);

        LightingButton = new("Dynamic Lighting", FontGlobals.RebirthFont, Color.WhiteSmoke) {
            IsVisible = false,
            Tooltip = "Sunlight, shadows and glowing shells and explosions. Use - and + to pick Off, Low, Medium, High or Ultra. Higher looks better but is heavier.",
            HoverColor = Color.White,
        };
        LightingButton.SetDimensions(() => new Vector2(1250, 350).ToResolution(), () => new Vector2(500, 150).ToResolution());
        // - and + step through Off, Low, Medium, High, Ultra
        Stepper(LightingButton, () => "Lighting: " + (!TankGame.Settings.DynamicLighting ? "Off"
                : TankGame.Settings.LightingQuality switch { 0 => "Low", 1 => "Medium", 2 => "High", _ => "Ultra" }),
            d => SetLighting(Math.Clamp((TankGame.Settings.DynamicLighting ? TankGame.Settings.LightingQuality : -1) + d, -1, 3)),
            () => !TankGame.Settings.DynamicLighting, () => TankGame.Settings.DynamicLighting && TankGame.Settings.LightingQuality >= 3);

        // phones: vibration for shooting, mines, items and getting hit
        LitSurroundingsButton = new("Vibration", FontGlobals.RebirthFont, Color.WhiteSmoke) {
            IsVisible = false,
            Tooltip = "Short vibrations when you shoot, lay a mine, get or use an item, get hit, or a blast goes off near you.",
            HoverColor = Color.White,
        };
        LitSurroundingsButton.SetDimensions(() => new Vector2(1240, 600).ToResolution(), () => new Vector2(500, 150).ToResolution());
        LitSurroundingsButton.OnLeftClick = (_) => {
            TankGame.Settings.Vibration = !TankGame.Settings.Vibration;
            PlayerColors.SaveSettings();
            if (TankGame.Settings.Vibration)
                Internals.Common.Framework.Input.Haptics.ItemPickup(); // a taste of it
        };
        ToggleLook(LitSurroundingsButton, "Vibration", () => TankGame.Settings.Vibration);

        // brightness of the game picture: tap the left side for darker, the right side for brighter
        BrightnessButton = new("Brightness", FontGlobals.RebirthFont, Color.WhiteSmoke) {
            IsVisible = false,
            Tooltip = "How bright the game looks. Tap - to make it darker, + to make it brighter.",
            HoverColor = Color.White,
        };
        BrightnessButton.SetDimensions(() => new Vector2(1250, 600).ToResolution(), () => new Vector2(500, 150).ToResolution());
        Stepper(BrightnessButton, () => $"Brightness: {TankGame.Settings.Brightness}%",
            d => { TankGame.Settings.Brightness = Math.Clamp(TankGame.Settings.Brightness + d * 5, 50, 130); PlayerColors.SaveSettings(); },
            () => TankGame.Settings.Brightness <= 50, () => TankGame.Settings.Brightness >= 130);

        if (Internals.Common.Framework.Input.TouchControls.Enabled) {
            // phones: three columns
            BrightnessButton.SetDimensions(() => new Vector2(1240, 350).ToResolution(), () => new Vector2(500, 150).ToResolution());
            PerPixelLightingToggle.SetDimensions(() => new Vector2(155, 95).ToResolution(), () => new Vector2(510, 160).ToResolution());
            PerPixelLightingButton.SetDimensions(() => new Vector2(160, 100).ToResolution(), () => new Vector2(500, 150).ToResolution());
            VsyncButton.SetDimensions(() => new Vector2(160, 350).ToResolution(), () => new Vector2(500, 150).ToResolution());
            FullScreenButton.SetDimensions(() => new Vector2(160, 600).ToResolution(), () => new Vector2(500, 150).ToResolution());
            ResolutionButton.SetDimensions(() => new Vector2(700, 100).ToResolution(), () => new Vector2(500, 150).ToResolution());
            TeamColoursButton.SetDimensions(() => new Vector2(700, 350).ToResolution(), () => new Vector2(500, 150).ToResolution());
            LightingButton.SetDimensions(() => new Vector2(700, 600).ToResolution(), () => new Vector2(500, 150).ToResolution());
            PerPixelLightingToggle.UniqueDraw = (_, _) => { }; // (the box itself shows on/off)
            PerPixelLightingButton.OnLeftClick = (_) => {
                TankGame.Settings.PerPixelLighting = !TankGame.Settings.PerPixelLighting;
                PlayerColors.SaveSettings();
            };
            ToggleLook(PerPixelLightingButton, "Per-Pixel Lighting", () => TankGame.Settings.PerPixelLighting);
            // phones run at native resolution, so this slot becomes the multi-core AI setting
            ResolutionButton.Text = "Multi-Core AI";
            ResolutionButton.Tooltip = "Spread enemy thinking across your phone's CPU cores for smoother big fights.";
            ResolutionButton.HoverColor = Color.White;
            ResolutionButton.Color = Color.WhiteSmoke;
            ResolutionButton.OnRightClick = null;
            ResolutionButton.OnLeftClick = (_) => {
                TankGame.Settings.MultiCoreAI = !TankGame.Settings.MultiCoreAI;
                PlayerColors.SaveSettings();
            };
            ToggleLook(ResolutionButton, "Multi-Core AI", () => TankGame.Settings.MultiCoreAI);
        }
    }

    public static void HideAll() {
        PerPixelLightingButton.IsVisible = false;
        VsyncButton.IsVisible = false;
        FullScreenButton.IsVisible = false;
        ResolutionButton.IsVisible = false;
        TeamColoursButton.IsVisible = false;
        LightingButton.IsVisible = false;
        LitSurroundingsButton.IsVisible = false;
        BrightnessButton.IsVisible = false;
        PerPixelLightingToggle.IsVisible = false;
        VsyncToggle.IsVisible = false;
        FullScreenButton.IsVisible = false;

        //little extra
        //TankGame.Settings.ResWidth = curPair.Key;
        //TankGame.Settings.ResHeight = curPair.Value;
    }

    public static void ShowAll() {
        PerPixelLightingButton.IsVisible = true;
        VsyncButton.IsVisible = true; // phones: the debug readout switch
        FullScreenButton.IsVisible = true;
        ResolutionButton.IsVisible = true; // phones: the multi-core AI switch
        TeamColoursButton.IsVisible = true;
        LightingButton.IsVisible = true;
        LitSurroundingsButton.IsVisible = Internals.Common.Framework.Input.TouchControls.Enabled;
        BrightnessButton.IsVisible = true;
        PerPixelLightingToggle.IsVisible = true;
        VsyncToggle.IsVisible = !Internals.Common.Framework.Input.TouchControls.Enabled;
        FullScreenButton.IsVisible = true;
    }
}