using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.Enums;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.Globals.Assets;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.RebirthUtils;
using TanksRebirth.GameContent.Systems.Coordinates;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Graphics;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.Troves;

/// <summary>Tank Troves' extra block types (they only appear in Tank Troves rooms).</summary>
public static class TroveBlockID {
    public const int Rotten = 200, Glass = 201, Crate = 202, Barrel = 203, Ice = 204, Steel = 205, Rubber = 206, Mirror = 207, Hay = 208,
        Grate = 209, Bush = 210, Magnet = 211, Gate = 212,
        // floor pieces
        Mud = 220, IceFloor = 221, Conveyor = 222, Spikes = 223, Lava = 224, Plate = 225;

    public static bool IsTrove(int type) => type is >= 200 and <= 230;
    /// <summary>Lies flat on the floor (drawn like a hole).</summary>
    public static bool IsFloor(int type) => type is >= 220 and <= 230;
    /// <summary>Shots, mines or fire can break it.</summary>
    public static bool IsBreakable(int type) => type is Rotten or Glass or Crate or Barrel or Ice or Hay;
    /// <summary>Tanks can't drive through it (when shut, for gates).</summary>
    public static bool BlocksTanks(int type) => !IsFloor(type) && type != Bush || type == Lava;

    public static string Name(int type) => type switch {
        Rotten => "Rotten wood", Glass => "Glass", Crate => "Crate", Barrel => "Explosive barrel", Ice => "Ice", Steel => "Steel",
        Rubber => "Rubber", Mirror => "Mirror", Hay => "Hay bale", Grate => "Grate", Bush => "Bush", Magnet => "Magnet", Gate => "Gate",
        Mud => "Mud", IceFloor => "Ice floor", Conveyor => "Conveyor", Spikes => "Spikes", Lava => "Lava", Plate => "Pressure plate",
        _ => "?",
    };
}

/// <summary>
/// How Tank Troves' extra blocks look and behave:
/// <list type="bullet">
/// <item>Rotten wood cracks on the first shot and crumbles on the second. Glass shatters and the shell keeps going.
/// Crates break in one shot and sometimes hold a pickup. Explosive barrels blow up (and set each other off).
/// Ice chips away over three hits. Hay bales swallow shells. All of these break in explosions too.</item>
/// <item>Steel can't be broken and always bounces a shell, even one with no bounces left. Rubber bounces it back faster
/// without using up a bounce. Mirrors send it straight back. Magnets bend passing shells toward them.
/// Grates let shells through but not tanks.</item>
/// <item>Bushes hide a player from enemies (tanks drive through them). Gates open and shut on a timer, or while a
/// pressure plate in the room is held down.</item>
/// <item>On the floor: mud slows tanks, ice makes them slide, conveyors push tanks and mines, spikes pop up on a beat,
/// and lava can't be crossed by tanks (shells fly over it).</item>
/// </list>
/// </summary>
public static class TroveBlocks {
    private const string Dir = "Assets/textures/ingame/blocks/";
    private static readonly Dictionary<string, Texture2D> _tex = [];
    private static Texture2D Tex(string name) {
        if (!_tex.TryGetValue(name, out var t) || t.IsDisposed)
            _tex[name] = t = GameResources.GetGameResource<Texture2D>(Dir + name);
        return t;
    }

    private static double Now => TroveRun.PlayTime;

    private static bool _hooked;
    /// <summary>Hooks into blocks and shells (once).</summary>
    public static void Init() {
        if (_hooked)
            return;
        _hooked = true;
        Block.CustomSwap = Swap;
        Block.CustomRender = Render;
        Block.OnPostRender += PostRender;
        Block.OnDestroy += Destroyed;
        Shell.HitsBlock = ShellHit;
    }

    // ------------------------------------------------------------------ setting a block up

