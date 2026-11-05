using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using TanksRebirth.Enums;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using TanksRebirth.Internals.Common.Utilities;
using TanksRebirth.Internals;
using Microsoft.Xna.Framework.Audio;
using TanksRebirth.Internals.Common;
using TanksRebirth.GameContent.GameMechanics;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Internals.Common.Framework.Input;
using TanksRebirth.Graphics;
using TanksRebirth.Net;
using TanksRebirth.GameContent.Systems;
using FontStashSharp;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.UI;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.RebirthUtils;
using TanksRebirth.GameContent.UI.MainMenu;
using TanksRebirth.Internals.Common.Framework;
using TanksRebirth.GameContent.UI.LevelEditor;
using TanksRebirth.GameContent.Globals.Assets;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.Internals.Common.Framework.Collisions;

namespace TanksRebirth.GameContent;

public class PlayerTank : Tank {
    private static bool _justCenteredMouse = false;
    #region The Rest
    public static int MyTeam;
    public static int MyTankType;
    public static int StartingLives = 3;
    public static Dictionary<int, int> TankKills { get; set; } = []; // this campaign only!
    // questioning the validity of this struct but whatever
    public struct CampaignStats {
        public int ShellsShot;
        public int ShellHits;
        public int MinesLaid;
        public int MineHits;
        public int Suicides; // self-damage this campaign?
    }
    public static CampaignStats PlayerStatistics;
    public static bool _drawShotPath;
    public static int[] KillCounts { get; set; } = [0, 0, 0, 0];
    public int PlayerId { get; }
    public int PlayerType { get; }

    private Texture2D? _tankTexture;

    public static Keybind controlUp = new("Up", Keys.W);
    public static Keybind controlDown = new("Down", Keys.S);
    public static Keybind controlLeft = new("Left", Keys.A);
    public static Keybind controlRight = new("Right", Keys.D);
    public static Keybind controlMine = new("Place Mine", Keys.Space);
    public static Keybind controlFirePath = new("Draw Shot Path", Keys.Q);
    public static GamepadBind FireBullet = new("Fire Bullet", Buttons.RightTrigger);
    public static GamepadBind PlaceMine = new("Place Mine", Buttons.A);

    private bool playerControl_isBindPressed;

    public Vector2 oldPosition;

    private bool _isPlayerModel;
    private Texture2D? _shadowTexture;

    // 46 if using keyboard, 10 if using a controller
    //private float _maxTurnInputBased;
    #endregion

    public static bool LastUsedController = false;

    public Vector2 DesiredDirection;
    public static float StickDeadzone { get; set; } = 0.12f;
    public static float StickAntiDeadzone { get; set; } = 0.85f;
    /// <summary>A <see cref="PlayerTank"/> instance which represents the current client's tank they *primarily* control. Will return null in cases where
    /// the tank simply is inexistent (i.e: in the main menu). In a single-player context, this will always be the first player tank.</summary>
    public static PlayerTank ClientTank => GameHandler.AllPlayerTanks[NetPlay.GetMyClientId()];
    /// <summary>The amount of lives for every existing player. Access a certain player's life count via indexing this array with a PlayerID entry.
    /// <para>Note that lives are always synced on multiplayer.</para>
    /// </summary>
    public static int[] Lives { get; set; } = new int[Server.MaxClients];

