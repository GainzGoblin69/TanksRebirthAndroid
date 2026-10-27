using System;
using Microsoft.Xna.Framework;
using TanksRebirth.Internals.Common.Framework.Input;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals.UI;

namespace TanksRebirth.GameContent.UI.MainMenu;

/// <summary>
/// Phones: the menus put their buttons in one big column on the left (finger-sized, thumb-reachable) instead of
/// the desktop's thin bars in the middle of the screen. The logo moves to the right half.
/// </summary>
public static partial class MainMenuUI {
    private static bool Phone => TouchControls.Enabled;
    private static float PhoneScale => WindowUtils.WindowHeight / 1080f;

    /// <summary>Number of slots in the Campaigns screen's column (campaigns, Level Select, Validate, Back).</summary>
    internal static int CampaignColumnCount = 4;

    private const float COLUMN_X = 64, COLUMN_W = 640, COLUMN_TOP = 110, COLUMN_BOTTOM_PAD = 56, COLUMN_GAP = 24, COLUMN_MAX_H = 128;

    /// <summary>Right edge of the button column, in pixels.</summary>
    public static float PhoneColumnRight => (COLUMN_X + COLUMN_W) * PhoneScale;

    /// <summary>Slot <paramref name="i"/> of <paramref name="n"/> in the left column, vertically centered, in pixels.</summary>
    public static Rectangle PhoneColumnSlot(int i, int n) {
        float s = PhoneScale;
        float top = COLUMN_TOP * s, bottom = WindowUtils.WindowHeight - COLUMN_BOTTOM_PAD * s, gap = COLUMN_GAP * s;
        n = Math.Max(1, n);
        if ((bottom - top - gap * (n - 1)) / n < 70 * s)
            gap = 12 * s; // many campaigns: squeeze the gaps first
        float h = MathF.Min(COLUMN_MAX_H * s, (bottom - top - gap * (n - 1)) / n);
        float total = n * h + (n - 1) * gap;
        float y0 = top + (bottom - top - total) / 2f;
        return new Rectangle((int)(COLUMN_X * s), (int)(y0 + i * (h + gap)), (int)(COLUMN_W * s), (int)h);
    }

    private static void PhonePlace(UIElement el, Func<Vector2> pos, Func<Vector2> size, Func<Rectangle?> phoneRect) {
        el.SetDimensions(
            () => Phone && phoneRect() is { } r ? r.Location.ToVector2() : pos(),
            () => Phone && phoneRect() is { } r ? r.Size.ToVector2() : size());
    }

    private static Rectangle? InMenuState(UIState state, int i, int n) =>
        IsActive && MenuState == state ? PhoneColumnSlot(i, n) : null;

    /// <summary>Called after the menu and GameUI buttons exist.</summary>
    private static void ApplyPhoneLayout() {
        // main menu
        PhonePlace(PlayButton, () => new Vector2(700, 550).ToResolution(), () => new Vector2(500, 50).ToResolution(),
            () => InMenuState(UIState.PrimaryMenu, 0, 4));
        PhonePlace(GameUI.OptionsButton, () => GameUI.OptionsButtonPos.ToResolution(), () => GameUI.OptionsButtonSize.ToResolution(),
            () => InMenuState(UIState.PrimaryMenu, 1, 4));
        PhonePlace(StatsMenu, () => new Vector2(WindowUtils.WindowWidth / 2 - 90.ToResolutionX(), WindowUtils.WindowHeight - 100.ToResolutionY()), () => new Vector2(180, 50).ToResolution(),
            () => InMenuState(UIState.PrimaryMenu, 2, 4));
        PhonePlace(GameUI.QuitButton, () => GameUI.QuitButtonPos.ToResolution(), () => GameUI.QuitButtonSize.ToResolution(),
            () => InMenuState(UIState.PrimaryMenu, 3, 4));

        // play menu
        PhonePlace(PlayButton_SinglePlayer, () => new Vector2(700, 450).ToResolution(), () => new Vector2(500, 50).ToResolution(),
            () => InMenuState(UIState.PlayList, 0, 5));
        PhonePlace(PlayButton_Multiplayer, () => new Vector2(700, 750).ToResolution(), () => new Vector2(500, 50).ToResolution(),
            () => InMenuState(UIState.PlayList, 1, 5));
        PhonePlace(CosmeticsMenuButton, () => new Vector2(50, 50).ToResolution(), () => new Vector2(300, 50).ToResolution(),
            () => InMenuState(UIState.PlayList, 2, 5));
        PhonePlace(PlayButton_LevelEditor, () => new Vector2(700, 650).ToResolution(), () => new Vector2(500, 50).ToResolution(),
            () => InMenuState(UIState.PlayList, 3, 5));
        // Difficulties: on the Single Player (campaign) screen and in the Wi-Fi host's lobby
        PhonePlace(DifficultiesButton, () => new Vector2(700, 550).ToResolution(), () => new Vector2(500, 50).ToResolution(),
            () => InMenuState(UIState.Campaigns, CampaignColumnCount - 3, CampaignColumnCount) ?? InMenuState(UIState.MPLobby, 1, 4));

        // Back is shared by every screen: only the play menu and the Campaigns screen use the column
        PhonePlace(GameUI.BackButton, () => new Vector2(700, 850).ToResolution(), () => new Vector2(500, 150).ToResolution(),
            () => InMenuState(UIState.PlayList, 4, 5) ?? InMenuState(UIState.Campaigns, CampaignColumnCount - 1, CampaignColumnCount));

        if (Phone) {
            // phones: Cosmetics is the tank color picker; Multiplayer has its own screens; Difficulties remembers where it was opened
            CosmeticsMenuButton.Text = "Tank Color";
            CosmeticsMenuButton.OnLeftClick = (_) => PhoneOpenTankColors();
            PlayButton_Multiplayer.OnLeftClick = (_) => PhonePlayMultiplayer();
            DifficultiesButton.OnLeftClick = (_) => PhoneOpenDifficulties();
            // the hover tooltips stick around after a tap on a touch screen
            foreach (var b in new UIElement[] { PlayButton, PlayButton_SinglePlayer, PlayButton_Multiplayer, DifficultiesButton, CosmeticsMenuButton, PlayButton_LevelEditor, StatsMenu })
                b.Tooltip = null;
        }
    }
}