    private static bool Swap(Block b, int type) {
        if (!TroveBlockID.IsTrove(type))
            return false;
        var p = b.Properties;
        p.IsDestructible = TroveBlockID.IsBreakable(type);
        p.IsSolid = true;
        p.IsCollidable = true;
        p.CanStack = true;
        p.HasShadow = true;
        b.Translucent = false;
        b.State = 0;
        b.Sink = 0;
        if (TroveBlockID.IsFloor(type)) {
            b.Model = ModelGlobals.FlatFace.Asset;
            p.CanStack = false;
            p.HasShadow = false;
            p.IsSolid = false;                       // shells fly over
            p.IsCollidable = type == TroveBlockID.Lava;  // tanks can't cross lava
            p.AllowShotPathBounce = false;
        }
        else
            b.Model = (GameScene.Theme == MapTheme.Christmas
                ? b.IsAlternateModel ? ModelGlobals.BlockStackAltSnowy : ModelGlobals.BlockStackSnowy
                : b.IsAlternateModel ? ModelGlobals.BlockStackAlt : ModelGlobals.BlockStack).Asset;
        switch (type) {
            case TroveBlockID.Rotten: b.SwapTexture(Tex("block_rotten")); b.State = 2; break;
            case TroveBlockID.Glass: b.SwapTexture(Tex("block_glass")); b.Translucent = true; break;
            case TroveBlockID.Crate: b.SwapTexture(Tex("block_crate")); break;
            case TroveBlockID.Barrel: b.SwapTexture(Tex("block_barrel")); break;
            case TroveBlockID.Ice: b.SwapTexture(Tex("block_ice_0")); b.Translucent = true; b.State = 3; break;
            case TroveBlockID.Steel: b.SwapTexture(Tex("block_steel")); break;
            case TroveBlockID.Rubber: b.SwapTexture(Tex("block_rubber")); break;
            case TroveBlockID.Mirror: b.SwapTexture(Tex("block_mirror")); break;
            case TroveBlockID.Hay: b.SwapTexture(Tex("block_hay")); break;
            case TroveBlockID.Grate: b.SwapTexture(Tex("block_grate")); b.Translucent = true; p.IsSolid = false; p.AllowShotPathBounce = false; break;
            case TroveBlockID.Bush:
                b.SwapTexture(Tex("block_bush"));
                b.Translucent = true;
                p.IsSolid = false;
                p.IsCollidable = false;
                p.AllowShotPathBounce = false;
                break;
            case TroveBlockID.Magnet: b.SwapTexture(Tex("block_magnet")); break;
            case TroveBlockID.Gate: b.SwapTexture(Tex("block_gate_closed")); b.Translucent = true; break;
            case TroveBlockID.Mud: b.SwapTexture(Tex("floor_mud")); b.Translucent = true; break;
            case TroveBlockID.IceFloor: b.SwapTexture(Tex("floor_ice")); b.Translucent = true; break;
            case TroveBlockID.Conveyor:
                b.SwapTexture(Tex("floor_conveyor_0"));
                // its direction comes from the room's layout (kept in the stack height: 1 = up the screen, then clockwise)
                b.State = Math.Max(0, b.Stack - 1) & 3;
                b.Yaw = MathHelper.Pi - (int)b.State * MathHelper.PiOver2; // (the texture's arrows point down)
                break;
            case TroveBlockID.Spikes: b.SwapTexture(Tex("floor_spikes_down")); break;
            case TroveBlockID.Lava: b.SwapTexture(Tex("floor_lava_0")); break;
            case TroveBlockID.Plate: b.SwapTexture(Tex("floor_plate")); break;
        }
        return true;
    }

    /// <summary>A conveyor's direction (0 = up the screen, then clockwise), kept in its stack height when the room is made.</summary>
    public static Vector2 ConveyorDir(Block b) => ((int)b.State & 3) switch {
        0 => new Vector2(0, -1),
        1 => new Vector2(1, 0),
        2 => new Vector2(0, 1),
        _ => new Vector2(-1, 0),
    };

    // ------------------------------------------------------------------ shells

