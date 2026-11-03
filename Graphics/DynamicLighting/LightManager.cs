using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using TanksRebirth.GameContent;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.Systems.CommandsSystem;
using TanksRebirth.GameContent.RebirthUtils;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.GameContent.UI.MainMenu;
using TanksRebirth.Internals;
using TanksRebirth.Internals.Common;
using TanksRebirth.Internals.Common.Utilities;
using Preset = TanksRebirth.Graphics.DynamicLighting.LightingPresets.Preset;
using TanksRebirth.Net;

namespace TanksRebirth.Graphics.DynamicLighting;

/// <summary>
/// Manages lighting for the lighting system.
/// </summary>
public static class LightManager {
    public static bool ShellLights = true;
    public static bool ExplosionLights = true;
    public static bool MineLights = true;

    /// <summary>Lets the room cast sun shadows.</summary>
    public static bool RoomCastsShadows {
        get => _roomCastsShadows;
        set {
            _roomCastsShadows = value;
            if (_initialized && LightingSystem.IsAvailable) {
                LightingSystem.SetCastsShadows(RoomScene.RoomSkyboxScene, value);
                SetRoomSeals(value);
            }
        }
    }
    // change this default to start with room shadows off
    static bool _roomCastsShadows = true;

    public static Preset Current => LightingPresets.Current;
    static Matrix[] _roomBones = [];
    static bool _initialized;

    /// <summary>Loads the lighting effect and hooks everything up. If the effect is missing the game runs as before.</summary>
    public static void Initialize(GraphicsDevice device) {
        if (_initialized)
            return;
        _initialized = true;

        Effect effect;
        try {
            effect = GameResources.GetGameResource<Effect>("Assets/shaders/lighting/lighting");
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"[Lighting] lighting.fx could not be loaded, dynamic lighting is off: {e.Message}", LogType.Warn);
            return;
        }
        if (!LightingSystem.Initialize(device, effect))
            return;

        // the sky dome around the room should neither block nor receive light
        var room = RoomScene.RoomSkyboxScene;
        LightingSystem.Instrument(room);
        LightingSystem.SetMeshLighting(room, "Sphere", new MeshLighting { Ignore = true });
        LightingSystem.SetCastsShadows(room, _roomCastsShadows);
        SetRoomSeals(_roomCastsShadows);

        LightingSystem.CollectLights += CollectLights;
        LightingSystem.CollectShadowCasters += CollectShadowCasters;


        // the lighting options live in the game's settings file (phones: on/off, at low quality)
        {
            var cur = DynLightSettings.Current;
            cur.Enabled = TankGame.Settings?.DynamicLighting == true;
            if (Internals.Common.Framework.Input.TouchControls.Enabled)
                cur.SetQualityLevel(LightingQualityLevel.Low);
            DynLightSettings.Current = cur;
        }

        // every model already loaded joins in; models loaded from now on join as they load
        foreach (var m in GameResources.CachedModels())
            LightingSystem.Instrument(m);
        InstrumentNewModels = true;

        // keep the settings in sync when F7 or a command changes the time of day
        LightingPresets.PresetChanged += preset => DynLightSettings.Current.TimeOfDay = preset;
        DynLightSettings.Current.Apply(force: true);

