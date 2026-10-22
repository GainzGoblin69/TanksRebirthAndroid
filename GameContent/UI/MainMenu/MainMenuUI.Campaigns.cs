using Microsoft.Xna.Framework.Input;
using System.Diagnostics;
using System.IO;
using System;
using System.Linq;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.GameUI;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Net;
using Microsoft.Xna.Framework;
using FontStashSharp;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Speedrunning;
using TanksRebirth.Internals;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.UI;

namespace TanksRebirth.GameContent.UI.MainMenu;

public static partial class MainMenuUI {
    // Explicit static constructor: makes the static fields above/below initialize at first use (as on desktop).
    // Without it, Android's Mono runtime may run them early, before the fonts/textures/settings they read exist.
    static MainMenuUI() { }
    static bool _playerHasVanillaCampaign;

    const int MAX_CAMPAIGNS_PER_COLUMN = 8;

    public static UITextButton UpdateCampaignButton = new("Validate", FontGlobals.RebirthFont, Color.Black) {
        Position = new Vector2(-100)
    };
    public static void DrawCampaignsUI() {
        DrawCampaignMenuExtras();
    }
    private static void SetCampaignDisplay() {
        SetPlayButtonsVisibility(false);

        float width = 150;
        float height = 50;
        UpdateCampaignButton.Text = "Validate";
        UpdateCampaignButton.Tooltip = "Ensures your Vanilla campaign is up-to-date.";
        UpdateCampaignButton.SetDimensions(() => new(WindowUtils.WindowWidth / 2 - width.ToResolutionX() / 2, 10), () => new Vector2(width, height).ToResolution());
        UpdateCampaignButton.Color = Color.White;
        UpdateCampaignButton.Font = FontGlobals.RebirthFont;
        UpdateCampaignButton.OnLeftClick = (a) => {
            DownloadVanillaCampaign(true);
            ChatSystem.SendMessage("Validation complete!", Color.Lime);
        };

        foreach (var elem in campaignNames)
            elem?.Remove();

        // get all the campaign folders from the SaveDirectory + Campaigns
        var path = Path.Combine(TankGame.SaveDirectory, "Campaigns");
        Directory.CreateDirectory(path);
        // add a new UIElement for each campaign folder

        bool phone = Internals.Common.Framework.Input.TouchControls.Enabled;
        // phones: the Vanilla campaign comes first (shown as "Campaign"), with Level Select right under it
        var campaignFiles = Directory.GetFiles(path).Where(file => file.EndsWith(".campaign"))
            .OrderBy(f => phone && Path.GetFileNameWithoutExtension(f) == LevelSelectCampaign ? 0 : 1).ToArray();
        bool hasVanilla = phone && campaignFiles.Any(f => Path.GetFileNameWithoutExtension(f) == LevelSelectCampaign);

        var defaultDimensions = new Vector2(300, 40);
        float padding = 20f;

        int totalCampaigns = campaignFiles.Length + (hasVanilla ? 1 : 0);
        int numColumns = (int)Math.Ceiling(totalCampaigns / (float)MAX_CAMPAIGNS_PER_COLUMN);

        // totalWidth and uiStartX must be calculated in logical units
        float totalWidth = numColumns * defaultDimensions.X + (numColumns - 1) * padding;

        // scale the start position AFTER calculating centering
        float uiStartX = totalWidth / 2f;

        int slot = 0;
        Func<Vector2> nextSlot() {
            var yOffControl = slot % MAX_CAMPAIGNS_PER_COLUMN;
            var xOffControl = slot / MAX_CAMPAIGNS_PER_COLUMN;
            slot++;
            float offsetY = yOffControl * (defaultDimensions.Y + padding);
            float offsetX = xOffControl * (defaultDimensions.X + padding);
            return () => new Vector2(
                (WindowUtils.WindowWidth / 2f) - uiStartX.ToResolutionX() + offsetX.ToResolutionX(),
                150f.ToResolutionY() + offsetY.ToResolutionY());
        }

        for (int i = 0; i < campaignFiles.Length; i++) {
            var name = campaignFiles[i];
            var fileName = Path.GetFileNameWithoutExtension(name);
            bool isVanilla = phone && fileName == LevelSelectCampaign;

            int numTanks = 0;
            var campaign = Campaign.Load(name);
            var missions = campaign.CachedMissions;
            foreach (var mission in missions)
                numTanks += mission.Tanks.Count(x => !x.IsPlayer);

            var elem = new UITextButton(isVanilla ? "Campaign" : fileName, FontGlobals.RebirthFont, Color.White, 0.8f) {
                IsVisible = true,
                Tooltip = missions.Length + " missions" +
                $"\n{numTanks} tanks total" +
                $"\n\nName: {campaign.MetaData.Name}" +
                $"\nDescription: {campaign.MetaData.Description}" +
                $"\nVersion: {campaign.MetaData.Version}" +
                $"\nStarting Lives: {campaign.MetaData.StartingLives}" +
                $"\nBonus Life Count: {campaign.CachedMissions.Count(x => x.GrantsExtraLife)}" +
                $"\nTags: {string.Join(", ", campaign.MetaData.Tags)}" +
                $"\n\nMiddle click to DELETE ME."
            };

            if (phone)
                elem.Tooltip = null;
            elem.SetDimensions(nextSlot(), () => defaultDimensions.ToResolution());

            elem.OnLeftClick += (el) => {
                if (Client.IsConnected() && !Client.IsHost()) {
                    ChatSystem.SendMessage("You cannot initiate a game as you are not the host!", Color.Red);
                    SoundPlayer.SoundError();
                    return;
                }

                var noExt = fileName;
                // phones: missions not cleared yet are locked (start no further than one past the furthest clear)
                if (phone)
                    MissionCheckpoint = Math.Min(MissionCheckpoint, LevelProgress.HighestUnlocked(noExt));
                PrepareGameplay(noExt, !Client.IsConnected() || Server.CurrentClientCount == 1, false);
                OnCampaignSelected?.Invoke(CampaignGlobals.LoadedCampaign);
            };

            elem.OnMiddleClick += (el) => {
                var path = Path.Combine(TankGame.SaveDirectory, "Campaigns", fileName);
                File.Delete(path + ".campaign");
                SetCampaignDisplay();
            };

            elem.OnMouseOver = (_) => SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);

            campaignNames.Add(elem);

            if (isVanilla) {
                var ls = new UITextButton("Level Select", FontGlobals.RebirthFont, Color.White, 0.8f) { IsVisible = true };
                ls.SetDimensions(nextSlot(), () => defaultDimensions.ToResolution());
                ls.OnMouseOver = (_) => SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);
                ls.OnLeftClick += (_) => OpenLevelSelect();
                campaignNames.Add(ls);
            }
        }
        // phones: Freeplay is gone (it only opened an empty board)
        if (phone)
            return;
        var extra = new UITextButton("Freeplay", FontGlobals.RebirthFont, Color.White, 0.8f) {
            IsVisible = true,
            Tooltip = "Play without a campaign!",
        };
        extra.SetDimensions(() => new Vector2(WindowUtils.WindowWidth / 2 - defaultDimensions.X.ToResolutionX() / 2, 90.ToResolutionY()), () => defaultDimensions.ToResolution());
        extra.OnMouseOver = (uiElement) => { SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect); };
        //elem.HasScissor = true;
        //elem.
        extra.OnLeftClick += (el) => {
            foreach (var elem in campaignNames)
                elem.Remove();

            CampaignGlobals.ShouldMissionsProgress = false;
            UpdateCampaignButton.IsVisible = false;
            IntermissionSystem.TimeBlack = 90;
        };
        campaignNames.Add(extra);
    }
    public static void BotherUserForNotHavingVanillaCampaign() {
        if (Client.IsConnected() && Client.IsHost())
            TankGame.SpriteRenderer.DrawString(FontGlobals.RebirthFont, $"The people who are connected to you MUST own this\ncampaign, and it MUST have the same file name.\nOtherwise, the campaign will not load.", new(12, WindowUtils.WindowHeight / 2), Color.White, new Vector2(0.75f).ToResolution(), 0f, Vector2.Zero);
    }
    // dlBytes is only non-null values when campaignExists is true
    public static bool IsVanillaCampaignUpToDate(out bool campaignExists, out byte[]? dlBytes, out string? dlName) {
        var checkPath = Path.Combine(TankGame.SaveDirectory, "Campaigns", "Vanilla.campaign");
        if (!File.Exists(checkPath)) {
            dlBytes = null;
            dlName = null;
            campaignExists = false;
            return false;
        }
        campaignExists = true;
        dlBytes = WebUtils.DownloadWebFile("https://github.com/RighteousRyan1/tanks_rebirth_motds/blob/master/Vanilla.campaign?raw=true", out dlName, out var status);
        var fileBytes = File.ReadAllBytes(checkPath);


        return dlBytes.SequenceEqual(fileBytes);
    }
    public static void DownloadVanillaCampaign(bool inCampaignsMenu) {
        var bytes = WebUtils.DownloadWebFile("https://github.com/RighteousRyan1/tanks_rebirth_motds/blob/master/Vanilla.campaign?raw=true", out var filename, out var status);
        var path = Path.Combine(TankGame.SaveDirectory, "Campaigns", filename);
        File.WriteAllBytes(path, bytes);

        if (inCampaignsMenu)
            SetCampaignDisplay();
    }
    /// <summary>Phones: pick the starting mission with - / + buttons instead of the mouse wheel.</summary>
    private static void DrawMissionPickerButtons() {
        var sb = TankGame.SpriteRenderer;
        var center = new Vector2(WindowUtils.WindowWidth / 2f, WindowUtils.WindowHeight * 0.66f);
        var size = new Vector2(110, 90).ToResolution();
        var minus = new Rectangle((int)(center.X - size.X * 2.4f), (int)(center.Y - size.Y / 2), (int)size.X, (int)size.Y);
        var plus = new Rectangle((int)(center.X + size.X * 1.4f), (int)(center.Y - size.Y / 2), (int)size.X, (int)size.Y);

        bool clicked = InputUtils.CanDetectClick();
        if (clicked && minus.Contains(MouseUtils.MousePosition) && MissionCheckpoint > 0) {
            MissionCheckpoint--;
            SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
        }
        int unlocked = LevelProgress.HighestUnlocked(LevelSelectCampaign);
        int count = VanillaMissionCount();
        if (count > 0)
            unlocked = Math.Min(unlocked, count - 1);
        if (MissionCheckpoint > unlocked)
            MissionCheckpoint = unlocked;
        if (clicked && plus.Contains(MouseUtils.MousePosition) && MissionCheckpoint < unlocked) {
            MissionCheckpoint++;
            SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
        }

        foreach (var (rect, label, enabled) in new[] { (minus, "-", MissionCheckpoint > 0), (plus, "+", MissionCheckpoint < unlocked) }) {
            var col = !enabled ? Color.Gray : rect.Contains(MouseUtils.MousePosition) ? Color.CornflowerBlue : Color.WhiteSmoke;
            DrawUtils.DrawNineSliced(sb, UIElement.UIPanelBackground, 12, rect, col, Vector2.Zero);
            var m = FontGlobals.RebirthFont.MeasureString(label);
            sb.DrawString(FontGlobals.RebirthFont, label, rect.Center.ToVector2(), Color.Black, new Vector2(1.6f).ToResolution(), 0f, m / 2f);
        }

        DrawUtils.DrawStringWithBorder(sb, FontGlobals.RebirthFont, $"Start at mission {MissionCheckpoint + 1}",
            center, Color.White, Color.Black, new Vector2(0.9f).ToResolution(), 0f, Anchor.Center);
        var rec = LevelProgress.Get(LevelSelectCampaign, MissionCheckpoint);
        var lines = new System.Collections.Generic.List<string> {
            rec.BestSolo > 0 ? $"Fastest time: {LevelProgress.FormatTime(rec.BestSolo)}" : "Fastest time: --"
        };
        if (rec.CoopAttempted)
            lines.Add(rec.BestCoop > 0 ? $"Co-op fastest: {LevelProgress.FormatTime(rec.BestCoop)}" : "Co-op fastest: --");
        if (MissionCheckpoint == unlocked && (count == 0 || unlocked < count - 1))
            lines.Add($"Clear mission {unlocked + 1} to unlock the next one");
        DrawUtils.DrawStringWithBorder(sb, FontGlobals.RebirthFont, string.Join("\n", lines),
            center + new Vector2(0, size.Y * 0.9f), Color.White, Color.Black, new Vector2(0.6f).ToResolution(), 0f, Anchor.TopCenter);
    }

    private static int _vanillaCount = -1;
    private static int VanillaMissionCount() {
        if (_vanillaCount >= 0)
            return _vanillaCount;
        if (_lsCampaign is not null)
            return _vanillaCount = _lsCampaign.CachedMissions.Length;
        var p = Path.Combine(TankGame.SaveDirectory, "Campaigns", LevelSelectCampaign + ".campaign");
        if (!File.Exists(p))
            return 0; // not downloaded yet; check again next time
        try { return _vanillaCount = Campaign.Load(p).CachedMissions.Length; }
        catch { return _vanillaCount = 0; }
    }

    public static void DrawCampaignMenuExtras() {
        if (_oldwheel != InputUtils.DeltaScrollWheel)
            MissionCheckpoint += InputUtils.DeltaScrollWheel - _oldwheel;
        if (MissionCheckpoint < 0)
            MissionCheckpoint = 0;

        if (Internals.Common.Framework.Input.TouchControls.Enabled) {
            // phones: the picker shows the best times; no speedrun list
            DrawMissionPickerButtons();
            return;
        }
        else DrawUtils.DrawStringWithBorder(TankGame.SpriteRenderer, FontGlobals.RebirthFont, $"You can scroll with your mouse to skip to a certain mission." +
            $"\nCurrently, you will skip to mission {MissionCheckpoint + 1}." +
            $"\nYou will be alerted if that mission does not exist.", new Vector2(WindowUtils.WindowWidth / 3, WindowUtils.WindowHeight * 0.6f),
            Color.White, Color.Black, new Vector2(0.75f).ToResolution(), 0f, Anchor.TopCenter);

        var recordsPos = new Vector2(WindowUtils.WindowWidth / 3 * 2, WindowUtils.WindowHeight * 0.6f);
        var tex = GameResources.GetGameResource<Texture2D>("Assets/textures/ui/trophy");
        TankGame.SpriteRenderer.Draw(tex, recordsPos - new Vector2(175, -45).ToResolution(), null, Color.White, 0f, Anchor.RightCenter.GetTextureAnchor(tex), new Vector2(0.5f).ToResolution(), default, default);
        var text = $"Top {Speedrun.LoadedSpeedruns.Length} speedruns:\n" + string.Join(Environment.NewLine, Speedrun.LoadedSpeedruns);
        DrawUtils.DrawStringWithBorder(TankGame.SpriteRenderer, FontGlobals.RebirthFont, text, recordsPos, Color.White, Color.Black, new Vector2(0.75f).ToResolution(), 0f, Anchor.TopCenter);
    }
}