    private static bool ShellHit(Block b, Shell s, CollisionDirection dir, bool corner) {
        if (!TroveBlockID.IsTrove(b.Type))
            return false;
        bool horizontal = dir is CollisionDirection.Left or CollisionDirection.Right;
        var at = b.Position3D + new Vector3(0, 12, 0);
        switch (b.Type) {
            case TroveBlockID.Rotten:
                if (--b.State <= 0) {
                    // the second shot: it crumbles, and the shell goes with it
                    Sound("wood_crumble", at, 0.9f);
                    b.Destroy();
                    s.Destroy(Shell.DestructionContext.WithObstacle, playSound: false);
                }
                else {
                    Sound("wood_crack", at, 0.8f);
                    b.SwapTexture(Tex("block_rotten_cracked"));
                    Chips(b, new Color(150, 120, 80), 6);
                    Bounce(s, dir, corner, horizontal);
                }
                return true;
            case TroveBlockID.Glass:
                // it shatters, and the shell flies on through
                Sound("glass_break", at, 0.9f);
                b.Destroy();
                return true;
            case TroveBlockID.Crate:
                Sound("wood_crumble", at, 0.8f);
                b.Destroy();
                s.Destroy(Shell.DestructionContext.WithObstacle, playSound: false);
                return true;
            case TroveBlockID.Barrel:
                s.Destroy(Shell.DestructionContext.WithObstacle, playSound: false);
                b.Destroy();
                return true;
            case TroveBlockID.Ice:
                Sound("ice_crack", at, 0.8f);
                if (--b.State <= 0)
                    b.Destroy();
                else {
                    b.SwapTexture(Tex(b.State >= 2 ? "block_ice_1" : "block_ice_2"));
                    Chips(b, new Color(200, 235, 255), 5);
                    Bounce(s, dir, corner, horizontal);
                }
                return true;
            case TroveBlockID.Hay:
                // swallowed, with a puff of straw
                Sound("hay_thud", at, 0.8f);
                Chips(b, new Color(236, 204, 120), 8);
                s.Destroy(Shell.DestructionContext.WithObstacle, playSound: false);
                return true;
            case TroveBlockID.Steel:
                // always bounces, even a shell that's out of bounces
                if (s.RicochetsRemaining == 0)
                    s.RicochetsRemaining = 1;
                Sound("clank", at, 0.55f);
                Bounce(s, dir, corner, horizontal);
                return true;
            case TroveBlockID.Rubber:
                // a free bounce, and faster
                s.RicochetsRemaining++;
                Sound("boing", at, 0.7f);
                Bounce(s, dir, corner, horizontal);
                if (s.Velocity.Length() < 9f)
                    s.Velocity *= 1.18f;
                return true;
            case TroveBlockID.Mirror:
                // straight back the way it came (a free bounce)
                s.Velocity = -s.Velocity;
                s.WallCooldown(6);
                Sound("clank", at, 0.6f, 0.5f);
                GameHandler.Particles.MakeShineSpot(s.Position3D, Color.White, 0.8f);
                return true;
        }
        return false;
    }

    private static void Bounce(Shell s, CollisionDirection dir, bool corner, bool horizontal) {
        if (corner) {
            s.Velocity = -s.Velocity;
            s.WallCooldown(5);
            return;
        }
        s.Ricochet(horizontal);
        s.WallCooldown(5);
    }

    // ------------------------------------------------------------------ breaking

    private static readonly List<(Vector2 At, double When, Tank? By)> _blasts = [];

    private static void Destroyed(Block b) {
        if (!TroveBlockID.IsTrove(b.Type) || !TroveRun.Active)
            return;
        var at = b.Position3D;
        switch (b.Type) {
            case TroveBlockID.Glass:
                Shards(b, new Color(200, 240, 255), 16);
                break;
            case TroveBlockID.Ice:
                Shards(b, new Color(210, 240, 255), 12);
                Sound("glass_break", at, 0.6f, -0.3f);
                break;
            case TroveBlockID.Barrel:
                // a moment later, so a row of them goes off one after another
                _blasts.Add((b.Position, Now + 0.12, null));
                break;
            case TroveBlockID.Crate:
                CrateDrop(b);
                break;
        }
    }

