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
        // phones: one big left-hand column (campaigns, Level Select, Validate, Back)
        CampaignColumnCount = totalCampaigns + 2;
        (Func<Vector2>, Func<Vector2>) nextSlot() {
            if (phone) {
                int idx = slot++;
                return (() => PhoneColumnSlot(idx, CampaignColumnCount).Location.ToVector2(), () => PhoneColumnSlot(idx, CampaignColumnCount).Size.ToVector2());
            }
            var yOffControl = slot % MAX_CAMPAIGNS_PER_COLUMN;
            var xOffControl = slot / MAX_CAMPAIGNS_PER_COLUMN;
            slot++;
            float offsetY = yOffControl * (defaultDimensions.Y + padding);
            float offsetX = xOffControl * (defaultDimensions.X + padding);
            return (() => new Vector2(
                (WindowUtils.WindowWidth / 2f) - uiStartX.ToResolutionX() + offsetX.ToResolutionX(),
                150f.ToResolutionY() + offsetY.ToResolutionY()), () => defaultDimensions.ToResolution());
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
            var (ePos, eSize) = nextSlot();
            elem.SetDimensions(ePos, eSize);

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
                var (lPos, lSize) = nextSlot();
                ls.SetDimensions(lPos, lSize);
                ls.OnMouseOver = (_) => SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect);
                ls.OnLeftClick += (_) => OpenLevelSelect();
                campaignNames.Add(ls);
            }
        }
        // phones: Freeplay is gone (it only opened an empty board); Validate joins the column
        if (phone) {
            int vIdx = CampaignColumnCount - 2;
            UpdateCampaignButton.Text = "Update Campaign";
            UpdateCampaignButton.Tooltip = null;
            UpdateCampaignButton.SetDimensions(() => PhoneColumnSlot(vIdx, CampaignColumnCount).Location.ToVector2(), () => PhoneColumnSlot(vIdx, CampaignColumnCount).Size.ToVector2());
            return;
        }
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
    /// <summary>Phones: a card on the right to pick the starting mission (- / +), with its best times.</summary>
    private static void DrawMissionPickerButtons() {
        var sb = TankGame.SpriteRenderer;
        float s = WindowUtils.WindowHeight / 1080f;
        var font = FontGlobals.RebirthFont;
        // centered in the space to the right of the button column
        float left = PhoneColumnRight + 40 * s;
        var card = new Rectangle((int)left, (int)(170 * s), (int)Math.Min(WindowUtils.WindowWidth - left - 60 * s, 900 * s), (int)(620 * s));
        card.X += (int)((WindowUtils.WindowWidth - 60 * s - card.Right) / 2);
        var cx = card.Center.X;

        float btnR = 72 * s;
        var minusC = new Vector2(card.X + 60 * s + btnR, card.Y + 250 * s);
        var plusC = new Vector2(card.Right - 60 * s - btnR, card.Y + 250 * s);
        var mouse = MouseUtils.MousePosition;
        bool OnBtn(Vector2 c) => Vector2.Distance(mouse, c) <= btnR * 1.15f;

        int unlocked = LevelProgress.HighestUnlocked(LevelSelectCampaign);
        int count = VanillaMissionCount();
        if (count > 0)
            unlocked = Math.Min(unlocked, count - 1);
        if (MissionCheckpoint > unlocked)
            MissionCheckpoint = unlocked;

        bool clicked = InputUtils.CanDetectClick();
        if (clicked && OnBtn(minusC) && MissionCheckpoint > 0) {
            MissionCheckpoint--;
            SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
        }
        if (clicked && OnBtn(plusC) && MissionCheckpoint < unlocked) {
            MissionCheckpoint++;
            SoundPlayer.PlaySoundInstance(TickSound, SoundContext.Effect);
        }

        sb.End();
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);

        int lip = (int)(10 * s);
        ToggleSwitch.DrawRoundedRect(sb, new Rectangle(card.X, card.Y + lip, card.Width, card.Height), 36 * s, UITextButton.PhoneEdge);
        ToggleSwitch.DrawRoundedRect(sb, card, 36 * s, UITextButton.PhoneFill);
        var ink = new Color(40, 30, 22);

        void Text(string t, float y, float scale, Color c) {
            var m = font.MeasureString(t);
            sb.DrawString(font, t, new Vector2(cx, y), c, new Vector2(scale * s), 0f, m / 2f);
        }
        Text("Start at mission", card.Y + 80 * s, 1.25f, ink);
        Text($"{MissionCheckpoint + 1}", minusC.Y, 2.6f, ink);

        foreach (var (c, label, enabled) in new[] { (minusC, "-", MissionCheckpoint > 0), (plusC, "+", MissionCheckpoint < unlocked) }) {
            var fill = !enabled ? new Color(200, 196, 188) : OnBtn(c) && InputUtils.CurrentMouseSnapshot.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed ? new Color(70, 110, 200) : new Color(96, 140, 230);
            ToggleSwitch.DrawDisc(sb, c + new Vector2(0, 6 * s), btnR, enabled ? new Color(50, 80, 150) : new Color(170, 165, 158));
            ToggleSwitch.DrawDisc(sb, c, btnR, fill);
            var m = font.MeasureString(label);
            sb.DrawString(font, label, c, Color.White, new Vector2(2.2f * s), 0f, m / 2f);
        }

        var rec = LevelProgress.Get(LevelSelectCampaign, MissionCheckpoint);
        float y = card.Y + 410 * s;
        Text(rec.BestSolo > 0 ? $"Fastest time: {LevelProgress.FormatTime(rec.BestSolo)}" : "Fastest time: --", y, 1.05f, ink);
        if (rec.CoopAttempted) {
            y += 64 * s;
            Text(rec.BestCoop > 0 ? $"Co-op fastest: {LevelProgress.FormatTime(rec.BestCoop)}" : "Co-op fastest: --", y, 1.05f, ink);
        }
        if (MissionCheckpoint == unlocked && (count == 0 || unlocked < count - 1))
            Text($"Clear mission {unlocked + 1} to unlock the next one", card.Bottom - 50 * s, 0.8f, new Color(120, 96, 70));

        sb.End();
        sb.Begin();
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
