using TanksRebirth.Internals.UI;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Linq;
using System;
using TanksRebirth.GameContent.Systems;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.UI;
using TanksRebirth.GameContent.Systems.Coordinates;
using TanksRebirth.Net;
using TanksRebirth.Achievements;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.Graphics;
using TanksRebirth.GameContent.Systems.PingSystem;
using TanksRebirth.Internals.Common.Framework.Animation;
using TanksRebirth.GameContent.RebirthUtils;
using TanksRebirth.GameContent.Cosmetics;
using TanksRebirth.GameContent.UI.MainMenu;
using TanksRebirth.GameContent.UI.LevelEditor;
using TanksRebirth.Graphics.Metrics;
using TanksRebirth.GameContent.Systems.ParticleSystem;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.TankSystem;

namespace TanksRebirth.GameContent;

#pragma warning disable CS8618
public class GameHandler {

    public const int MAX_AI_TANKS = 50;
    public const int MAX_PLAYERS = 4;
    public const int MAX_PARTICLES = 15000;

    public delegate void PostRender();
    public static event PostRender? OnPostRender;
    public delegate void PostUpdate();
    public static event PostUpdate? OnPostUpdate;

    public static ParticleManager Particles { get; } = new(MAX_PARTICLES, () => CameraGlobals.GameView, () => CameraGlobals.GameProjection);
    public static XpBar ExperienceBar;

    public static AITank[] AllAITanks = new AITank?[MAX_AI_TANKS];
    public static PlayerTank[] AllPlayerTanks = new PlayerTank?[MAX_PLAYERS];
    public static Tank[] AllTanks = new Tank?[MAX_PLAYERS + MAX_AI_TANKS];

    internal static void MapEvents() {
        CampaignGlobals.OnMissionEnd += IntermissionHandler.DoEndMissionWorkload;
    }