    /// <summary>Crates: one chance each per run at a pickup (the same on every phone).</summary>
    private static void CrateDrop(Block b) {
        if (TroveRun.Room is not { } room || TroveRun.Battle)
            return;
        var g = TroveGen.ToGrid(b.Position);
        if (!room.CorkRolled.Add(new Point(g.X + 1000, g.Y)))
            return;
        uint h = (uint)TroveRun.Seed * 2654435761u ^ (uint)TroveRun.FloorNumber * 40503u ^ (uint)(room.Cell.X * 73856093) ^ (uint)(room.Cell.Y * 19349663)
            ^ (uint)(g.X * 83492791) ^ (uint)(g.Y * 2971215073u) ^ 0xC8A7Eu;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        int roll = (int)(h % 100) / TroveItems.Luck();
        TroveItemId? item = roll switch {
            < 8 => TroveItemId.Shield,
            < 12 => TroveItemId.Boost,
            < 15 => TroveItemId.ExtraLife,
            15 => TroveItemId.ExtraShell,
            _ => null,
        };
        if (item is not { } id)
            return;
        room.GroundDrops.Add(new TroveGroundDrop { At = b.Position, Item = id });
        GameHandler.Particles.MakeShineSpot(new Vector3(b.Position.X, 12, b.Position.Y), Color.Gold, 1.2f);
    }

    private static void Chips(Block b, Color c, int n) {
        for (int i = 0; i < n; i++) {
            var p = GameHandler.Particles.MakeParticle(b.Position3D + new Vector3(Random.Shared.NextSingle() * 16 - 8, 10 + Random.Shared.NextSingle() * 8, Random.Shared.NextSingle() * 16 - 8),
                GameResources.GetGameResource<Texture2D>("Assets/textures/misc/tank_rock"));
            var vel = new Vector3(Random.Shared.NextSingle() * 3 - 1.5f, 2 + Random.Shared.NextSingle() * 2, Random.Shared.NextSingle() * 3 - 1.5f);
            p.HasAdditiveBlending = false;
            p.Scale = new(0.35f);
            p.Color = c;
            p.Roll = -CameraGlobals.DEFAULT_ORTHOGRAPHIC_ANGLE;
            p.UniqueBehavior = q => {
                vel.Y -= 0.2f;
                q.Position += vel;
                q.Alpha -= 0.04f;
                if (q.Alpha <= 0 || q.Position.Y < 0)
                    q.Destroy();
            };
        }
    }

    private static void Shards(Block b, Color c, int n) {
        for (int i = 0; i < n; i++) {
            var p = GameHandler.Particles.MakeParticle(b.Position3D + new Vector3(Random.Shared.NextSingle() * 18 - 9, 4 + Random.Shared.NextSingle() * 16, Random.Shared.NextSingle() * 18 - 9),
                GameResources.GetGameResource<Texture2D>("Assets/textures/misc/tank_rock_2"));
            var vel = new Vector3(Random.Shared.NextSingle() * 5 - 2.5f, 1.5f + Random.Shared.NextSingle() * 3, Random.Shared.NextSingle() * 5 - 2.5f);
            float spin = Random.Shared.NextSingle() * 0.4f - 0.2f;
            p.HasAdditiveBlending = true;
            p.Scale = new(0.3f + Random.Shared.NextSingle() * 0.3f);
            p.Color = c;
            p.Alpha = 0.9f;
            p.Roll = -CameraGlobals.DEFAULT_ORTHOGRAPHIC_ANGLE;
            p.UniqueBehavior = q => {
                vel.Y -= 0.22f;
                q.Position += vel;
                q.Rotation2D += spin;
                if (q.Position.Y < 0.5f) {
                    q.Position = new Vector3(q.Position.X, 0.5f, q.Position.Z);
                    vel *= 0.4f;
                }
                q.Alpha -= 0.015f;
                if (q.Alpha <= 0)
                    q.Destroy();
            };
        }
    }

    private static void Sound(string name, Vector3 at, float volume, float pitch = 0f) =>
        SoundPlayer.PlaySoundInstance($"Assets/sounds/blocks/{name}.ogg", SoundContext.Effect, volume,
            pitchOverride: pitch + (Random.Shared.NextSingle() - 0.5f) * 0.12f, position: at);

    // ------------------------------------------------------------------ every update

    private static double _lastFrame;
    private static int _frame;
    // gates: how long they've been held open by a plate
    private static double _plateHeldUntil;
    private static readonly Dictionary<Tank, double> _spikedAt = [];

