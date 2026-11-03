using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;
using System.Threading.Tasks;
using TanksRebirth.Enums;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.GameContent.UI;
using TanksRebirth.GameContent.UI.MainMenu;
using TanksRebirth.Graphics;
using TanksRebirth.Internals;
using TanksRebirth.Internals.Common.Framework;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals.UI;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.RebirthUtils;
public static class SceneManager {

    public static Lighting.LightProfile GameLight = new() {
        Color = new(150, 150, 170),
        Brightness = 0.75f,
        //isNight = true
    };

    public static Color ThunderColor = Color.DeepSkyBlue;

    private static bool _musicLoaded;

    public delegate void LoadTankScene();
    public static event LoadTankScene? OnLoadTankScene;
    public delegate void MissionCleanupEvent();
    public static event MissionCleanupEvent? OnMissionCleanup;


    /// <summary>
    /// Uses a multithreaded approach to start the campaign results screen.
    /// </summary>
    /// <param name="delay">The delay of time before starting the results screen.</param>
    /// <param name="context">The context of which the campaign is ending.</param>
    // ---- (from 1.9) the mission-end message: the words slide in from both sides, flash gold, then fade ----
    internal static float timeLeft;
    static string _resultMsg = string.Empty;
    static float _animTimer;

    public static void InitResults(float time, MissionEndContext ctx) {
        if (Systems.Troves.TroveRun.Active || UI.MainMenu.MainMenuUI.IsActive) return; // Troves has its own banners
        timeLeft = time;
        _animTimer = 0f;
        _resultMsg = ctx switch {
            MissionEndContext.GameOver => "Game Over!",
            MissionEndContext.Win => "Mission Complete!",
            MissionEndContext.Lose => "You Died!",
            MissionEndContext.CampaignCompleteMajor => "Campaign Complete!",
            MissionEndContext.CampaignCompleteMinor => "Campaign Complete!",
            _ => string.Empty
        };
    }

    public static void DrawResultsMessage(SpriteBatch sb) {
        if (timeLeft <= 0 || string.IsNullOrEmpty(_resultMsg)) return;
        timeLeft -= RuntimeData.DeltaTime;

        _animTimer = MathF.Min(_animTimer + 0.025f * RuntimeData.DeltaTime, 2f);
        float slideEase = Easings.OutCubic(MathHelper.Clamp(_animTimer, 0f, 1f));
        float impactEase = Easings.OutSine(MathHelper.Clamp(_animTimer - 1f, 0f, 1f));

        var font = FontGlobals.RebirthFontLarge;
        float res = WindowUtils.WindowHeight / 1080f;
        int mid = _resultMsg.Length / 2;
        string left = _resultMsg[..mid], right = _resultMsg[mid..];
        var leftSize = font.MeasureString(left) * res;
        var rightSize = font.MeasureString(right) * res;

        float gapX = MathHelper.Lerp(1200f * res, 0f, slideEase);
        float alpha = slideEase;
        if (_animTimer > 1.6f) alpha *= 1f - (_animTimer - 1.6f) / 0.4f;

        Color textColor = Color.White;
        float scale = 1f;
        if (_animTimer >= 0.8f) {
            textColor = Color.Lerp(Color.Goldenrod, IntermissionSystem.BackgroundColor, impactEase);
            scale = 1f + 0.15f * (1f - impactEase);
        }
        // the text sits a little above the middle so it doesn't cover the tanks' last moments
        var centre = WindowUtils.WindowCenter - new Vector2(0, WindowUtils.WindowHeight * 0.12f);
        Vector2 leftPos = centre - new Vector2(gapX + rightSize.X / 2f, 0);
        Vector2 rightPos = centre + new Vector2(gapX + leftSize.X / 2f, 0);

        if (slideEase < 1f) { // afterimages while sliding
            foreach (var (trail, a) in new[] { (220f, 0.2f), (100f, 0.45f) }) {
                float t = trail * res * (1f - slideEase);
                DrawUtils.DrawStringWithBorder(sb, font, left, leftPos - new Vector2(t, 0), Color.Red * a * alpha, Color.Transparent, new Vector2(res), 0f);
                DrawUtils.DrawStringWithBorder(sb, font, right, rightPos + new Vector2(t, 0), Color.Red * a * alpha, Color.Transparent, new Vector2(res), 0f);
            }
        }
        DrawUtils.DrawStringWithBorder(sb, font, left, leftPos, textColor * alpha, Color.Black * alpha, new Vector2(res * scale), 0f, borderThickness: 2f * res);
        DrawUtils.DrawStringWithBorder(sb, font, right, rightPos, textColor * alpha, Color.Black * alpha, new Vector2(res * scale), 0f, borderThickness: 2f * res);
    }

