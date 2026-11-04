using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using TanksRebirth.GameContent;
using TanksRebirth.GameContent.Systems.ParticleSystem;

namespace TanksRebirth.Graphics.DynamicLighting;

/// <summary>
/// The look of the lights that follow gameplay objects (headlights, shells, mines, explosions). Takes plain
/// positions so it doesn't depend on game types; <see cref="LightManager"/> feeds it the actual tanks and shells.
/// Brightness follows the current preset through <see cref="LightingPresets.DynamicBrightness"/>.
/// </summary>
/// <remarks>Lights are pooled and reused every frame, so adding them doesn't allocate.</remarks>
public static class GameplayLights {
    static readonly List<PointLight> _pointPool = [];
    static readonly List<SpotLight> _spotPool = [];
    static int _pointsUsed, _spotsUsed;

    static float Brightness => LightingPresets.DynamicBrightness;

    /// <summary>Call once at the start of collecting a frame's gameplay lights.</summary>
    public static void BeginFrame() {
        _pointsUsed = 0;
        _spotsUsed = 0;
    }
    /// <summary>Creates a temporary light. If you wish to modify the particle, subscribe to <see cref="Particle.UpdateCallback"/>.</summary>
    /// <param name="time">The amount of time, in ticks (60 = 1 second).</param>
    /// <returns>The particle created.</returns>
    public static Particle CreateTempLight(ParticleManager system, Vector3 position, Light light, float time) {
        var particle = system.MakeParticle(position, TanksRebirth.GameContent.Globals.TextureGlobals.Pixels[Color.White]);
        particle.Alpha = 0f; // invisible: it only carries the light's lifetime
        LightingSystem.Lights.Add(light);

        particle.UniqueBehavior += (a) => {
            if (particle.LifeTime > time) {
                LightingSystem.Lights.Remove(light);
                particle.Destroy();
            }
        };

        return particle;
    }

    public static void AddHeadlight(Vector3 relativePosition, Vector2 forward, bool isLocalPlayer) {
        forward.Normalize();
        var forward3 = new Vector3(forward.X, 0f, forward.Y);

        var head = NextSpot();
        head.Position = relativePosition;
        head.Direction = forward3 + new Vector3(0f, -0.32f, 0f);
        head.Color = new Color(255, 236, 196);
        head.Intensity = 1.7f * Brightness;
        head.Range = 250f;
        head.InnerAngle = MathHelper.ToRadians(10f);
        head.OuterAngle = MathHelper.ToRadians(40f);
        head.CastsShadows = true;
        head.Priority = isLocalPlayer ? 10 : 8;
        LightingSystem.AddFrameLight(head);
    }

    public static void AddShellLight(Vector3 position, Color color) {
        var light = NextPoint();
        light.Position = position;
        light.Color = color;
        light.Intensity = 0.9f * Brightness;
        light.Range = 70f;
        light.Wrap = 0.4f;
        light.CastsShadows = true;
        light.Priority = 1;
        LightingSystem.AddFrameLight(light);
    }

    /// <summary>A coloured shell's glow; <paramref name="burst"/> (1 -> 0) is the bright flash as it leaves the barrel.</summary>
    public static void AddColoredShellLight(Vector3 position, Color color, float burst) {
        var light = NextPoint();
        light.Position = position + new Vector3(0f, 8f, 0f);
        light.Color = color;
        light.Intensity = (1.1f + 2.2f * burst) * MathF.Max(Brightness, 1f);
        light.Range = 60f + 50f * burst;
        light.Wrap = 0.4f;
        light.CastsShadows = false;
        light.Priority = 2;
        LightingSystem.AddFrameLight(light);
    }

    public static void AddMineLight(Mine mine) {
        var light = NextPoint();
        light.Position = mine.Position3D + new Vector3(0f, 9f, 0f);
        light.Color = new Color(255, 40, 30);
        // light.Intensity = (0.25f + 1.1f * blink) * Brightness;
        light.Intensity = 1f;
        light.Range = 55f;
        light.Wrap = 0.5f;
        light.CastsShadows = false;
        light.Priority = 1;
        LightingSystem.AddFrameLight(light);
    }