    public static void Update() {
        if (!TroveRun.Active || !CampaignGlobals.InMission)
            return;
        float dt = TanksRebirth.RuntimeData.DeltaTime;
        // barrels going off
        for (int i = _blasts.Count - 1; i >= 0; i--) {
            if (Now < _blasts[i].When)
                continue;
            var bl = _blasts[i];
            _blasts.RemoveAt(i);
            SoundPlayer.PlaySoundInstance("Assets/sounds/mine_explode.ogg", SoundContext.Effect, 0.8f, position: new Vector3(bl.At.X, 0, bl.At.Y));
            new Explosion(bl.At, 7f, bl.By, 0.25f);
        }

        bool animate = Now - _lastFrame > 0.12;
        if (animate) {
            _lastFrame = Now;
            _frame++;
        }
        bool anyPlate = false, platePressed = false;
        foreach (var b in Block.AllBlocks) {
            if (b is null || !TroveBlockID.IsTrove(b.Type))
                continue;
            switch (b.Type) {
                case TroveBlockID.Lava:
                    if (animate)
                        b.SwapTexture(Tex($"floor_lava_{(_frame / 2 + b.Id) & 3}"));
                    break;
                case TroveBlockID.Conveyor:
                    if (animate)
                        b.SwapTexture(Tex($"floor_conveyor_{_frame & 3}"));
                    Convey(b, dt);
                    break;
                case TroveBlockID.Plate: {
                    anyPlate = true;
                    bool pressed = AnyTankOn(b, 12f);
                    if (pressed != b.State > 0) {
                        b.State = pressed ? 1 : 0;
                        b.SwapTexture(Tex(pressed ? "floor_plate_pressed" : "floor_plate"));
                        Sound("plate_click", b.Position3D, 0.6f, pressed ? 0f : -0.3f);
                    }
                    platePressed |= pressed;
                    break;
                }
                case TroveBlockID.Magnet:
                    Pull(b, dt);
                    break;
                case TroveBlockID.Bush:
                    // a player inside sees through it
                    b.Alpha = MathHelper.Lerp(b.Alpha, PlayerInside(b) ? 0.45f : 1f, MathF.Min(1f, 0.2f * dt));
                    break;
                case TroveBlockID.Ice:
                    // fire melts it
                    foreach (var f in TroveItems.Fires)
                        if (Now < f.Until && Vector2.Distance(f.At, b.Position) < 30f) {
                            b.Destroy();
                            break;
                        }
                    break;
            }
        }
        if (platePressed)
            _plateHeldUntil = Now + 1.2;

        // gates and spikes
        foreach (var b in Block.AllBlocks) {
            if (b is null)
                continue;
            if (b.Type == TroveBlockID.Gate)
                Gate(b, anyPlate ? Now < _plateHeldUntil : GateCycleOpen(b), dt);
            else if (b.Type == TroveBlockID.Spikes)
                Spikes(b, dt);
        }
    }

    // a gate with no plate in the room: shut 4.5 s, open 3 s (alternate gates take turns)
    private static bool GateCycleOpen(Block b) {
        var g = TroveGen.ToGrid(b.Position);
        double t = (Now + ((g.X + g.Y) % 2) * 3.75) % 7.5;
        return t >= 4.5;
    }

    private static void Gate(Block b, bool open, float dt) {
        bool isOpen = b.State > 0;
        if (open != isOpen) {
            // it won't come down on a tank
            if (!open && AnyTankOn(b, 16f))
                return;
            b.State = open ? 1 : 0;
            b.Properties.IsSolid = !open;
            b.SetPassable(open);
            b.SwapTexture(Tex(open ? "block_gate_open" : "block_gate_closed"));
            Sound("gate", b.Position3D, 0.5f, open ? 0.1f : -0.2f);
        }
        // the bars slide into the floor
        b.Sink = MathHelper.Lerp(b.Sink, open ? 0.82f : 0f, Math.Min(1f, 0.18f * dt));
    }

    // spikes: down 2.2 s (they blink red for the last 0.7), up 1.2 s
    private const double SPIKE_CYCLE = 3.4, SPIKE_DOWN = 2.2;
    private static double SpikePhase(Block b) {
        var g = TroveGen.ToGrid(b.Position);
        return (Now + (g.X * 0.37 + g.Y * 0.21) % 1.0 * 0.4) % SPIKE_CYCLE;
    }