    /// <summary>In multiplayer, gets the lives of the client that this code is currently being called on.</summary>
    public static int GetMyLives() => Lives[NetPlay.GetMyClientId()];
    /// <summary>
    /// Adds lives to the player in Single-Player, adds to the lives of all players in Multiplayer.
    /// </summary>
    /// <param name="num">How many lives to add.</param>
    public static void AddLives(int num) {
        if (Client.IsConnected())
            Lives[NetPlay.GetMyClientId()] += num;
        else
            for (int i = 0; i < Lives.Length; i++)
                Lives[i] += num;
    }
    /// <summary>
    /// Sets the lives of the player in Single-Player, sets the lives of all players in Multiplayer.
    /// </summary>
    /// <param name="num">How many lives to set the player(s) to.</param>
    public static void SetLives(int num) {
        if (Client.IsConnected())
            Lives[NetPlay.GetMyClientId()] = num;
        else
            for (int i = 0; i < Lives.Length; i++)
                Lives[i] = num;
    }
    public void SwapTankTexture(Texture2D texture) => _tankTexture = texture;
    private int _colorIndex = -1;
    public PlayerTank(int playerType, bool isPlayerModel = true, int copyTier = -1) {
        Model = isPlayerModel ? ModelGlobals.TankPlayer.Asset : ModelGlobals.TankEnemy.Asset;

        Texture2D texAsset;

        // the tank color picked in the Tank Color screen (if any) replaces the standard player texture
        int colorIndex = copyTier == -1 && isPlayerModel ? PlayerColors.ColorIndexFor(playerType) : -1;
        if (colorIndex >= 0)
            texAsset = PlayerColors.MakeTexture(TankGame.Instance.GraphicsDevice, PlayerColors.Palette[colorIndex]);
        else if (copyTier == -1)
            texAsset = Assets[$"plrtank_" + PlayerID.Collection.GetKey(playerType)!.ToLower()];
        else {
            texAsset = Assets[$"tank_" + TankID.Collection.GetKey(copyTier)!.ToLower()];

            Properties = AIManager.GetAITankProperties(copyTier);
        }

        _tankTexture = colorIndex >= 0 ? texAsset : texAsset.Duplicate(TankGame.Instance.GraphicsDevice);
        _colorIndex = colorIndex;

        _isPlayerModel = isPlayerModel;
        PlayerType = playerType;
        Team = TeamID.Red;
        IsDestroyed = true;
        PlayerId = playerType;
        _shadowTexture = GameResources.GetGameResource<Texture2D>("Assets/textures/tank_shadow");

        GameHandler.AllPlayerTanks[PlayerId] = this;

        if (copyTier == -1)
            ApplyDefaults(ref Properties);

        var newTankIndex = Array.IndexOf(GameHandler.AllTanks, null);

        WorldId = newTankIndex;

        GameHandler.AllTanks[newTankIndex] = this;

        base.Initialize();
    }
    public sealed override void ApplyDefaults(ref TankProperties properties) {
        properties.TreadVolume = 0.2f;
        properties.ShellCooldown = 5; // 5
        properties.ShootStun = 5; // 5
        properties.ShellSpeed = 3f; // 3f
        properties.MaxSpeed = 1.8f; // 1.8
        properties.RicochetCount = 1; // 1
        properties.ShellLimit = 5; // 5
        properties.MineCooldown = 6;
        properties.MineLimit = 2; // 2
        properties.MineStun = 1; // 8
        properties.Invisible = false;
        properties.Acceleration = 0.3f;
        properties.Deceleration = 0.6f;
        properties.TurningSpeed = 0.1f;

        // this changes depending on input (or should it?)
        // phones (touch joystick or a handheld's controller) use the keyboard's wider turn window, which feels smooth
        properties.MaximalTurn = MathHelper.ToRadians(InputUtils.CurrentGamePadSnapshot.IsConnected && !TouchControls.Enabled ? 10 : 46); // normally it's 10 degrees, but we want to make it easier for keyboard players.

        Properties.ShootPitch = 0.1f * PlayerType;

        properties.ShellType = ShellID.Player;

        properties.ShellHoming = new();

        properties.DestructionColor = PlayerType switch {
            PlayerID.Blue => Color.Blue,
            PlayerID.Red => Color.Crimson,
            PlayerID.Green => Color.Lime,
            PlayerID.Yellow => Color.Yellow,
            _ => throw new Exception($"The player type with number \"{PlayerType}\" is not mapped to a color!"),
        };
        if (_colorIndex >= 0)
            properties.DestructionColor = PlayerColors.Palette[_colorIndex].Main;

        base.ApplyDefaults(ref properties);

        // Kid Mode: player 2 has a 5-hit shield and homing shots
        if (Difficulties.Types["KidMode"] && PlayerId == PlayerID.Red) {
            properties.Armor = new(this, 5);
            properties.ShellHoming = new() { Radius = 200f, Speed = properties.ShellSpeed, Power = 0.1f * properties.ShellSpeed };
        }
    }
    public override void Update() {
        /*if (Input.KeyJustPressed(Keys.P))
            foreach (var m in TankDeathMark.deathMarks)
                m?.ResurrectTank();*/
        // FIXME: reference?

        // pi/2 = up
        // 0 = down
        // pi/4 = right
        // 3/4pi = left

        DesiredDirection = Vector2.Zero;
        LastUsedController = InputUtils.IsGamepadBeingUsed();

        base.Update();

        if (LevelEditorUI.IsActive || IsDestroyed) return;

        if (IsTurning) {
            if (DesiredChassisRotation - ChassisRotation >= MathHelper.PiOver2) {
                ChassisRotation += MathHelper.Pi;
                Flip = !Flip;
            }
            else if (DesiredChassisRotation - ChassisRotation <= -MathHelper.PiOver2) {
                ChassisRotation -= MathHelper.Pi;
                Flip = !Flip;
            }
        }

        if (NetPlay.IsClientMatched(PlayerId))
            Client.SyncPlayerTank(this);

        // TODO: optimize?
        ProcessPlayerMouse();

        if (!CampaignGlobals.InMission || LevelEditorUI.IsActive || ChatSystem.ActiveHandle) {
            playerControl_isBindPressed = false;
            return;
        }

        bool localCoop = LocalCoop.ControlsPlayer(PlayerId);
        if (localCoop) {
            // same-device co-op: each player has their own input (touch, or their own controller)
            if (!Properties.Stationary && CurShootStun <= 0 && CurMineStun <= 0)
                ControlHandle_LocalCoop();
            if (!GameUI.Paused && (LocalCoop.ShootPressed(PlayerId) || (LocalCoop.UsesTouch(PlayerId) && InputUtils.CanDetectClick())))
                Shoot(false);
            oldPosition = Position;
            return;
        }

        if (!Properties.Stationary) {
            if (NetPlay.IsClientMatched(PlayerId)) {
                if (CurShootStun <= 0 && CurMineStun <= 0) {
                    if (TouchControls.Enabled)
                        ControlHandle_Touch(); // touch joystick and controller sticks share the smooth steering
                    else if (LastUsedController)
                        ControlHandle_ConsoleController();
                    else
                        ControlHandle_Keybinding();
                }
            }
        }

        if (NetPlay.IsClientMatched(PlayerId)) {
            if (InputUtils.CanDetectClick()) {
                if (!ChatSystem.ChatBoxHover && !ChatSystem.ActiveHandle && !GameUI.Paused) {
                    Shoot(false);
                }
            }
            UpdateCharge();
            UpdateGatling();
        }

        oldPosition = Position;
    }
    void ProcessPlayerMouse() {
        if (LocalCoop.ControlsPlayer(PlayerId) && !MainMenuUI.IsActive && !LevelEditorUI.IsActive) {
            var aimWorld = MatrixUtils.GetWorldPosition(LocalCoop.AimScreen(PlayerId), -11f);
            TurretRotation = -(new Vector2(aimWorld.X, aimWorld.Z) - Position).ToRotation() + MathHelper.PiOver2;
            return;
        }
        if (NetPlay.IsClientMatched(PlayerId)) {
            if (!Difficulties.Types["POV"] || TouchControls.Enabled || LevelEditorUI.IsActive || MainMenuUI.IsActive) {
                Vector3 mouseWorldPos = MatrixUtils.GetWorldPosition(MouseUtils.MousePosition, -11f);
                if (!LevelEditorUI.IsActive)
                    TurretRotation = -(new Vector2(mouseWorldPos.X, mouseWorldPos.Z) - Position).ToRotation() + MathHelper.PiOver2;
                else
                    TurretRotation = ChassisRotation;
            }
            else if (!GameUI.Paused) {
                // if (DebugManager.IsFreecamEnabled && InputUtils.MouseRight) { } 
                if (!DebugManager.IsFreecamEnabled && !InputUtils.CanDetectClick() && !controlMine.JustPressed) {
                    var mouseState = Mouse.GetState();
                    var screenCenter = new Point(WindowUtils.WindowWidth / 2, WindowUtils.WindowHeight / 2);

                    if (_justCenteredMouse) {
                        // skip to avoid jumps
                        _justCenteredMouse = false;
                        return;
                    }

                    // subtract mouse delta eventually
                    int deltaX = mouseState.X - screenCenter.X;

                    TurretRotation += -deltaX / (312f.ToResolutionX());

                    // recenter
                    Mouse.SetPosition(screenCenter.X, screenCenter.Y);
                    _justCenteredMouse = true;
                }
            }
        }
    }
    public override void Remove(bool nullifyMe) {
        if (nullifyMe) {
            GameHandler.AllPlayerTanks[PlayerId] = null;
            GameHandler.AllTanks[WorldId] = null;
            _tankTexture?.Dispose();
        }
        base.Remove(nullifyMe);
    }
    /// <summary>How far a controller stick must be pushed before the tank moves. Built-in sticks (like the AYN Thor's)
    /// can rest slightly off-centre, which used to drive the tank on its own.</summary>
    public const float StickDeadZone = 0.35f;