    public static void AddExplosionLight(Vector3 position, float radius, float life) {
        if (life <= 0f) return;

        var light = NextPoint();
        light.Position = position + new Vector3(0f, 14f, 0f);
        light.Color = new Color(255, 168, 82);
        light.Intensity = 3.2f * MathF.Pow(MathHelper.Clamp(life, 0f, 1f), 1.5f) * MathF.Max(Brightness, 1f);
        light.Range = 110f + radius * 3.5f;
        light.Wrap = 0.2f;
        light.CastsShadows = true;
        light.Priority = 9;
        LightingSystem.AddFrameLight(light);
    }

    // ---- flashes: short-lived lights for things that go bang (shell impacts, muzzle flashes, tank explosions)

    struct FlashInfo {
        public Vector3 Position;
        public Color Color;
        public float Intensity, Range, Start, Duration;
        public bool Shadows;
        public int Priority;
    }
    static readonly List<FlashInfo> _flashes = [];

    /// <summary>A light that bursts at full strength and fades out over <paramref name="seconds"/>. Does nothing while lighting is off.</summary>
    public static void Flash(Vector3 position, Color color, float intensity, float range, float seconds, bool shadows = false, int priority = 5) {
        if (!LightingSystem.IsActive)
            return;
        if (_flashes.Count > 48)
            _flashes.RemoveAt(0); // a big fight: the oldest flash goes first
        _flashes.Add(new FlashInfo { Position = position, Color = color, Intensity = intensity, Range = range,
            Start = RuntimeData.RunTime, Duration = seconds * 60f, Shadows = shadows, Priority = priority });
    }

    /// <summary>Adds this frame's flash lights (called while collecting the frame's gameplay lights).</summary>
    public static void AddFlashes() {
        float now = RuntimeData.RunTime;
        // bright enough to read in daylight, without blowing out at night
        float boost = MathF.Max(Brightness, 1f);
        for (int i = _flashes.Count - 1; i >= 0; i--) {
            var f = _flashes[i];
            float t = (now - f.Start) / MathF.Max(f.Duration, 1f);
            if (t >= 1f || t < 0f) {
                _flashes.RemoveAt(i);
                continue;
            }
            float fade = (1f - t) * (1f - t); // bursts, then dies away quickly
            var light = NextPoint();
            light.Position = f.Position;
            light.Color = f.Color;
            light.Intensity = f.Intensity * fade * boost;
            light.Range = f.Range * (0.75f + 0.25f * (1f - t));
            light.Wrap = 0.3f;
            light.CastsShadows = f.Shadows;
            light.Priority = f.Priority;
            LightingSystem.AddFrameLight(light);
        }
    }

    public static void AddLantern(Vector3 position, float time, bool isLocalPlayer) {
        // i keep having to ask ai how to make a good flickering effect :cry:
        var flicker = 0.9f + 0.06f * MathF.Sin(time * 2.1f) + 0.03f * MathF.Sin(time * 7.7f + 1.3f) + 0.02f * MathF.Sin(time * 17.3f + 4.1f);
        var light = NextPoint();
        light.Position = position;
        light.Color = new Color(255, 178, 76);
        light.Intensity = 1.6f * flicker;
        light.Range = 100f * (0.97f + 0.03f * flicker);
        light.Wrap = 0.3f;
        light.CastsShadows = true;
        light.Priority = isLocalPlayer ? 11 : 9;
        LightingSystem.AddFrameLight(light);
    }

    static PointLight NextPoint() {
        if (_pointsUsed == _pointPool.Count)
            _pointPool.Add(new PointLight());
        var light = _pointPool[_pointsUsed++];
        light.Enabled = true;
        light.ShadowBias = 0.75f;
        return light;
    }

    // idek the point of this or nextpoint
    static SpotLight NextSpot() {
        if (_spotsUsed == _spotPool.Count)
            _spotPool.Add(new SpotLight());
        var light = _spotPool[_spotsUsed++];
        light.Enabled = true;
        light.ShadowBias = 0.75f;
        return light;
    }
}