    private static void Spikes(Block b, float dt) {
        double ph = SpikePhase(b);
        bool up = ph >= SPIKE_DOWN;
        if (up != b.State > 0) {
            b.State = up ? 1 : 0;
            b.SwapTexture(Tex(up ? "floor_spikes_up" : "floor_spikes_down"));
            if (up)
                Sound("spikes", b.Position3D, 0.45f);
        }
        b.Sink = MathHelper.Lerp(b.Sink, up ? 1f : 0f, Math.Min(1f, 0.45f * dt));
        if (!up || b.Sink < 0.5f)
            return;
        bool host = !Client.IsConnected() || Client.IsHost();
        foreach (var t in GameHandler.AllTanks) {
            if (t is null || t.IsDestroyed || t.Properties.Immortal || Vector2.Distance(t.Position, b.Position) > Block.SIDE_LENGTH * 0.6f)
                continue;
            // each phone hurts its own players; the host hurts the enemies
            bool decides = t is PlayerTank pt ? TroveItems.IsLocal(pt.PlayerId) : host;
            if (!decides || _spikedAt.TryGetValue(t, out var last) && Now - last < SPIKE_CYCLE - 0.2)
                continue;
            _spikedAt[t] = Now;
            t.Damage(new TankHurtContextOther(null, TankHurtContextOther.HurtContext.FromIngame, "Spikes"), true);
        }
    }

    /// <summary>Spikes about to come up blink.</summary>
    public static bool SpikesWarning(Block b) {
        double ph = SpikePhase(b);
        return ph >= SPIKE_DOWN - 0.7 && ph < SPIKE_DOWN;
    }

    private static void Convey(Block b, float dt) {
        var d = ConveyorDir(b) * 0.55f * dt;
        foreach (var t in GameHandler.AllTanks)
            if (t is { IsDestroyed: false } && Within(t.Position, b))
                t.Position += d;
        foreach (var m in Mine.AllMines)
            if (m is not null && Within(m.Position, b))
                m.Position += d;
    }

    private static bool Within(Vector2 p, Block b) =>
        MathF.Abs(p.X - b.Position.X) < Block.SIDE_LENGTH / 2 && MathF.Abs(p.Y - b.Position.Y) < Block.SIDE_LENGTH / 2;

    private static void Pull(Block b, float dt) {
        foreach (var s in Shell.AllShells) {
            if (s is null)
                continue;
            var to = b.Position - s.Position;
            float dist = to.Length();
            if (dist > 110f || dist < 4f)
                continue;
            float speed = s.Velocity.Length();
            if (speed < 0.01f)
                continue;
            // a gentle pull, stronger up close; the shell keeps its speed
            float k = 0.05f * (1f - dist / 110f) * dt;
            var v = Vector2.Normalize(s.Velocity) + Vector2.Normalize(to) * k * 3f;
            s.Velocity = Vector2.Normalize(v) * speed;
            if (Random.Shared.NextDouble() < 0.04 * dt)
                GameHandler.Particles.MakeShineSpot(s.Position3D, new Color(120, 170, 255), 0.4f);
        }
    }

    private static bool AnyTankOn(Block b, float r) {
        foreach (var t in GameHandler.AllTanks)
            if (t is { IsDestroyed: false } && Vector2.Distance(t.Position, b.Position) < r)
                return true;
        return false;
    }

    private static bool PlayerInside(Block b) {
        foreach (var t in GameHandler.AllPlayerTanks)
            if (t is { IsDestroyed: false } && Within(t.Position, b))
                return true;
        return false;
    }

    /// <summary>Is this player inside a bush (enemies can't see them)?</summary>
    public static bool InBush(Tank t) {
        if (t is not PlayerTank || !TroveRun.Active)
            return false;
        foreach (var b in Block.AllBlocks)
            if (b is not null && b.Type == TroveBlockID.Bush && Within(t.Position, b))
                return true;
        return false;
    }