    /// <summary>Tank Troves Charge Shot: when the fire button went down (play time), or -1 while not charging.</summary>
    public double ChargeStart = -1;
    private bool _chargeFiring;
    /// <summary>How full the Charge Shot is (0..1), or -1 while not charging.</summary>
    public float ChargeFraction => ChargeStart < 0 ? -1f : (float)Math.Clamp((Systems.Troves.TroveRun.PlayTime - ChargeStart) / Systems.Troves.TroveItems.CHARGE_SECONDS, 0, 1);

    public override void Shoot(bool fxOnly, bool netSend = true) {
        // Tank Troves Charge Shot: pressing fire starts charging instead (the shot goes when the button is let go)
        if (!fxOnly && !ReplayingNetShot && !_chargeFiring && Systems.Troves.TroveItems.HasChargeShot(PlayerId)
            && TanksRebirth.Internals.Common.Framework.Input.Haptics.IsLocal(this) && !LocalCoop.ControlsPlayer(PlayerId)) {
            if (ChargeStart < 0 && !Systems.Troves.TroveRun.InReady)
                ChargeStart = Systems.Troves.TroveRun.PlayTime;
            return;
        }
        // Tank Troves: every shell this shot makes gets this player's items (whatever the shot was)
        var before = Systems.Troves.TroveRun.Active ? Systems.Troves.TroveItems.MyShells(this) : null;
        ShootInner(fxOnly, netSend);
        if (before is not null) {
            Systems.Troves.TroveItems.AfterShot(this, before);
            if (_chargeFiring)
                Systems.Troves.TroveItems.SuperCharge(this, before);
        }
    }

    /// <summary>Gatling Barrel: while fire is held, keep shooting (faster the longer it's held).</summary>
    private void UpdateGatling() {
        if (!Systems.Troves.TroveItems.HasGatling(PlayerId) || IsDestroyed || !CampaignGlobals.InMission)
            return;
        var pad = InputUtils.CurrentGamePadSnapshot;
        bool held = !GameUI.Paused && (InputUtils.CurrentMouseSnapshot.LeftButton == ButtonState.Pressed || FireBullet.IsPressed
            || pad.IsButtonDown(Buttons.RightShoulder) || pad.Triggers.Right > 0.4f);
        Systems.Troves.TroveItems.GatlingHeld(PlayerId, held);
        if (held && CurShootCooldown <= 0 && !InputUtils.CanDetectClick() && !Systems.Troves.TroveRun.InReady)
            Shoot(false);
    }