    public static void DoEndScene(TimeSpan delay, MissionEndContext context) {
        // i think this works.
        // only adjusts speed for when this is called.
        Task.Run(async () => {
            await Task.Delay(delay).ConfigureAwait(false);
            CampaignCompleteUI.PerformSequence(context);
        });
    }
    public static void HandleSceneVisuals() {
        if (Difficulties.Types["ThunderMode"])
            DoThunderStuff();
        else if (GameScene.Theme == MapTheme.Christmas) {
            GameLight.Color = new(50, 50, 50, 50);
            GameLight.Brightness = 0.4f;
            GameLight.Apply(false);
            RenderGlobals.BackBufferColor = (Color.DeepSkyBlue.ToVector3() * 0.2f).ToColor();
            if (Client.ClientRandom.NextFloat(0, 1) <= 0.3f) {

                // TODO: add some sort of snowflake limit because the damn renderer sucks ass.

                float y = 200f;

                float x = Client.ClientRandom.NextFloat(-450f, 450f);
                float z = Client.ClientRandom.NextFloat(-250f, 400f);

                int snowflake = Client.ClientRandom.Next(0, 2);

                var p = GameHandler.Particles.MakeParticle(new Vector3(x, y, z), GameResources.GetGameResource<Texture2D>($"Assets/christmas/snowflake_{snowflake}"));

                p.Scale = new Vector3(Client.ClientRandom.NextFloat(0.1f, 0.25f));

                Vector2 wind = new(0.05f, 0f);

                float weight = Client.ClientRandom.NextFloat(0.05f, 0.15f);

                float rotFactor = Client.ClientRandom.NextFloat(0.001f, 0.01f);

                p.UniqueBehavior = (a) => {
                    if (p.Position.Y <= 0) {
                        GeometryUtils.Add(ref p.Scale, -0.006f * RuntimeData.DeltaTime);
                        if (p.Scale.X <= 0)
                            p.Destroy();

                    }
                    else {
                        p.Position.X += wind.X * RuntimeData.DeltaTime;
                        p.Position.Y -= weight;
                        p.Position.Z += wind.Y * RuntimeData.DeltaTime;

                        p.Rotation2D += 0.01f * RuntimeData.DeltaTime;

                        p.Roll += rotFactor * RuntimeData.DeltaTime;

                        p.Pitch += (rotFactor / 2) * RuntimeData.DeltaTime;
                    }
                };
            }
        }
    }
    private static void DoThunderStuff() {
        if (IntermissionSystem.BlackAlpha > 0 || IntermissionSystem.Alpha >= 1f || MainMenuUI.IsActive || GameUI.Paused) {
            if (Thunder.SoftRain!.IsPlaying()) {
                Thunder.SoftRain.Instance.Stop();
                // maybe black instead? don't think it matters tho since models are rendered in place of it
                RenderGlobals.BackBufferColor = Color.Transparent;

                GameLight.Color = new(150, 150, 170);
                GameLight.Brightness = 0.71f;

                GameLight.Apply(false);
            }
            return;
        }
        if (!Thunder.SoftRain.IsPlaying())
            Thunder.SoftRain.Instance.Play();
        Thunder.SoftRain.Instance.Volume = TankGame.Settings.AmbientVolume;


        // TODO: should the chance be scaled by tps?
        if (Client.ClientRandom.NextFloat(0, 1f) <= 0.003f * RuntimeData.DeltaTime) {
            var rand = new Range<byte>((byte)Thunder.ThunderType.Fast, (byte)Thunder.ThunderType.Instant2);
            var type = (Thunder.ThunderType)Client.ClientRandom.Next(rand.Min, rand.Max);

            if (!Thunder.Thunders.Any(x => x is not null && x.Type == type))
                new Thunder(type);
        }

        Thunder brightest = null;

        float minThresh = 0.05f;

        foreach (var thun in Thunder.Thunders) {
            if (thun is not null) {
                thun.Update();

                if (brightest is null)
                    brightest = thun;
                else if (thun.CurBright > brightest.CurBright && thun.CurBright > minThresh)
                    brightest = thun;
            }
        }

        GameLight.Color = Color.Multiply(Color.DeepSkyBlue, 0.5f); // DeepSkyBlue


        if (brightest is not null && brightest.CurBright > minThresh) {
            RenderGlobals.BackBufferColor = Color.DeepSkyBlue * brightest.CurBright;
            GameLight.Brightness = brightest.CurBright / 2;
            Console.WriteLine("no balls");
        }
        else {
            Console.WriteLine("balls");
            GameLight.Brightness = minThresh;
        }

        GameLight.Apply(false);
    }
    public static void CleanupScene(bool sync = false) {
        if (sync)
            Client.SyncCleanup();

        foreach (var mine in Mine.AllMines)
            mine?.Remove();

        foreach (var bullet in Shell.AllShells)
            bullet?.Remove();

        foreach (var expl in Explosion.Explosions)
            expl?.Remove();

        foreach (var crate in Crate.crates)
            crate?.Remove();

        foreach (var pu in Powerup.Powerups)
            pu?.Remove();

        foreach (var plane in Airplane.AllPlanes)
            plane?.Remove();

        ClearTankDeathmarks();
        ClearTankTracks();

        OnMissionCleanup?.Invoke();
    }
    public static void LoadGameScene() {
        if (!_musicLoaded) {
            OnLoadTankScene?.Invoke();
            _musicLoaded = true;
        }
        else {
            foreach (var song in TankMusicSystem.Audio)
                song.Value.Stop();
            TankMusicSystem.SnowLoop.Stop();
            TankMusicSystem.SnowLoop.Play();
        }
    }
    public static void ClearTankDeathmarks() {
        for (int i = 0; i < TankDeathMark.deathMarks.Length; i++) {
            TankDeathMark.deathMarks[i]?.check?.Destroy();
            TankDeathMark.deathMarks[i] = null;
        }
        for (int i = 0; i < GameHandler.AllTanks.Length; i++) {
            var tank = GameHandler.AllTanks[i];
            if (tank is null) continue;
            if (tank.IsDestroyed) tank.Remove(true);
        }

        TankDeathMark.total_death_marks = 0;
    }
    public static void CleanupEntities(bool cleanAiOnly = false) {
        for (int a = 0; a < Block.AllBlocks.Length; a++)
            Block.AllBlocks[a]?.Remove();
        if (!cleanAiOnly) {
            for (int a = 0; a < GameHandler.AllTanks.Length; a++)
                GameHandler.AllTanks[a]?.Remove(true);
        }
        else {
            for (int a = 0; a < GameHandler.AllPlayerTanks.Length; a++)
                GameHandler.AllPlayerTanks[a]?.Remove(true);
        }
    }
    public static void ClearTankTracks() {
        for (int i = 0; i < TankFootprint.AllFootprints.Length; i++) {
            TankFootprint.AllFootprints[i]?.Remove();
            TankFootprint.AllFootprints[i] = null;
        }
    }
    public static void StartTnkScene() {
        DebugManager.DebuggingEnabled = false;

        IntermissionSystem.TextAnimatorLarge?.Restart();
        IntermissionSystem.TextAnimatorSmall?.Restart();
        GameLight.Apply(false);
    }
}