    // mud slows; ice floor slides
    private static readonly Dictionary<Tank, Vector2> _slide = [];
    public static Vector2 FilterVelocity(Tank t, Vector2 v) {
        bool mud = false, ice = false;
        foreach (var b in Block.AllBlocks) {
            if (b is null || b.Type is not (TroveBlockID.Mud or TroveBlockID.IceFloor) || !Within(t.Position, b))
                continue;
            mud |= b.Type == TroveBlockID.Mud;
            ice |= b.Type == TroveBlockID.IceFloor;
        }
        if (mud)
            v *= 0.5f;
        if (!ice) {
            _slide.Remove(t);
            return v;
        }
        var last = _slide.TryGetValue(t, out var l) ? l : v;
        var now = Vector2.Lerp(last, v, MathF.Min(1f, 0.06f * TanksRebirth.RuntimeData.DeltaTime));
        _slide[t] = now;
        return now;
    }

    /// <summary>A new room: forget the last one's moving parts.</summary>
    public static void Reset() {
        _blasts.Clear();
        _spikedAt.Clear();
        _slide.Clear();
        _plateHeldUntil = 0;
    }

    /// <summary>Lights: lava glows, barrels have a warning lamp, magnets hum blue.</summary>
    public static void AddFrameLights() {
        foreach (var b in Block.AllBlocks) {
            if (b is null)
                continue;
            switch (b.Type) {
                case TroveBlockID.Lava:
                    Graphics.DynamicLighting.GameplayLights.AddGlow(b.Position3D + new Vector3(0, 8, 0), new Color(255, 120, 40),
                        1.1f + 0.2f * MathF.Sin((float)Now * 3f + b.Id), 50f, 2);
                    break;
                case TroveBlockID.Spikes when SpikesWarning(b):
                    Graphics.DynamicLighting.GameplayLights.AddGlow(b.Position3D + new Vector3(0, 6, 0), new Color(255, 50, 30), 0.8f, 30f, 2);
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ drawing

    private static BasicEffect? _fx;
    private static VertexPositionNormalTexture[]? _barrelSide, _barrelLid, _spike;

    private static bool Render(Block b) {
        if (b.Type != TroveBlockID.Barrel)
            return false;
        var gd = TankGame.Instance.GraphicsDevice;
        EnsureMeshes();
        var fx = Effect(gd);
        fx.World = Matrix.CreateTranslation(b.Position3D);
        fx.Texture = Tex("block_barrel");
        Draw(gd, fx, _barrelSide!);
        fx.Texture = Tex("block_barrel_lid");
        Draw(gd, fx, _barrelLid!);
        return true;
    }

    private static void PostRender(Block b) {
        if (b.Type == TroveBlockID.Spikes && b.Sink > 0.02f) {
            var gd = TankGame.Instance.GraphicsDevice;
            EnsureMeshes();
            var fx = Effect(gd);
            fx.World = Matrix.CreateScale(1f, b.Sink, 1f) * Matrix.CreateTranslation(b.Position3D);
            fx.Texture = Tex("block_steel");
            Draw(gd, fx, _spike!);
        }
        else if (b.Type == TroveBlockID.Spikes && SpikesWarning(b) && (int)(Now * 8) % 2 == 0) {
            // about to come up: the holes blink red
            GameHandler.Particles.MakeShineSpot(b.Position3D + new Vector3(0, 1, 0), new Color(255, 40, 30), 0.25f);
        }
    }

    private static BasicEffect Effect(GraphicsDevice gd) {
        _fx ??= new BasicEffect(gd) { TextureEnabled = true };
        _fx.View = CameraGlobals.GameView;
        _fx.Projection = CameraGlobals.GameProjection;
        _fx.SetDefaultGameLighting_IngameEntities(10f);
        if (Block.Tint != Vector3.One) {
            _fx.DiffuseColor *= Block.Tint;
            _fx.AmbientLightColor *= Block.Tint;
        }
        _fx.DirectionalLight0.Direction *= 0.1f;
        _fx.Alpha = 1f;
        return _fx;
    }

    private static void Draw(GraphicsDevice gd, BasicEffect fx, VertexPositionNormalTexture[] v) {
        var oldRaster = gd.RasterizerState;
        gd.RasterizerState = RasterizerState.CullNone;
        gd.SamplerStates[0] = SamplerState.LinearClamp;
        foreach (var pass in fx.CurrentTechnique.Passes) {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleList, v, 0, v.Length / 3);
        }
        gd.RasterizerState = oldRaster;
    }

    private static void EnsureMeshes() {
        if (_barrelSide is not null)
            return;
        const int sides = 20, rings = 5;
        float r = Block.SIDE_LENGTH * 0.42f, h = Block.SIDE_LENGTH * 1.02f;
        var side = new List<VertexPositionNormalTexture>();
        for (int i = 0; i < sides; i++) {
            float a0 = i * MathHelper.TwoPi / sides, a1 = (i + 1) * MathHelper.TwoPi / sides;
            for (int k = 0; k < rings; k++) {
                float ya = h * k / rings, yb = h * (k + 1) / rings;
                // a slight bulge in the middle
                Vector3 P(float a, float y) {
                    float bulge = 1f + 0.07f * MathF.Sin(y / h * MathF.PI);
                    return new Vector3(MathF.Sin(a) * r * bulge, y, MathF.Cos(a) * r * bulge);
                }
                Vector3 N(float a) => new(MathF.Sin(a), 0, MathF.Cos(a));
                float va = 0.5f - 0.5f * k / rings, vb = 0.5f - 0.5f * (k + 1) / rings;
                float u0 = (float)i / sides, u1 = (float)(i + 1) / sides;
                side.Add(new(P(a0, ya), N(a0), new(u0, va))); side.Add(new(P(a1, ya), N(a1), new(u1, va))); side.Add(new(P(a1, yb), N(a1), new(u1, vb)));
                side.Add(new(P(a0, ya), N(a0), new(u0, va))); side.Add(new(P(a1, yb), N(a1), new(u1, vb))); side.Add(new(P(a0, yb), N(a0), new(u0, vb)));
            }
        }
        _barrelSide = side.ToArray();
        var lid = new List<VertexPositionNormalTexture>();
        for (int i = 0; i < sides; i++) {
            float a0 = i * MathHelper.TwoPi / sides, a1 = (i + 1) * MathHelper.TwoPi / sides;
            var top = new Vector3(0, h, 0);
            Vector2 Uv(float a) => new(0.5f + MathF.Sin(a) * 0.5f, 0.5f + MathF.Cos(a) * 0.5f);
            lid.Add(new(top, Vector3.Up, new(0.5f, 0.5f)));
            lid.Add(new(top + new Vector3(MathF.Sin(a0) * r, 0, MathF.Cos(a0) * r), Vector3.Up, Uv(a0)));
            lid.Add(new(top + new Vector3(MathF.Sin(a1) * r, 0, MathF.Cos(a1) * r), Vector3.Up, Uv(a1)));
        }
        _barrelLid = lid.ToArray();
        // nine spikes on a 3x3 grid (as the plate's holes)
        var sp = new List<VertexPositionNormalTexture>();
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++) {
                var bse = new Vector3((i - 1) * Block.SIDE_LENGTH * 0.25f, 0.1f, (j - 1) * Block.SIDE_LENGTH * 0.25f);
                float rr = Block.SIDE_LENGTH * 0.07f, hh = Block.SIDE_LENGTH * 0.42f;
                var tip = bse + new Vector3(0, hh, 0);
                for (int k = 0; k < 6; k++) {
                    float a0 = k * MathHelper.TwoPi / 6, a1 = (k + 1) * MathHelper.TwoPi / 6;
                    var p0 = bse + new Vector3(MathF.Sin(a0) * rr, 0, MathF.Cos(a0) * rr);
                    var p1 = bse + new Vector3(MathF.Sin(a1) * rr, 0, MathF.Cos(a1) * rr);
                    var n = Vector3.Normalize(Vector3.Cross(p1 - p0, tip - p0));
                    if (n.Y < 0)
                        n = -n;
                    sp.Add(new(p0, n, new(0.45f, 0.45f))); sp.Add(new(tip, n, new(0.5f, 0.5f))); sp.Add(new(p1, n, new(0.45f, 0.45f)));
                }
            }
        _spike = sp.ToArray();
    }
}