    /// <summary>Charge Shot: while charging, watch for the fire button to be let go (full: fire; early: cancel).</summary>
    private void UpdateCharge() {
        if (ChargeStart < 0)
            return;
        if (IsDestroyed || !CampaignGlobals.InMission || !Systems.Troves.TroveItems.HasChargeShot(PlayerId)) {
            ChargeStart = -1;
            return;
        }
        var pad = InputUtils.CurrentGamePadSnapshot;
        bool held = InputUtils.CurrentMouseSnapshot.LeftButton == ButtonState.Pressed || FireBullet.IsPressed
            || pad.IsButtonDown(Buttons.RightShoulder) || pad.Triggers.Right > 0.4f;
        if (held || GameUI.Paused)
            return;
        bool full = ChargeFraction >= 1f;
        ChargeStart = -1;
        if (full && CurShootCooldown <= 0) {
            _chargeFiring = true;
            try { Shoot(false); }
            finally { _chargeFiring = false; }
        }
    }
    private void ShootInner(bool fxOnly, bool netSend) {
        if (!ReplayingNetShot) PlayerStatistics.ShellsShot++; // another player's shot isn't yours
        if (!ReplayingNetShot && !fxOnly && CurShootCooldown <= 0 && TanksRebirth.Internals.Common.Framework.Input.Haptics.IsLocal(this))
            TanksRebirth.Internals.Common.Framework.Input.Haptics.Shoot();
        // Gears of War missile upgrade: this shot is a missile
        if (MissileUpgrade.ShotsLeft(PlayerId) > 0) {
            var (type, bounces, speed) = (Properties.ShellType, Properties.RicochetCount, Properties.ShellSpeed);
            var before = LastShotShell;
            Properties.ShellType = ShellID.Missile;
            Properties.RicochetCount = 0;
            Properties.ShellSpeed = speed * 1.8f; // missiles are fast
            base.Shoot(false);
            (Properties.ShellType, Properties.RicochetCount, Properties.ShellSpeed) = (type, bounces, speed);
            if (LastShotShell is not null && !ReferenceEquals(before, LastShotShell))
                MissileUpgrade.UsedShot(PlayerId);
            return;
        }
        base.Shoot(false);
    }
    public override void LayMine() {
        if (Systems.Troves.TroveRun.InReady)
            return; // "Ready...": not yet
        // Tank Troves slider bombs replace mines while you have them
        if (CurMineCooldown <= 0 && Systems.Troves.TroveItems.TryUseSliderBomb(this)) {
            FireSliderBomb();
            return;
        }
        // Tank Troves Detonator: with mines out, the mine button blows them up instead
        if (Systems.Troves.TroveItems.TryDetonate(this))
            return;
        PlayerStatistics.MinesLaid++;
        int before = OwnedMineCount;
        base.LayMine();
        if (OwnedMineCount > before && TanksRebirth.Internals.Common.Framework.Input.Haptics.IsLocal(this))
            TanksRebirth.Internals.Common.Framework.Input.Haptics.Mine();
        if (OwnedMineCount > before)
            foreach (var m in Mine.AllMines)
                if (m is not null && ReferenceEquals(m.Owner, this) && m.DetonateTime >= m.DetonateTimeMax - 1)
                    Systems.Troves.TroveItems.AfterMine(this, m);
    }
    /// <summary>A slow bomb that slides along the floor the way the turret points, and explodes when it hits something.</summary>
    private void FireSliderBomb() {
        CurMineCooldown = Properties.MineCooldown;
        var dir = Vector2.UnitY.Rotate(TurretRotation);
        var shell = new Shell(TurretPosition, new Vector2(-dir.X, dir.Y) * Properties.ShellSpeed * 0.7f, ShellID.Explosive, this, 0) {
            IsSliderBomb = true,
            ExplodeScale = 9f * Systems.Troves.TroveItems.BlastSize(PlayerId),
            SparesOwner = Systems.Troves.TroveItems.BlastProof(PlayerId),
        };
        Systems.Troves.TroveItems.AfterSliderBomb(this, shell);
        if (NetPlay.IsClientMatched(PlayerId))
            Client.SyncShellFire(shell);
    }
    private void ControlHandle_ConsoleController() {

        var leftStick = InputUtils.CurrentGamePadSnapshot.ThumbSticks.Left;
        var rightStick = InputUtils.CurrentGamePadSnapshot.ThumbSticks.Right;
        var dPad = InputUtils.CurrentGamePadSnapshot.DPad;

        // inverse y because stick down is positive y (which is up z)
        DesiredDirection = new Vector2(leftStick.X, -leftStick.Y);

        var rotationMet = ChassisRotation > DesiredChassisRotation - Properties.MaximalTurn 
            && ChassisRotation < DesiredChassisRotation + Properties.MaximalTurn;

        if (leftStick.Length() > 0) {
            playerControl_isBindPressed = true;
        }

        if (dPad.Down == ButtonState.Pressed) {
            playerControl_isBindPressed = true;
            DesiredDirection.Y = 1;
        }
        if (dPad.Up == ButtonState.Pressed) {
            playerControl_isBindPressed = true;
            DesiredDirection.Y = -1;
        }
        if (dPad.Left == ButtonState.Pressed) {
            playerControl_isBindPressed = true;
            DesiredDirection.X = -1;
        }
        if (dPad.Right == ButtonState.Pressed) {
            playerControl_isBindPressed = true;
            DesiredDirection.X = 1;
        }

        if (!rotationMet) {
            Speed *= Properties.Deceleration * (1f - RuntimeData.DeltaTime);
            IsTurning = true;
            if (Speed < 0)
                Speed = 0;
        }
        else {
            if (Difficulties.Types["POV"])
                DesiredDirection = DesiredDirection.Rotate(-TurretRotation + MathHelper.Pi);

            Speed += Properties.Acceleration * RuntimeData.DeltaTime;
            if (Speed > Properties.MaxSpeed)
                Speed = Properties.MaxSpeed;
        }

        var norm = Vector2.Normalize(DesiredDirection);

        DesiredChassisRotation = norm.ToRotation() - MathHelper.PiOver2;

        ChassisRotation = MathUtils.RoughStep(ChassisRotation, DesiredChassisRotation, Properties.TurningSpeed * RuntimeData.DeltaTime);

        // (on Android the right stick drives TouchControls' free aim cursor instead)
        if (rightStick.Length() > 0 && !TouchControls.Enabled) {
            var unprojectedPosition = MatrixUtils.ConvertWorldToScreen(new Vector3(0, 11, 0), World, View, Projection);
            Mouse.SetPosition((int)(unprojectedPosition.X + rightStick.X * 250), (int)(unprojectedPosition.Y - rightStick.Y * 250));
            //Mouse.SetPosition((int)(Input.CurrentMouseSnapshot.X + rightStick.X * TankGame.Instance.Settings.ControllerSensitivity), (int)(Input.CurrentMouseSnapshot.Y - rightStick.Y * TankGame.Instance.Settings.ControllerSensitivity));
        }

        Velocity = Vector2.UnitY.Rotate(ChassisRotation) * Speed;

        if (FireBullet.JustPressed)
            Shoot(false);
        if (PlaceMine.JustPressed)
            LayMine();
    }
    /// <summary>Movement from the on-screen joystick. Mirrors the keyboard scheme, but with an analog direction.
    /// Aiming and firing come through the synthesized mouse (tap on the right side of the screen).</summary>
    private void ControlHandle_LocalCoop() {
        if (LocalCoop.MinePressed(PlayerId))
            LayMine();
        SteerWith(LocalCoop.Move(PlayerId));
    }
    private void ControlHandle_Touch() {
        if (TouchControls.MineJustPressed || PlaceMine.JustPressed)
            LayMine();
        if (FireBullet.JustPressed)
            Shoot(false);

        IsTurning = false;

        ChassisRotation %= MathHelper.Tau;

        var move = TouchControls.MoveVector;
        // controller: left stick (or d-pad) steers the same way as the touch joystick
        var pad = InputUtils.CurrentGamePadSnapshot;
        if (pad.IsConnected) {
            var ls = pad.ThumbSticks.Left;
            var padMove = new Vector2(ls.X, -ls.Y);
            if (pad.DPad.Left == ButtonState.Pressed) padMove.X = -1;
            if (pad.DPad.Right == ButtonState.Pressed) padMove.X = 1;
            if (pad.DPad.Up == ButtonState.Pressed) padMove.Y = -1;
            if (pad.DPad.Down == ButtonState.Pressed) padMove.Y = 1;
            if (padMove.Length() > StickDeadZone)
                move = padMove;
        }
        SteerWith(move);
    }
    /// <summary>Analog steering shared by the touch joystick and controllers (smooth, no spinning in place).</summary>
    private void SteerWith(Vector2 move) {
        IsTurning = false;
        ChassisRotation %= MathHelper.Tau;
        if (move != Vector2.Zero) {
            playerControl_isBindPressed = true;
            // any push moves at full speed; only the direction counts. A light push wobbles more, so its
            // direction is smoothed a little (a full push turns right away)
            var dir = Vector2.Normalize(move);
            float push = Math.Clamp(move.Length(), 0f, 1f);
            if (push < 0.6f && DesiredDirection != Vector2.Zero && !float.IsNaN(DesiredDirection.X) && Vector2.Dot(Vector2.Normalize(DesiredDirection), dir) > 0.5f) {
                var blended = Vector2.Lerp(Vector2.Normalize(DesiredDirection), dir, 0.25f + push);
                if (blended.LengthSquared() > 1e-4f)
                    dir = Vector2.Normalize(blended);
            }
            DesiredDirection = dir;
        }

        if (Difficulties.Types["POV"])
            DesiredDirection = DesiredDirection.Rotate(-TurretRotation + MathHelper.Pi);

        // a zero direction normalizes to NaN, which the base tank treats as "not moving" (same as no keys held)
        var norm = Vector2.Normalize(DesiredDirection);

        DesiredChassisRotation = norm.ToRotation() - MathHelper.PiOver2;

        ChassisRotation = MathUtils.RoughStep(ChassisRotation, DesiredChassisRotation, Properties.TurningSpeed * RuntimeData.DeltaTime);

        Velocity = Vector2.UnitY.Rotate(ChassisRotation) * Speed;
    }
    private void ControlHandle_Keybinding() {
        if (controlFirePath.JustPressed)
            _drawShotPath = !_drawShotPath;
        if (controlMine.JustPressed)
            LayMine();

        IsTurning = false;

        //var rotationMet = TankRotation > TargetTankRotation - Properties.MaximalTurn && TankRotation < TargetTankRotation + Properties.MaximalTurn;

        ChassisRotation %= MathHelper.Tau;

        if (controlDown.IsPressed) {
            playerControl_isBindPressed = true;
            DesiredDirection.Y = 1;
            LastUsedController = false;
        }
        if (controlUp.IsPressed) {
            playerControl_isBindPressed = true;
            DesiredDirection.Y = -1;
            LastUsedController = false;
        }
        if (controlLeft.IsPressed) {
            playerControl_isBindPressed = true;
            DesiredDirection.X = -1;
            LastUsedController = false;
        }
        if (controlRight.IsPressed) {
            playerControl_isBindPressed = true;
            DesiredDirection.X = 1;
            LastUsedController = false;
        }

        if (Difficulties.Types["POV"])
            DesiredDirection = DesiredDirection.Rotate(-TurretRotation + MathHelper.Pi);

        var norm = Vector2.Normalize(DesiredDirection);

        DesiredChassisRotation = norm.ToRotation() - MathHelper.PiOver2;

        ChassisRotation = MathUtils.RoughStep(ChassisRotation, DesiredChassisRotation, Properties.TurningSpeed * RuntimeData.DeltaTime);

        Velocity = Vector2.UnitY.Rotate(ChassisRotation) * Speed;
    }
    public override void Destroy(ITankHurtContext context, bool netSend) {
        // Kid Mode: player 2 never runs out of lives. Gears of War: a fallen player is "down" (waiting to be
        // revived) rather than losing a life; if everyone is down, it's game over.
        bool keepLives = (Difficulties.Types["KidMode"] && PlayerId == PlayerID.Red) || Difficulties.Types["GearsOfWar"];
        if (Difficulties.Types["GearsOfWar"] && CampaignGlobals.InMission)
            ReviveSystem.PlayerDown(this);
        AchievementStore.PlayerDestroyed(this, context);
        if (keepLives) { }
        else if (LocalCoop.ControlsPlayer(PlayerId))
            Lives[PlayerId]--; // same-device co-op: each player has their own lives
        else if (Client.IsConnected()) {
            // maybe make a camera transition to said tank.

            //if (context.Source is not null)
                //CameraGlobals.SpectatorId = CameraGlobals.SpectateValidTank(context.Source.WorldId, true);

            // only decements the lives on the destroyed player's system, where lives are synced across everyone at all times
            if (NetPlay.IsClientMatched(PlayerId)) {
                Lives[PlayerId]--;
            }
        }
        else
            AddLives(-1);

        Remove(false);

        var c = PlayerType switch {
            PlayerID.Blue => TankDeathMark.CheckColor.Blue,
            PlayerID.Red => TankDeathMark.CheckColor.Red,
            PlayerID.Green => TankDeathMark.CheckColor.Green,
            PlayerID.Yellow => TankDeathMark.CheckColor.Yellow, // TODO: change these colors.
            _ => throw new Exception($"Player Death Mark for colour {PlayerType} is not supported."),
        };

        var playerDeathMark = new TankDeathMark(c) {
            Position = Position3D + new Vector3(0, 0.1f, 0),
        };

        playerDeathMark.StoredTank = new TankTemplate {
            IsPlayer = true,
            Position = playerDeathMark.Position.FlattenZ(),
            Rotation = ChassisRotation,
            Team = Team,
            PlayerType = PlayerType,
        };

        base.Destroy(context, netSend);

        if (context.Source is not PlayerTank player) return;

        // only increment these data values on the destroyed player's system
        // ensure the source tank is 
        if (NetPlay.IsClientMatched(player.PlayerId)) {
            TankGame.SaveFile.Suicides++;
            PlayerStatistics.Suicides++;
        }
        TankGame.SaveFile.Deaths++;
    }
    private void DrawShootPath() {
        const int MAX_PATH_UNITS = 10000;

        var whitePixel = TextureGlobals.Pixels[Color.White];
        var pathPos = Position + new Vector2(0, 18).Rotate(-TurretRotation);
        var pathDir = Vector2.UnitY.Rotate(TurretRotation - MathHelper.Pi);
        pathDir.Y *= -1;
        pathDir *= Properties.ShellSpeed;

        var pathRicochetCount = 0;


        for (int i = 0; i < MAX_PATH_UNITS; i++) {
            var dummyPos = Vector2.Zero;

            if (pathPos.X < GameScene.MIN_X || pathPos.X > GameScene.MAX_X) {
                pathRicochetCount++;
                pathDir.X *= -1;
            }
            if (pathPos.Y < GameScene.MIN_Z || pathPos.Y > GameScene.MAX_Z) {
                pathRicochetCount++;
                pathDir.Y *= -1;
            }

            var pathHitbox = new Rectangle((int)pathPos.X - 3, (int)pathPos.Y - 3, 6, 6);

            // Why is velocity passed by reference here lol
            Collision.HandleCollisionSimple_ForBlocks(pathHitbox, pathDir, ref dummyPos, out var dir, out var block, out bool corner, false, (c) => c.Properties.IsSolid);

            if (corner)
                return;
            if (block != null) {
                if (block.Properties.AllowShotPathBounce) {
                    switch (dir) {
                        case CollisionDirection.Up:
                        case CollisionDirection.Down:
                            pathDir.Y *= -1;
                            pathRicochetCount += block.Properties.PathBounceCount;
                            break;
                        case CollisionDirection.Left:
                        case CollisionDirection.Right:
                            pathDir.X *= -1;
                            pathRicochetCount += block.Properties.PathBounceCount;
                            break;
                    }
                }
            }

            var cannotBounce = pathRicochetCount > Properties.RicochetCount;
            if (cannotBounce) return;
            var tankInPath = GameHandler.AllTanks.FirstOrDefault(
                tnk => tnk is not null && !tnk.IsDestroyed && tnk.CollisionCircle.Intersects(new Circle { Center = pathPos, Radius = 4 }));
            if (Array.IndexOf(GameHandler.AllTanks, tankInPath) > -1 && tankInPath is not null) {
                TanksSpotted = [tankInPath!];
                return;
            }
            TanksSpotted = [];

            pathPos += pathDir;

            var pathPosScreen = MatrixUtils.ConvertWorldToScreen(Vector3.Zero, Matrix.CreateTranslation(pathPos.X, 11, pathPos.Y), CameraGlobals.GameView, CameraGlobals.GameProjection);
            var off = MathF.Abs(MathF.Sin(i * MathF.PI / 5 - RuntimeData.RunTime * 0.3f));
            var rgbColor = ColorUtils.HsvToRgb(RuntimeData.UpdateCount + i % 255 / 255f * 360, 1, 1);
            var scale = Vector2.One * 4 * off;
            DrawUtils.DrawTextureWithBorder(TankGame.SpriteRenderer, whitePixel, pathPosScreen, Color.Black,
                rgbColor, scale, 0f, Anchor.Center, 1f);
            //TankGame.SpriteRenderer.Draw(whitePixel, pathPosScreen, null, ColorUtils.HsvToRgb(RuntimeData.UpdateCount + i % 255 / 255f * 360, 1, 1), 0, whitePixel.Size() / 2, new Vector2(3 + off).ToResolution(), default, default);
        }
    }
    public override void Render() {
        base.Render();
        if (IsDestroyed)
            return;
        DrawExtras();
        if (Properties.Invisible && CampaignGlobals.InMission)
            return;
        for (int i = 0; i < (Lighting.AccurateShadows ? 2 : 1); i++) {
            foreach (ModelMesh mesh in Model.Meshes) {
                foreach (BasicEffect effect in mesh.Effects) {
                    effect.World = i == 0 ? boneTransforms[mesh.ParentBone.Index] : 
                        boneTransforms[mesh.ParentBone.Index] 
                        * Matrix.CreateShadow(Lighting.AccurateLightingDirection, new(Vector3.UnitY, 0)) * Matrix.CreateTranslation(0, 0.2f, 0);
                    effect.View = View;
                    effect.Projection = Projection;
                    effect.TextureEnabled = true;

                    if (!Properties.HasTurret)
                        if (mesh.Name == "Cannon")
                            return;

                    if (mesh.Name == "Shadow") {
                        if (!Lighting.AccurateShadows) {
                            effect.Alpha = 0.5f;
                            effect.Texture = _shadowTexture;
                            mesh.Draw();
                        }
                        continue;
                    }

                    effect.Alpha = 1f;
                    effect.Texture = _tankTexture;

                    if (IsHoveredByMouse)
                        effect.EmissiveColor = Color.White.ToVector3();
                    else
                        effect.EmissiveColor = Color.Black.ToVector3();
                    effect.SetDefaultGameLighting_IngameEntities(specular: _isPlayerModel, ambientMultiplier: _isPlayerModel ? 2f : 0.9f);
                    ApplyTeamTint(effect, mesh, Team);
                    mesh.Draw();
                }
            }
        }
    }
    static readonly Dictionary<int, float[]> _bulletAnim = [];
    /// <summary>(from 1.9) Every player's shells-left, not just your own: one column per player down the right edge,
    /// in that player's colour, with shells shrinking away when fired and popping back when they return.
    /// Columns stay clear of the phone buttons (they never reach the joystick area) and squeeze for big shell limits.</summary>
    private void DrawBulletCount() {
        int limit = Properties.ShellLimit;
        if (limit <= 0) return;
        var sb = TankGame.SpriteRenderer;
        var tex = GameResources.GetGameResource<Texture2D>("Assets/textures/ui/bullet_ui");
        float res = WindowUtils.WindowHeight / 1080f;

        // how many players are in the game decides whether the columns get a "P1" label
        int players = GameHandler.AllPlayerTanks.Count(t => t is not null && !t.IsDestroyed);
        // the column's height is capped at ~40% of the screen, shrinking the icons for big shell limits
        float baseScale = 0.5f * MathF.Min(1f, 5f / limit);
        float step = tex.Height * baseScale * res + 5f * res;
        float maxH = WindowUtils.WindowHeight * 0.4f;
        if (step * limit > maxH) { float k = maxH / (step * limit); baseScale *= k; step *= k; }

        float colW = MathF.Max(tex.Width * 0.5f * res, 36f * res) + 10f * res;
        float x = WindowUtils.WindowWidth - 20f * res - colW / 2f - PlayerId * colW;
        float y = 20f * res;

        var bright = PlayerID.PlayerTankColors.TryGetValue(PlayerType, out var pc) ? Color.Lerp(pc, Color.White, 0.35f) : Color.White;
        if (players > 1) {
            var label = $"P{PlayerId + 1}";
            DrawUtils.DrawStringWithBorder(sb, FontGlobals.RebirthFont, label, new Vector2(x, y + 12f * res), bright, Color.Black,
                new Vector2(0.55f * res), 0f, borderThickness: 1.5f * res);
            y += 32f * res;
        }

        if (!_bulletAnim.TryGetValue(PlayerId, out var anim) || anim.Length != limit) {
            anim = new float[limit];
            Array.Fill(anim, 1f); // full, so they don't pop in at the start
            _bulletAnim[PlayerId] = anim;
        }
        for (int i = 0; i < limit; i++) {
            float target = i < OwnedShellCount ? 0f : 1f;
            anim[i] = MathHelper.Lerp(anim[i], target, MathHelper.Clamp(0.08f * RuntimeData.DeltaTime, 0f, 1f));
            float v = target > 0.5f ? Easings.OutBack(anim[i]) : Easings.OutQuart(anim[i]);
            var color = Color.Lerp(Color.DimGray * 0.5f, bright, MathHelper.Clamp(v, 0f, 1f));
            float scale = MathHelper.Lerp(baseScale * 0.7f, baseScale, v) * res;
            DrawUtils.DrawTextureWithBorder(sb, tex, new Vector2(x, y + step / 2f + i * step), color,
                Color.White * MathHelper.Clamp(v, 0f, 1f), new Vector2(scale), 0f, Anchor.Center, borderThickness: 0.5f + MathF.Min(1f, 5f / limit));
        }
    }
    private void DrawExtras() {
        if (IsDestroyed)
            return;

        if (!MainMenuUI.IsActive && !LevelEditorUI.IsActive)
            DrawBulletCount();

        // a bit hardcoded but whatever
        bool needClarification = (!MainMenuUI.IsActive && IntermissionHandler.TankFunctionWait > 0) || MainMenuUI.MenuState == MainMenuUI.UIState.Mulitplayer;

        if (needClarification) {
            var playerColor = PlayerID.PlayerTankColors[PlayerType];
            var pos = MatrixUtils.ConvertWorldToScreen(Vector3.Zero, World, View, Projection) - new Vector2(0, 50).ToResolution();

            bool flip = false;

            float rotation = 0f;

            // flip the graphic so it doesn't appear offscreen if it would normally appear too high
            if (pos.Y <= 150) {
                flip = true;
                pos.Y += 75;
                rotation = MathHelper.Pi;
            }
            var tex1 = GameResources.GetGameResource<Texture2D>("Assets/textures/ui/chevron_border");
            var tex2 = GameResources.GetGameResource<Texture2D>("Assets/textures/ui/chevron_inside");

            // var p = GameHandler.AllPlayerTanks;
            //string pText = "nerd";
            var scale = 0.3f;
            string pText = Client.IsConnected() ? Server.ConnectedClients[PlayerId].Name : $"P{PlayerId + 1}"; // heeheeheeha

            TankGame.SpriteRenderer.Draw(tex1, pos, null, Color.White, rotation, Anchor.BottomCenter.GetAnchor(tex1.Size()), scale.ToResolution(), default, default);
            TankGame.SpriteRenderer.Draw(tex2, pos, null, playerColor, rotation, Anchor.BottomCenter.GetAnchor(tex2.Size()), scale.ToResolution(), default, default);

            DrawUtils.DrawStringWithBorder(TankGame.SpriteRenderer, FontGlobals.RebirthFontLarge, pText, new(pos.X, pos.Y + (flip ? 90 : -110).ToResolutionY()), playerColor, Color.White, new Vector2(scale * 2).ToResolution(), 0f, Anchor.Center, 2f);
        }

        if (DebugManager.DebugLevel == 1 || _drawShotPath)
            DrawShootPath();

        if (Properties.Invisible && CampaignGlobals.InMission)
            return;

        Properties.Armor?.Render();
    }
    public override string ToString()
        => $"pos: {Position} | vel: {Velocity} | dead: {IsDestroyed} | rotation: {ChassisRotation} | OwnedBullets: {OwnedShellCount}";
}