    internal static void Initialize() {
        Client.ClientRandSeed = DateTime.Now.Millisecond;
        Client.ClientRandom = new(Client.ClientRandSeed);

        AllAITanks = new AITank[MAX_AI_TANKS];
        AllPlayerTanks = new PlayerTank[MAX_PLAYERS];
        AllTanks = new Tank[MAX_PLAYERS + MAX_AI_TANKS];

        ExperienceBar = new() {
            Level = (ushort)TankGame.SaveFile.ExpLevel
        };
        ExperienceBar.Value = TankGame.SaveFile.ExpLevel - ExperienceBar.Level;
        ExperienceBar.ApproachValue = ExperienceBar.Value;

        ExperienceBar.Alignment = Anchor.LeftCenter;
        ExperienceBar.FillColor = Color.Lime;
        ExperienceBar.EmptyColor = Color.Red;

        GameSceneUI.Initialize();
        CosmeticsUI.Initialize();
        RebirthMouse.Initialize();
    }
    private static int _menuStillFrames;
    /// <summary>Restart the phone menu's still-frame warm-up (e.g. after loading a different mission behind it).</summary>
    public static void ResetMenuStill() => _menuStillFrames = 0;
    /// <summary>True once the menu's battlefield has been positioned and frozen.</summary>
    public static bool MenuStillReady => _menuStillFrames > 10;
    private static int _menuStillTankCount;
    private static bool _musicWasOn = true;
    internal static void UpdateAll(GameTime gameTime) {
        /*void doTestWithFont() {
            var str = 
                "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                "abcdefghijklmnopqrstuvwxyz" +
                "1234567890" +
                "`[]\\;',./~{}|:\"<>?";
            int longestId = -1;
            var longest = 0f;
            for (int i = 0; i < str.Length; i++) {
                var x = FontGlobals.RebirthFontLarge.MeasureString(str[i].ToString()).X;
                if (x > longest) {
                    longestId = i;
                    longest = x;
                }
            }
            Console.WriteLine($"{str[longestId]} is the longest. ({longest})");
        }
        if (InputUtils.KeyJustPressed(Keys.OemTilde))
            doTestWithFont();*/
        // ChatSystem.CurTyping = SoundPlayer.GetLengthOfSound("Content/Assets/sounds/tnk_shoot_ricochet_rocket_loop.ogg").ToString();

        ExperienceBar.Update();
        CosmeticsUI.Update();
        RoomScene.Update();

        Difficulties.GlobalManage();

        if (MainMenuUI.IsActive)
            PlayerTank.SetLives(999);
        // technically, level 0 in code is level 1, so we want to use that number (1) if the user is level 0.
        // ExperienceBar.Value = TankGame.GameData.ExpLevel - MathF.Floor(TankGame.GameData.ExpLevel);

        VanillaAchievements.Repository.UpdateCompletions(TankGame.VanillaAchievementPopupHandler);
        AchievementStore.Tick();
        Unlocks.Tick();

        if (RuntimeData.RunTime % 30 < RuntimeData.DeltaTime) {
            Client.SendLives();
            Client.SendKillCounts();
        }

        /* uh, yeah. this is the decay-per-level calculation. people don't want it!
        var floor1 = MathF.Floor(TankGame.GameData.ExpLevel + 1f);
        var floor0 = MathF.Floor(TankGame.GameData.ExpLevel);
        GameData.UniversalExpMultiplier = floor1 - (GameData.DecayPerLevel * floor0);*/

        if (InputUtils.KeyJustPressed(Keys.I)) {
            new Shell(new Vector2(0, 100), -Vector2.UnitY * 2, ShellID.Standard, null);
            new Shell(new Vector2(MouseUtils.Test.X * 10, -100), Vector2.UnitY * 2, ShellID.Standard, null);
        }

        if (Difficulties.Types["InfiniteLives"])
            PlayerTank.SetLives(PlayerTank.StartingLives);

        for (int i = 0; i < Animator.Animators.Count; i++)
            Animator.Animators[i].PlayAnimation(gameTime);

        foreach (var ping in IngamePing.AllIngamePings)
            ping?.Update();

        // phones: the main menu shows a still of the battlefield, so freeze the demo battle after a few frames
        // (a few updates are needed first to position the tanks)
        bool menuStill = Internals.Common.Framework.Input.TouchControls.Enabled && MainMenuUI.IsActive;
        // only count frames where the battle actually updated (not while the screen is still fading in), and
        // only once there are tanks on the board
        // The demo mission loads a moment after the menu appears, so restart the count whenever the set of tanks
        // on the board changes; new tanks need some updates to be positioned before they can be drawn.
        int liveAi = menuStill ? AllAITanks.Count(t => t is not null && !t.IsDestroyed) : 0;
        if (!menuStill || liveAi != _menuStillTankCount)
            _menuStillFrames = 0;
        _menuStillTankCount = liveAi;
        if (menuStill && liveAi > 0 && !IntermissionSystem.IsAwaitingNewMission)
            _menuStillFrames++;
        // (the menu battle now plays out live behind the menu; it used to freeze into a still)
        bool freezeEntities = false;

        Internals.Perf.Mark(Internals.Perf.S.UpdMisc);
        if (!IntermissionSystem.IsAwaitingNewMission && !freezeEntities) {
            foreach (var pTank in AllPlayerTanks)
                pTank?.Update();
            Internals.Perf.Mark(Internals.Perf.S.Players);

            AIManager.ProcessAITanks();
            Internals.Perf.Mark(Internals.Perf.S.AI);

            foreach (var mine in Mine.AllMines)
                mine?.Update();

            foreach (var bullet in Shell.AllShells)
                bullet?.Update();

            foreach (var fp in TankFootprint.AllFootprints)
                fp?.Update();

            foreach (var p in Airplane.AllPlanes)
                p?.Update();
        }
        if (CampaignGlobals.InMission) {
            _musicWasOn = true;
            TankMusicSystem.Update();

            foreach (var crate in Crate.crates)
                crate?.Update();

            foreach (var pu in Powerup.Powerups)
                pu?.Update();
        }
        else {
            // silence the mission music: right when a mission ends, then only now and then
            // (it used to stop every track on every frame between missions and on the menu)
            if (_musicWasOn || RuntimeData.UpdateCount % 30 == 0) {
                foreach (var audio in TankMusicSystem.Audio) {
                    audio.Value.Volume = 0;
                    audio.Value.Stop();
                }
            }
            _musicWasOn = false;
        }

        LevelEditorUI.Update();
        Internals.Perf.Mark(Internals.Perf.S.Shots);

        foreach (var expl in Explosion.Explosions)
            expl?.Update();

        if (CampaignGlobals.ShouldMissionsProgress && !MainMenuUI.IsActive)
            IntermissionHandler.HandleMissionChanging();

        foreach (var cube in Block.AllBlocks)
            cube?.OnUpdate();

        if ((DebugManager.DebuggingEnabled && DebugManager.DebugLevel == DebugManager.Id.LevelEditDebug && CameraGlobals.OverheadView) || LevelEditorUI.IsActive)
            foreach (var sq in PlacementSquare.Placements)
                sq?.Update();

        Internals.Perf.Mark(Internals.Perf.S.Blocks);
        Particles.UpdateParticles();
        Internals.Perf.Mark(Internals.Perf.S.Particles);

        var mmActive = MainMenuUI.IsActive;
        if (mmActive)
            MainMenuUI.Update();
        Internals.Perf.Mark(Internals.Perf.S.MenuUpd);
        // I AM PUTTING THIS HERE BECAUSE FUCK YOU.
        MainMenuUI.UpdateCampaignButton.IsVisible = MainMenuUI.MenuState == MainMenuUI.UIState.Campaigns && mmActive && !Internals.Common.Framework.Input.TouchControls.Enabled;

        // questioning as to why this is placed here
        if ((CameraGlobals.OverheadView || MainMenuUI.IsActive) && !LevelEditorUI.IsActive) {
            CampaignGlobals.InMission = false;
            IntermissionHandler.TankFunctionWait = 600;
        }

        SceneManager.HandleSceneVisuals();

        IntermissionHandler.Update();

        if (CameraGlobals.OverheadView)
            LevelEditorUI.HandleLevelEditorModifications();

        Internals.Perf.Mark(Internals.Perf.S.Logic);
        OnPostUpdate?.Invoke();
        Internals.Perf.Mark(Internals.Perf.S.PostUpd);
    }