        TankGame.ClientLog.Write("Lighting system loaded.", LogType.Info);
    }

    /// <summary>Set once lighting has loaded: <see cref="GameResources"/> hands it every newly loaded model.</summary>
    public static bool InstrumentNewModels;

    /// <summary>The Graphics switch: turns dynamic lighting on (loading it the first time) or off.</summary>
    public static void SetEnabled(bool on) {
        if (on && !_initialized)
            Initialize(TankGame.Instance.GraphicsDevice);
        if (!_initialized)
            return;
        var cur = DynLightSettings.Current;
        cur.Enabled = on;
        DynLightSettings.Current = cur;
        cur.Apply();
    }

    public static void Apply(Preset preset) {
        LightingPresets.Apply(preset, RoomScene.RoomSkyboxScene);
    }

    public static void Cycle() => Apply(LightingPresets.Next(Current));

    static float temp = 1f;
    static sbyte sw = 1;

    /// <summary>Hotkeys and animation. Call from the game's Update.</summary>
    public static void Update() {
        if (!_initialized)
            return;


        if (LightingSystem.BeforeAfter) {
            if (InputUtils.KeyJustPressed(Keys.NumPad5)) sw *= -1;

            temp += 1f * RuntimeData.DeltaTime * sw;
            temp = MathHelper.Clamp(temp, 0, 1);

            LightingSystem.BeforeAfterPercent = Easings.InOutCubic(temp);
            // LightingSystem.BeforeAfterPercent = MathHelper.Clamp(MouseUtils.Test.X, 0, 1);
        }

        UpdateModifierLooks();

        LightingPresets.UpdateDayCycle(RuntimeData.DeltaTime, RoomScene.RoomSkyboxScene);
    }

    // modifiers

    /// <summary>Height of the lanterns above the tanks. Lower than the top of a tank and the tank shadows everything around it.</summary>
    public static float LanternHeight = 30f;
    /// <summary>How much of the time of day's light is left during a storm.</summary>
    public static float StormDarkness = 0.4f;
    public static Color StormSky = new(70, 80, 104);
    public static Color StormGround = new(36, 38, 46);
    public static Color LightningColor = new(196, 214, 255);
    /// <summary>Sun intensity of the brightest lightning.</summary>
    public static float LightningIntensity = 1.5f;

    static bool _storm, _lantern, _filtering;
    static Vector3 _strikeDirection = Vector3.Down;

    static void UpdateModifierLooks() {
        _storm = Difficulties.Types["ThunderMode"] && !MainMenuUI.IsActive;
        _lantern = Difficulties.Types["LanternMode"] && !MainMenuUI.IsActive;

        var filtering = _storm || _lantern;
        if (!filtering && !_filtering)
            return;
        _filtering = filtering;
        LightingPresets.Filter = filtering ? FilterLook : null;

        // (our thunder doesn't report its flashes, so the storm is just overcast here)

        // static presets apply their look only once, so it's applied again while the modifiers change it (and once more to undo them)
        LightingPresets.Refresh(RoomScene.RoomSkyboxScene);
    }

    static void FilterLook(ref LightingPresets.TimeOfDay time) {
        if (_lantern) {
            // everything is dark, only the lanterns light the board
            time.Sky = new Color(22, 24, 38);
            time.Ground = new Color(10, 10, 14);
            time.Ambient = 0.3f;
            time.SunIntensity = 0f;
            time.ShaftDensity = 0f;
            time.TableLamp = time.DeskLamp = time.ClockLight = time.FloorLamp = 0f;
            time.Background = 0.05f;
            time.Headlights = 0f;
            time.DynamicBrightness = 1.3f;
        }

        if (!_storm)
            return;

        // overcast: no direct sun, and a darker, bluer version of the time of day
        time.Sky = Color.Lerp(time.Sky, StormSky, 0.6f);
        time.Ground = Color.Lerp(time.Ground, StormGround, 0.6f);
        time.Ambient *= StormDarkness;
        time.Background *= StormDarkness;
        if (!_lantern) {
            time.Headlights = 1f;
            time.DynamicBrightness = MathF.Max(time.DynamicBrightness, 1f);
        }

        // the lightning takes the place of the sun, so it casts shadows through the windows
        const float flash = 0f;
        time.SunDirection = _strikeDirection;
        time.SunColor = LightningColor;
        time.SunIntensity = flash * LightningIntensity;
        time.SunWrap = 0.3f;
        time.ShadowRadius = 620f;
        time.ShaftColor = LightningColor;
        time.ShaftDensity = flash * 0.3f;
        time.ShaftAnisotropy = 0.4f;
        time.ShaftMarch = 1800f;
        time.Sky = Color.Lerp(time.Sky, LightningColor, flash * 0.5f);
        time.Ambient += flash * 0.5f;
        time.Background += flash * 0.8f;
    }

    // picks a lightning strike direction... should probably sync at some point
    static Vector3 PickStrikeDirection() {
        var rng = Client.ClientRandom;
        var to = new Vector3(rng.NextFloat(-200f, 200f), 0f, rng.NextFloat(-150f, 150f));
        var from = rng.Next(2) == 0
            ? LightingPresets.BackWindows + new Vector3(rng.NextFloat(-400f, 300f), rng.NextFloat(0f, 150f), 0f)
            : LightingPresets.SideWindows + new Vector3(0f, rng.NextFloat(0f, 350f), rng.NextFloat(-500f, 0f));
        return Vector3.Normalize(to - from);
    }

    static void CollectShadowCasters() {
        if (!RoomCastsShadows || !GameScene.ShouldRenderAll || !LightingSystem.Sun.Enabled)
            return;

        // the room is frustum culled by RoomScene, but its walls and windows still have to block the sun
        var room = RoomScene.RoomSkyboxScene;
        if (_roomBones.Length != room.Bones.Count)
            _roomBones = new Matrix[room.Bones.Count];
        room.CopyAbsoluteBoneTransformsTo(_roomBones);

        foreach (var mesh in room.Meshes) {
            if (mesh.Name.Contains("glass", StringComparison.OrdinalIgnoreCase))
                continue;
            LightingSystem.SubmitShadowCaster(mesh, _roomBones[mesh.ParentBone.Index]);
        }
    }

    static void CollectLights() {
        GameplayLights.BeginFrame();

        var anySortOfTankLighting = LightingPresets.HeadlightsEnabled || _lantern;
        if (anySortOfTankLighting) {
            var lights = LightingPresets.HeadlightsEnabled;
            foreach (var tank in GameHandler.AllPlayerTanks) {
                if (tank is null || tank.IsDestroyed) continue;

                if (lights) GameplayLights.AddHeadlight(tank.TurretPosition3D - new Vector3(0, 5, 0), tank.TurretPosition - tank.Position, tank == PlayerTank.ClientTank);
                if (_lantern) {
                    // RuntimeData.RunTime produces weird results
                    var time = (float)RuntimeData.RunTime / 60f;
                    GameplayLights.AddLantern(tank.Position3D + new Vector3(0, LanternHeight, 0), time + tank.PlayerId * 1.7f, tank == PlayerTank.ClientTank);
                }
            }
        }

        if (ShellLights) {
            foreach (var shell in Shell.AllShells) {
                if (shell is null) continue;
                if (shell.Type is not (TanksRebirth.GameContent.ID.ShellID.TrailedRocket or TanksRebirth.GameContent.ID.ShellID.Missile or TanksRebirth.GameContent.ID.ShellID.Rocket)) continue;

                var color = Color.Orange;
                GameplayLights.AddShellLight(shell.Position3D - Vector3.Normalize(shell.Velocity3D) * 20f, color);
            }
        }

        if (MineLights) {
            foreach (var mine in Mine.AllMines) {
                if (mine is null) continue;
                if (mine.DetonateTime >= Mine.TICKS_OF_FLASHING || (int)(mine.DetonateTime / 8f) % 2 != 0) continue; // flashing red

                // only creates a light when flashing red
                GameplayLights.AddMineLight(mine);
            }
        }

        if (ExplosionLights) {
            foreach (var explosion in Explosion.Explosions) {
                if (explosion is null)
                    continue;
                var life = 1f - explosion.LifeTime / MathF.Max(explosion.LingerDuration, 1f);
                GameplayLights.AddExplosionLight(explosion.Position3D, explosion.DamageRadiusScale * Explosion.MAGIC_EXPLOSION_NUMBER, life);
            }
        }
    }

    /// <summary>
    /// The room model has small gaps where the walls meet the ceiling, which let thin lines of sun through when the
    /// sun is in the right spot. These invisible blockers sit in the walls above the windows and on top of the ceiling to seal them.
    /// (Only while the room casts shadows: without walls they would just block the sun.)
    /// </summary>
    static void SetRoomSeals(bool enabled) {
        LightingSystem.SunBlockers.Clear();
        if (!enabled)
            return;
        const float ceiling = 2131f, top = 2400f, sealFrom = 2000f, overlap = 6f;
        // the outer walls are at x = -3500 / 1986 and z = -358 / 3300, the windows sit ~60 further out
        float west = -3500f, east = 1986f, back = -358f, front = 3300f, depth = 70f;
        // a lid just above the ceiling, no wider than the walls (wider would cut off sun coming in through the windows)
        LightingSystem.SunBlockers.Add(new BoundingBox(new Vector3(west - overlap, ceiling, back - overlap), new Vector3(east + overlap, top, front + overlap)));
        // thin bands in the walls above the windows, covering the gap between the top of the walls and the ceiling
        LightingSystem.SunBlockers.Add(new BoundingBox(new Vector3(east - overlap, sealFrom, back - depth), new Vector3(east + depth, top, front + depth)));
        LightingSystem.SunBlockers.Add(new BoundingBox(new Vector3(west - depth, sealFrom, back - depth), new Vector3(east + depth, top, back + overlap)));
        LightingSystem.SunBlockers.Add(new BoundingBox(new Vector3(west - depth, sealFrom, back - depth), new Vector3(west + overlap, top, front + depth)));
        LightingSystem.SunBlockers.Add(new BoundingBox(new Vector3(west - depth, sealFrom, front - overlap), new Vector3(east + depth, top, front + depth)));
    }
}
