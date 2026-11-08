using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
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
    /// <summary>How bright an explosion's light still is when the explosion ends (0-1).</summary>
    public const float ExplosionEndLife = 0.6f;

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
        ForceHighPrecision(effect);
        if (!LightingSystem.Initialize(device, effect))
            return;

        // phones: the room and the board's frame (the table and the wooden border around the board) are left
        // completely alone, so they draw exactly as with lighting off. On phone GPUs lighting turned a band
        // around the board black; lighting the play area (floor, blocks, tanks, shells) is what matters.
        UpdateExcludedModels();

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
            cur.SetQualityLevel(QualityFromSettings);
            cur.RoomShadows = AllowRoomCascade;
            cur.TimeOfDay = Preset.DayCycle; // follows the real time of day (see Update)
            DynLightSettings.Current = cur;
            LightingPresets.DayCyclePaused = true;
            LightingPresets.Hour = RealHour;
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

    /// <summary>
    /// Phones: the room's sun shadows (walls and windows blocking the sun) are off. That shadow map is drawn once and
    /// reused for several frames, and phone GPUs don't keep a texture's contents between frames: the reused copy read
    /// as "all in shadow", which drew the table and everything around the board black. The board's own shadows are
    /// drawn every frame and unaffected.
    /// </summary>
    public static bool PhoneGpu => OperatingSystem.IsAndroid();

    /// <summary>
    /// Phones: how much of the scene around the board gets lighting (Options > Graphics > "Lit:"), least risky first.
    /// 0 = only the play area (known good), 1 = + the room and table, 2 = + sun through the windows (the room
    /// shadow map), 3 = + the board's wooden border (its see-through shadow overlays and its unplaced outer pieces
    /// stay out of lighting on phones). PCs always get everything.
    /// </summary>
    // phones stop at 2: lighting the board's wooden border drew it black on a Red Magic 11 Pro (even without its overlays)
    public static int Surroundings => PhoneGpu ? 2 : 3; // phones: play area, room and window sun (everything that works)
    public static bool AllowRoomCascade => Surroundings >= 2;

    /// <summary>The device's clock as hours (6.5 = 6:30 AM).</summary>
    static float RealHour { get { var n = DateTime.Now; return n.Hour + n.Minute / 60f + n.Second / 3600f; } }

    static void UpdateExcludedModels() {
        LightingSystem.NeverInstrument.Clear();
        if (Surroundings < 1 && RoomScene.RoomSkyboxScene is { } room)
            LightingSystem.NeverInstrument.Add(room);
        var borders = new[] { GameScene.BoundsRenderer.BoundaryModel, TanksRebirth.GameContent.Globals.Assets.ModelGlobals.GameBoundary.Asset };
        if (Surroundings < 3)
            foreach (var m in borders)
                if (m is not null) LightingSystem.NeverInstrument.Add(m);
        // phones: the border's see-through shadow overlays (the soft dark edges laid on the table) and its outer pieces
        // (drawn without being placed) stay out of lighting; the solid wood frame is lit. ("+ Border" went black with them.)
        foreach (var m in borders) {
            if (m is null) continue;
            foreach (var mesh in m.Meshes) {
                bool overlay = mesh.Name is "polygon2" or "polygon5" or "polygon7" or "polygon15" or "polygon20" or "polygon21"
                    || mesh.Name.Contains("outer", StringComparison.OrdinalIgnoreCase);
                if (PhoneGpu && overlay) LightingSystem.SetMeshLighting(mesh, new MeshLighting { Ignore = true });
            }
        }
    }

    /// <summary>Applies a new "Lit:" level right away: models are put back to their own effects and handed to lighting again.</summary>
    public static void ApplySurroundings() {
        if (!_initialized)
            return;
        UpdateExcludedModels();
        bool on = DynLightSettings.Current.Enabled;
        LightingSystem.UninstrumentAll();
        if (on) {
            foreach (var m in GameResources.CachedModels())
                LightingSystem.Instrument(m);
            LightingSystem.Instrument(RoomScene.RoomSkyboxScene);
        }
        var cur = DynLightSettings.Current;
        cur.RoomShadows = AllowRoomCascade;
        DynLightSettings.Current = cur;
        cur.Apply(force: true);
    }

    static LightingQualityLevel QualityFromSettings =>
        (LightingQualityLevel)Math.Clamp(TankGame.Settings?.LightingQuality ?? 0, 0, (int)LightingQualityLevel.Ultra);

    /// <summary>The Graphics switch: turns dynamic lighting on (loading it the first time) or off, at the quality in the settings.</summary>
    public static void SetEnabled(bool on) {
        if (on && !_initialized)
            Initialize(TankGame.Instance.GraphicsDevice);
        if (!_initialized)
            return;
        if (on && !InstrumentNewModels) {
            // back on after being switched off: models join in again
            foreach (var m in GameResources.CachedModels())
                LightingSystem.Instrument(m);
            LightingSystem.Instrument(RoomScene.RoomSkyboxScene);
            InstrumentNewModels = true;
        }
        if (!on) {
            // off means off: every model draws with its original effects again, exactly as without lighting
            InstrumentNewModels = false;
            LightingSystem.UninstrumentAll();
        }
        var cur = DynLightSettings.Current;
        cur.Enabled = on;
        cur.SetQualityLevel(QualityFromSettings);
        cur.RoomShadows = AllowRoomCascade;
        DynLightSettings.Current = cur;
        cur.Apply();
    }

    /// <summary>
    /// Phones: MonoGame compiles every pixel shader at "mediump" (half) precision. The lighting shader keeps shadow depths
    /// and world positions in its math, which half precision can't hold, so on real phone GPUs shadows came out as
    /// blocky black patches (emulators run everything at full precision, so they looked fine). This switches the
    /// lighting shader to full precision wherever the GPU supports it, before it's first used.
    /// </summary>
    static void ForceHighPrecision(Effect effect) {
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        int patched = 0;
        try {
            if (typeof(Effect).GetField("_shaders", F)?.GetValue(effect) is not Array shaders)
                return;
            foreach (var sh in shaders) {
                var code = sh?.GetType().GetField("_glslCode", F);
                if (code?.GetValue(sh) is not string glsl || !glsl.Contains("precision mediump float;"))
                    continue;
                glsl = glsl.Replace("precision mediump float;",
                    "\n#ifdef GL_FRAGMENT_PRECISION_HIGH\nprecision highp float;\n#else\nprecision mediump float;\n#endif\n");
                glsl = glsl.Replace("precision mediump int;", "\n#ifdef GL_FRAGMENT_PRECISION_HIGH\nprecision highp int;\n#else\nprecision mediump int;\n#endif\n");
                code.SetValue(sh, glsl);
                patched++;
            }
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"[Lighting] couldn't raise shader precision: {e.Message}", LogType.Warn);
        }
        TankGame.ClientLog.Write($"[Lighting] full precision for {patched} shader(s).", LogType.Info);
    }

    public static void Apply(Preset preset) {
        LightingPresets.Apply(preset, RoomScene.RoomSkyboxScene);
    }

    public static void Cycle() => Apply(LightingPresets.Next(Current));

    static float _reportIn = 120;
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

        // the light through the windows follows the real time of day (like the clock on the wall)
        if (LightingPresets.Current == Preset.DayCycle)
            LightingPresets.Hour = RealHour;

        // a short report every ~10 s while lighting is on (shows in the debug readout)
        if (LightingSystem.IsActive && (_reportIn -= RuntimeData.DeltaTime) <= 0) {
            _reportIn = 600;
            var a = LightingSystem.Ambient;
            var sun = LightingSystem.Sun;
            TankGame.ClientLog.Write($"[Lighting] {LightingPresets.Current} q={DynLightSettings.Current.QualityLevel} " +
                $"ambient {a.Intensity:0.00} sky {a.Sky} ground {a.Ground} sun {sun.Intensity:0.00} bg {LightingSystem.BackgroundLight:0.00} | {LightingSystem.Stats}", LogType.Info);
        }

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
    /// <summary>How dark gameplay wants the scene (0 = normal, 1 = very dark; e.g. a haunted or factory floor). Dark scenes switch the headlights on.</summary>
    public static Func<float>? SceneDarkness;
    static float _dark;
    static Vector3 _strikeDirection = Vector3.Down;

    static void UpdateModifierLooks() {
        _storm = Difficulties.Types["ThunderMode"] && !MainMenuUI.IsActive;
        _lantern = Difficulties.Types["LanternMode"] && !MainMenuUI.IsActive;

        float dark = 0f;
        try { dark = MainMenuUI.IsActive ? 0f : MathHelper.Clamp(SceneDarkness?.Invoke() ?? 0f, 0f, 1f); }
        catch { SceneDarkness = null; }
        bool darkChanged = MathF.Abs(dark - _dark) > 0.01f;
        _dark = dark;
        var filtering = _storm || _lantern || _dark > 0f;
        if (!filtering && !_filtering && !darkChanged)
            return;
        if (filtering == _filtering && !darkChanged && !_storm && !_lantern && _dark > 0f)
            return; // (nothing new: the dark look is already applied)
        _filtering = filtering;
        LightingPresets.Filter = filtering ? FilterLook : null;

        // (our thunder doesn't report its flashes, so the storm is just overcast here)

        // static presets apply their look only once, so it's applied again while the modifiers change it (and once more to undo them)
        LightingPresets.Refresh(RoomScene.RoomSkyboxScene);
    }

    static void FilterLook(ref LightingPresets.TimeOfDay time) {
        if (_dark > 0f) {
            // a darker room for moody floors; the tanks' headlights come on
            float keep = 1f - _dark * 0.6f;
            time.Ambient *= keep;
            time.Background *= keep;
            time.SunIntensity *= 1f - _dark;
            time.ShaftDensity *= 1f - _dark;
            time.Sky = Color.Lerp(time.Sky, new Color(20, 22, 40), _dark * 0.5f);
            time.Ground = Color.Lerp(time.Ground, new Color(10, 10, 16), _dark * 0.5f);
            time.TableLamp *= keep; time.DeskLamp *= keep; time.ClockLight *= keep; time.FloorLamp *= keep;
            if (_dark > 0.25f) {
                time.Headlights = 1f;
                time.DynamicBrightness = MathF.Max(time.DynamicBrightness, 1.2f);
            }
        }
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

    /// <summary>A tank's headlight colour (items, bosses); null = the normal warm white.</summary>
    public static Func<Tank, Color?>? HeadlightTint;
    /// <summary>Tanks other than the players that get headlights too (bosses).</summary>
    public static Func<IEnumerable<Tank>>? ExtraHeadlightTanks;
    /// <summary>More lights from gameplay (floor themes: alarm lights, crystals).</summary>
    public static Action? ExtraLights;

    static void CollectLights() {
        GameplayLights.BeginFrame();
        GameplayLights.AddFlashes();
        try { ExtraLights?.Invoke(); }
        catch (Exception e) { TankGame.ClientLog.Write($"Extra lights: {e.Message}", TanksRebirth.Internals.LogType.Warn); ExtraLights = null; }

        var anySortOfTankLighting = LightingPresets.HeadlightsEnabled || _lantern;
        if (anySortOfTankLighting) {
            var lights = LightingPresets.HeadlightsEnabled;
            foreach (var tank in GameHandler.AllPlayerTanks) {
                if (tank is null || tank.IsDestroyed) continue;

                if (lights) GameplayLights.AddHeadlight(tank.TurretPosition3D - new Vector3(0, 5, 0), tank.TurretPosition - tank.Position, tank == PlayerTank.ClientTank,
                    HeadlightTint?.Invoke(tank) ?? GameplayLights.DefaultHeadlight);
                if (_lantern) {
                    // RuntimeData.RunTime produces weird results
                    var time = (float)RuntimeData.RunTime / 60f;
                    GameplayLights.AddLantern(tank.Position3D + new Vector3(0, LanternHeight, 0), time + tank.PlayerId * 1.7f, tank == PlayerTank.ClientTank);
                }
            }
            if (lights && ExtraHeadlightTanks is { } extra)
                foreach (var tank in extra())
                    if (tank is { IsDestroyed: false })
                        GameplayLights.AddHeadlight(tank.TurretPosition3D - new Vector3(0, 5, 0), tank.TurretPosition - tank.Position, false,
                            HeadlightTint?.Invoke(tank) ?? GameplayLights.DefaultHeadlight);
        }

        // at night or on a dark floor, every plain shell gives off a faint white glow so it's easy to see
        bool darkScene = LightingPresets.HeadlightsEnabled || _dark > 0.25f;
        if (ShellLights) {
            foreach (var shell in Shell.AllShells) {
                if (shell is null) continue;
                if (darkScene && shell.Tint is null && !shell.Rainbow)
                    GameplayLights.AddGlow(shell.RenderPosition3D + new Vector3(0f, 4f, 0f), new Color(235, 240, 255), 0.7f, 26f);
                // coloured shells (Tank Troves stat items) glow their own colour, with a burst as they leave the barrel
                if (shell.Tint is not null || shell.Rainbow) {
                    var glow = shell.Rainbow ? shell.CurrentRainbowColor : shell.Tint!.Value;
                    float burst = MathHelper.Clamp(1f - shell.LifeTime / 10f, 0f, 1f);
                    GameplayLights.AddColoredShellLight(shell.Position3D, glow, burst);
                    continue;
                }
                if (shell.Type is not (TanksRebirth.GameContent.ID.ShellID.TrailedRocket or TanksRebirth.GameContent.ID.ShellID.Missile or TanksRebirth.GameContent.ID.ShellID.Rocket)) continue;

                var color = Color.Orange;
                GameplayLights.AddShellLight(shell.Position3D - Vector3.Normalize(shell.Velocity3D) * 20f, color);
            }
        }

        // a boosting tank's flame trail lights up the ground behind it (flickering)
        if (GameContent.Systems.Troves.TroveRun.Active) {
            foreach (var t in GameContent.GameHandler.AllPlayerTanks) {
                if (t is not { IsDestroyed: false } pt || pt.PlayerId is < 0 or > 3 || !GameContent.Systems.Troves.TroveItems.Players[pt.PlayerId].Boosting)
                    continue;
                var back = pt.Velocity.LengthSquared() > 0.01f ? -Vector2.Normalize(pt.Velocity) : Vector2.Zero;
                float flicker = 0.85f + 0.15f * MathF.Sin((float)GameContent.Systems.Troves.TroveRun.PlayTime * 31f + pt.PlayerId);
                GameplayLights.AddGlow(pt.Position3D + new Vector3(back.X * 16f, 5f, back.Y * 16f), new Color(255, 140, 40), 1.5f * flicker, 70f);
            }
        }

        // neon and rainbow tank skins light up around them in the dark
        if (darkScene) {
            foreach (var t in GameContent.GameHandler.AllPlayerTanks) {
                if (t is not { } pt || pt.IsDestroyed || pt.ColorIndex < 0)
                    continue;
                if (GameContent.Systems.PlayerColors.GlowOf(pt.ColorIndex) is { } glow)
                    GameplayLights.AddGlow(pt.Position3D + new Vector3(0f, 6f, 0f), glow, 0.9f, 60f);
            }
        }

        if (MineLights) {
            foreach (var mine in Mine.AllMines) {
                if (mine is null) continue;
                bool flashingRed = mine.DetonateTime < Mine.TICKS_OF_FLASHING && (int)(mine.DetonateTime / 8f) % 2 == 0;
                if (flashingRed)
                    GameplayLights.AddMineLight(mine);
                else // a steady soft yellow glow
                    GameplayLights.AddGlow(mine.Position3D + new Vector3(0f, 7f, 0f), new Color(255, 214, 70), 0.9f, 44f);
            }
        }

        if (ExplosionLights) {
            foreach (var explosion in Explosion.Explosions) {
                if (explosion is null)
                    continue;
                // full brightness for as long as the fireball is up, only easing off at the very end
                // (Explosion.Remove hands over to a short flash that covers the last flames)
                var t = explosion.LifeTime / MathF.Max(explosion.LingerDuration, 1f);
                var life = t < 0.7f ? 1f : MathHelper.Lerp(1f, ExplosionEndLife, MathHelper.SmoothStep(0f, 1f, (t - 0.7f) / 0.3f));
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