    internal static void RenderAll() {
        TankGame.Instance.GraphicsDevice.BlendState = BlendState.AlphaBlend;

        // (phones: no experience bar during play)
        if (!MainMenuUI.IsActive && !LevelEditorUI.IsEditing && !Internals.Common.Framework.Input.TouchControls.Enabled) {
            ExperienceBar.Position = new(WindowUtils.WindowWidth / 2 - ExperienceBar.Scale.X / 2, 50);
            ExperienceBar.Scale = new(600, 20);
            ExperienceBar.Alignment = Anchor.LeftCenter;
            ExperienceBar.FillColor = Color.Green;
            ExperienceBar.EmptyColor = Color.Red;
            ExperienceBar.GainedColor = Color.Lime;
            ExperienceBar.Render(TankGame.SpriteRenderer);
        }
        
        Internals.Perf.Mark(Internals.Perf.S.Room);
        // CHECK: move this back if necessary
        GameScene.RenderWorldModels();
        // TankFootprint.Draw();
        Internals.Perf.Mark(Internals.Perf.S.World);

        foreach (var tank in AllTanks)
            tank?.Render();
        Internals.Perf.Mark(Internals.Perf.S.Tanks);

        foreach (var cube in Block.AllBlocks)
            cube?.OnRender();
        Internals.Perf.Mark(Internals.Perf.S.BlocksDraw);

        foreach (var mine in Mine.AllMines)
            mine?.Render();

        foreach (var bullet in Shell.AllShells)
            bullet?.Render();

        foreach (var mark in TankDeathMark.deathMarks)
            mark?.Render();

        foreach (var ping in IngamePing.AllIngamePings)
            ping?.Render();

        foreach (var p in Airplane.AllPlanes)
            p?.Render();

        //foreach (var print in TankFootprint.footprints)
        //print?.Render();

        foreach (var crate in Crate.crates)
            crate?.Render();

        foreach (var powerup in Powerup.Powerups)
            powerup?.Render();

        Internals.Perf.Mark(Internals.Perf.S.ShotsDraw);
        Particles.RenderModelParticles();

        if ((DebugManager.DebugLevel == DebugManager.Id.LevelEditDebug && CameraGlobals.OverheadView) || LevelEditorUI.IsActive)
            foreach (var sq in PlacementSquare.Placements)
                sq?.Render();

        TankGame.Instance.GraphicsDevice.BlendState = BlendState.NonPremultiplied;

        Particles.RenderParticles();
        Internals.Perf.Mark(Internals.Perf.S.PartDraw);

        // (1.9) shadows + lights, multiplied onto the world drawn so far. Everything after this stays unlit.
        Graphics.DynamicLighting.LightingSystem.EndFrame(TankGame.GameFrameBuffer);
        Internals.Perf.Mark(Internals.Perf.S.Lights);

        // only render the level editor if it's active
        // change depth stencil...?
        if (LevelEditorUI.IsActive) {
            LevelEditorUI.Render();
        }

        // only draw if ingame + is multiplayer.
        if (CampaignGlobals.ShouldMissionsProgress 
            && !MainMenuUI.IsActive
            && Client.IsConnected()) {
            PingMenu.DrawPingHUD();
        }

        IntermissionHandler.RenderCountdownGraphics();

        OnPostRender?.Invoke();
        Internals.Perf.Mark(Internals.Perf.S.PostDraw);
    }

    public static void RenderUI() {
        foreach (var element in UIElement.AllUIElements.ToList()) {
            // element.Position = Vector2.Transform(element.Position, UIMatrix * Matrix.CreateTranslation(element.Position.X, element.Position.Y, 0));
            if (element.Parent != null)
                continue;

            if (element.HasScissor)
                TankGame.SpriteRenderer.End();

            element?.Draw(TankGame.SpriteRenderer);

            if (element!.HasScissor)
                TankGame.SpriteRenderer.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, rasterizerState: RenderGlobals.DefaultRasterizer);
        }
        foreach (var element in UIElement.AllUIElements)
            element?.DrawTooltips(TankGame.SpriteRenderer);

        CameraGlobals.SetMatrices();
    }
    public static void SetupGraphics() {
        TankGame.ClientLog.Write("SetupGraphics: GameShaders.Initialize();", Internals.LogType.Debug);
        GameShaders.Initialize();
        TankGame.ClientLog.Write("SetupGraphics: GameScene.InitializeRenderers();", Internals.LogType.Debug);
        GameScene.InitializeRenderers();
        TankGame.ClientLog.Write("SetupGraphics: SceneManager.LoadGameScene();", Internals.LogType.Debug);
        SceneManager.LoadGameScene();
        TankGame.ClientLog.Write("SetupGraphics: DebugManager.InitDebugUI();", Internals.LogType.Debug);
        DebugManager.InitDebugUI();
        TankGame.ClientLog.Write("SetupGraphics: PlacementSquare.InitializeLevelEditorSquares();", Internals.LogType.Debug);
        PlacementSquare.InitializeLevelEditorSquares();
    }
}