using System;
using System.Linq;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.GameContent.UI;
using TanksRebirth.GameContent.UI.MainMenu;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.Troves;

/// <summary>
/// A Tank Troves run: 5 floors of rooms. Clear a room and its doors open; drive into a door to move on.
/// Each floor ends with a boss; beating the last one wins the run. Lives carry over between rooms: if everyone
/// goes down, the room starts over (and costs a life) until the lives run out.
/// In multiplayer the host decides (rooms, retries, floors) and the other phones follow.
/// </summary>
public static class TroveRun {
    public const int STARTING_LIVES = 3;
    /// <summary>How close (game units) a tank has to get to a door to go through it.</summary>
    public const float DOOR_REACH = 36f;

    public enum EndKind { None, RunOver, Victory }
    // multiplayer events (host -> everyone)
    public const int EV_START = 1, EV_MOVE = 2, EV_RETRY = 3, EV_NEXT_FLOOR = 4, EV_RUN_OVER = 5, EV_VICTORY = 6, EV_MENU = 7, EV_ITEM = 8, EV_BLOCK_BUST = 9,
        EV_REROLL = 10, EV_SECRET = 11, EV_WAVE2 = 12, EV_STOPWATCH = 13, EV_NUKE = 14, EV_DECOY = 15, EV_INVINCIBLE = 16,
        EV_WALL = 17, EV_CONVERT = 18, EV_SHRINK = 19, EV_BLAST = 20;
    /// <summary>How close (game units) a tank has to get to an item to pick it up.</summary>
    public const float PICKUP_REACH = 26f;
    public static Vector2 PickupPoint => new Coordinates.BlockMapPosition(10, 8);

    // the boss of the current room (for the health bar at the top)
    public static AI.AITank? BossTank { get; private set; }
    public static int BossMaxHits { get; private set; }
    /// <summary>What the health bar at the top says (a floor's boss, or a mini-boss).</summary>
    public static string BossTitle { get; private set; } = "";
    public static readonly string[] BossNames = ["Captain Marine", "The Violet Viper", "Green Goliath", "The Black Baron", "Obsidian Overlord"];
    private static bool _hooked;

    public static bool Active { get; private set; }
    public static int Seed { get; private set; }
    public static int FloorNumber { get; private set; }
    public static TroveFloor? Floor { get; private set; }
    public static Point Current { get; private set; }
    public static TroveRoom? Room => Floor is not null && Floor.Rooms.TryGetValue(Current, out var r) ? r : null;
    public static EndKind Ended { get; private set; }
    public static bool ShowingEnd => Active && Ended != EndKind.None;

    public static int RoomsCleared, TanksDestroyed;
    public static double PlayTime;

    private static bool _starting;
    private static TroveDir? _entry;
    private static float _roomTime, _deadTimer, _bossTimer, _logTimer;
    // fading between rooms: 1 = going dark, -1 = coming back
    private static int _fadeDir;
    public static float Fade { get; private set; }
    private static Action? _atBlack;

    private static bool Authority => !Client.IsConnected() || Client.IsHost();
    private static float Dt => (float)TankGame.LastGameTime.ElapsedGameTime.TotalSeconds;

    // ------------------------------------------------------------------ starting

    /// <summary>The Tank Troves button: start a new run (online, the host's phone starts it for everyone).</summary>
    public static void StartFromMenu() {
        if (Client.IsConnected() && !Client.IsHost()) {
            ChatSystem.SendMessage("Only the host can start a run.", Color.Orange);
            SoundPlayer.SoundError();
            return;
        }
        int seed = TroveGen.NewSeed();
        if (Client.IsConnected())
            Client.SendTroveEvent(EV_START, seed);
        Begin(seed);
    }

    public static void Begin(int seed) {
        Active = true;
        _starting = true;
        Seed = seed;
        FloorNumber = 1;
        Floor = TroveGen.Generate(seed, 1);
        Current = Floor.Start;
        SetFloorTint();
        Ended = EndKind.None;
        RoomsCleared = TanksDestroyed = 0;
        PlayTime = 0;
        _entry = null;
        _fadeDir = 0;
        Fade = 0;
        _atBlack = null;
        _deadTimer = _bossTimer = _roomTime = 0;
        MarkSeen();

        var campaign = new Campaign();
        campaign.MetaData = campaign.MetaData with { Name = "Tank Troves", StartingLives = STARTING_LIVES };
        campaign.CachedMissions = new Mission[TroveGen.FLOORS];
        for (int i = 0; i < TroveGen.FLOORS; i++)
            campaign.CachedMissions[i] = new Mission([], []) { Name = $"Floor {i + 1}" };
        campaign.CachedMissions[0] = TroveGen.BuildMission(Floor, Room!, null);
        CampaignGlobals.LoadedCampaign = campaign;
        campaign.LoadMission(0);

        LevelProgress.CurrentCampaignKey = "TankTroves";
        MissileUpgrade.Reset();
        TroveItems.Reset();
        TestSetup();
        RevealMap();
        BossTank = null;
        if (!_hooked) {
            _hooked = true;
            // a floor's first room is set up by the campaign's own sequence: give the players their items then
            CampaignGlobals.OnMissionStart += () => { if (Active) { ApplyItems(); TroveItems.SpawnBuddies(); } };
            // the new floor's colour appears when its first room is set up (behind the floor banner), not when the boss falls
            Campaign.OnMissionLoad += (_, _) => { if (Active) SetFloorTint(); };
        }
        ReviveSystem.Clear();
        PlayerTank.StartingLives = Difficulties.Types["GearsOfWar"] ? 1 : STARTING_LIVES;
        IntermissionSystem.BannerColor = new Color(92, 60, 140);
        IntermissionSystem.BackgroundColor = new Color(222, 210, 182);

        MainMenuUI.LaunchCustomGame();
        // rooms and floors are handled here, not by the campaign's "mission complete" logic
        CampaignGlobals.ShouldMissionsProgress = false;
    }

    /// <summary>Each floor's floor colour: it gets darker and stranger the deeper you go.</summary>
    public static readonly Vector3[] FloorTints = [
        new(1.00f, 1.00f, 1.00f), // The Cellar: the usual wood
        new(1.00f, 0.82f, 0.62f), // The Pantry: warm amber
        new(0.70f, 0.80f, 0.92f), // The Workshop: cold steel blue
        new(0.82f, 0.52f, 0.48f), // The Armory: rusty red
        new(0.55f, 0.44f, 0.72f), // The Vault: deep violet
    ];
    private static void SetFloorTint() =>
        Graphics.GameScene.FloorTint = FloorTints[Math.Clamp(FloorNumber - 1, 0, FloorTints.Length - 1)];

    public static void Stop() {
        Graphics.GameScene.FloorTint = Vector3.One;
        Active = false;
        _starting = false;
        Ended = EndKind.None;
        Fade = 0;
        _fadeDir = 0;
    }

    /// <summary>
    /// For automated testing only: if "troves_test.txt" is in the save folder, player 1 starts with the items it lists
    /// (one line: names separated by spaces) and the second line names an item to put on the floor of the first room.
    /// </summary>
    private static bool _testHoldPickup;
    private static void TestSetup() {
        _testHoldPickup = false;
        try {
            var path = System.IO.Path.Combine(TankGame.SaveDirectory, "troves_test.txt");
            if (!System.IO.File.Exists(path) || Floor is null)
                return;
            var lines = System.IO.File.ReadAllLines(path);
            foreach (var name in lines.ElementAtOrDefault(0)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [])
                if (Enum.TryParse<TroveItemId>(name, out var id))
                    TroveItems.Give(0, id);
            if (Enum.TryParse<TroveItemId>(lines.ElementAtOrDefault(1)?.Trim(), out var floorItem)) {
                Floor.Rooms[Floor.Start].Pickup = floorItem;
                _testHoldPickup = true; // players start on top of it: leave it there so its label shows
            }
            TroveItems.LastPickup = null;
            TankGame.ClientLog.Write($"Troves: test setup from {path}", Internals.LogType.Info);
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Troves: test setup failed: {e.Message}", Internals.LogType.Warn);
        }
    }

    /// <summary>Treasure Map: every room of the floor shows on the minimap (the secret one too, marked "?").</summary>
    public static void RevealMap() {
        if (Floor is null || !TroveItems.Players.Any(p => p.Has(TroveItemId.TreasureMap)))
            return;
        foreach (var r in Floor.Rooms.Values)
            r.Seen = true;
    }

    /// <summary>Reroll: a different item on this room's floor.</summary>
    public static void SetPickup(TroveItemId id) {
        if (Room is not { } room)
            return;
        room.Pickup = id;
        room.PickupTaken = false;
        GameHandler.Particles.MakeShineSpot(new Vector3(PickupPoint.X, 12, PickupPoint.Y), Color.White, 1.5f);
    }

    /// <summary>Each room starts the music for the tanks in it (the game normally only does this when a mission starts).</summary>
    private static void RestartMusic() {
        TankMusicSystem.StopAll();
        TankMusicSystem.PlayAll();
    }

    /// <summary>The secret room's doorway is open now.</summary>
    private static void RevealSecret() {
        if (Floor is null || Floor.SecretFound || Floor.Secret is not { } sp)
            return;
        Floor.SecretFound = true;
        Floor.Rooms[sp].Seen = true;
        // the cork walls in front of it stay gone (also when you come back to those rooms)
        foreach (var r in Floor.Rooms.Values)
            if (TroveGen.SecretDoor(Floor, r) is { } d)
                r.Blocks.RemoveAll(b => TroveGen.InCrack(b.X, b.Y, d));
        SoundPlayer.PlaySoundInstance("Assets/music/fanfares/life_get.ogg", SoundContext.Effect, 0.6f, pitchOverride: -0.3f);
        ChatSystem.SendMessage("You found a secret room!", Color.Gold);
    }

    /// <summary>Challenge rooms: the second wave arrives.</summary>
    private static void SpawnWave2(TroveRoom room) {
        if (room.Wave2Spawned)
            return;
        room.Wave2Spawned = true;
        foreach (var e in room.Wave2) {
            var t = new TankTemplate {
                IsPlayer = false, AiTier = e.Tier, Rotation = e.Rotation, Team = TeamID.Blue,
                Position = new Coordinates.BlockMapPosition(e.X, e.Y),
            }.GetAiTank();
            t.Team = TeamID.Blue;
            GameHandler.Particles.MakeShineSpot(t.Position3D + new Vector3(0, 10, 0), Color.Red, 1.2f);
        }
        _roomTime = 0;
        RestartMusic();
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", SoundContext.Effect, 0.8f, pitchOverride: -0.5f);
        ChatSystem.SendMessage("Wave 2!", Color.OrangeRed);
    }

    private static void MarkSeen() {
        if (Floor is null || Room is not { } room)
            return;
        room.Visited = true;
        room.Seen = true;
        foreach (var d in TroveGen.Doors(Floor, room)) {
            var s = TroveGen.Step(d);
            Floor.Rooms[new Point(Current.X + s.X, Current.Y + s.Y)].Seen = true;
        }
    }

    // ------------------------------------------------------------------ every update

    public static void Update() {
        if (!Active)
            return;
        if (MainMenuUI.IsActive) {
            if (!_starting)
                Stop(); // quit from the pause menu
            return;
        }
        _starting = false;
        GameUI.RestartButton.IsVisible = false; // a run can't be "restarted" like a campaign mission

        // a status line in the game log every few seconds (helps when testing)
        _logTimer += Dt;
        if (_logTimer > 3f) {
            _logTimer = 0;
            var me = GameHandler.AllPlayerTanks[NetPlay.GetMyClientId()];
            float nearest = Room is { } lr && Floor is not null && me is not null
                ? TroveGen.Doors(Floor, lr).Select(d => Vector2.Distance(me.Position, DoorPoint(d))).DefaultIfEmpty(-1).Min() : -1;
            TankGame.ClientLog.Write($"Troves: floor {FloorNumber} room {Current} inMission={CampaignGlobals.InMission} cleared={Room?.Cleared} " +
                $"map={Room?.CampaignMap ?? "-"} fade={Fade:0.00}/{_fadeDir} paused={GameUI.Paused} ended={Ended} dt={Dt:0.000} me={me?.Position} nearestDoor={nearest:0}", Internals.LogType.Info);
        }

        if (_fadeDir != 0) {
            Fade = Math.Clamp(Fade + _fadeDir * Dt * 4f, 0f, 1f);
            if (_fadeDir > 0 && Fade >= 1f) {
                var act = _atBlack;
                _atBlack = null;
                act?.Invoke();
                _fadeDir = -1;
            }
            else if (_fadeDir < 0 && Fade <= 0f)
                _fadeDir = 0;
        }

        TroveItems.Update();
        if (!CampaignGlobals.InMission || Ended != EndKind.None || GameUI.Paused || Room is not { } room)
            return;
        PlayTime += Dt;
        _roomTime += Dt;

        // a room is cleared when its enemies are gone
        bool enemies = GameHandler.AllAITanks.Any(t => t is not null && !t.IsDestroyed && t.Team != TeamID.Red);
        if (!room.Cleared && !enemies && _roomTime > 0.6f && _fadeDir == 0 && room.Kind == TroveRoomKind.Challenge && !room.Wave2Spawned) {
            // a challenge room's first wave is down: the host sends in the second
            if (Authority) {
                Send(EV_WAVE2);
                SpawnWave2(room);
            }
        }
        else if (!room.Cleared && !enemies && _roomTime > 0.6f && _fadeDir == 0) {
            room.Cleared = true;
            TroveItems.OnRoomCleared();
            RoomsCleared++;
            TanksDestroyed += room.Enemies.Count + room.Wave2.Count;
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", SoundContext.Effect, 0.8f);
            if (room.Kind == TroveRoomKind.Boss)
                _bossTimer = 2.5f;
        }

        // an explosion next to the hidden doorway opens the secret room
        if (Floor is { SecretFound: false } && TroveGen.SecretDoor(Floor, room) is { } sd) {
            var door = DoorPoint(sd);
            // ...or once the cork wall in front of it is shot away
            bool wallGone = _roomTime > 1f && !Block.AllBlocks.Any(b => b is not null && b.Properties.IsDestructible && Vector2.Distance(b.Position, door) < 60f);
            if (wallGone) {
                SendAny(EV_SECRET);
                RevealSecret();
            }
            else foreach (var ex in Explosion.Explosions)
                if (ex is not null && Vector2.Distance(ex.Position, door) < 80f + ex.Scale * Explosion.MAGIC_EXPLOSION_NUMBER * 0.5f) {
                    SendAny(EV_SECRET);
                    RevealSecret();
                    break;
                }
        }

        if (!Authority || _fadeDir != 0)
            return;

        // everyone down: try the room again (a life is gone), or the run is over
        bool anyAlive = GameHandler.AllPlayerTanks.Any(p => p is not null && !p.IsDestroyed);
        if (!anyAlive) {
            _deadTimer += Dt;
            if (_deadTimer > 1.8f) {
                _deadTimer = 0;
                bool livesLeft = !Difficulties.Types["GearsOfWar"] && Enumerable.Range(0, 4)
                    .Any(i => GameHandler.AllPlayerTanks[i] is not null && PlayerTank.Lives[i] > 0);
                if (livesLeft) {
                    Send(EV_RETRY);
                    Retry();
                }
                else {
                    Send(EV_RUN_OVER);
                    End(EndKind.RunOver);
                }
            }
            return;
        }
        _deadTimer = 0;

        // the boss is down: on to the next floor (or the run is won)
        if (room.Kind == TroveRoomKind.Boss && room.Cleared && _bossTimer > 0) {
            _bossTimer -= Dt;
            if (_bossTimer <= 0) {
                if (FloorNumber >= TroveGen.FLOORS) {
                    Send(EV_VICTORY);
                    End(EndKind.Victory);
                }
                else {
                    Send(EV_NEXT_FLOOR);
                    NextFloor();
                }
            }
            return;
        }

        // picking up an item
        if (room.PickupVisible && !_testHoldPickup) {
            foreach (var p in GameHandler.AllPlayerTanks) {
                if (p is null || p.IsDestroyed || Vector2.Distance(p.Position, PickupPoint) > PICKUP_REACH)
                    continue;
                Send(EV_ITEM, p.PlayerId);
                TakePickup(p.PlayerId);
                break;
            }
        }

        // through a door
        if (!room.Cleared || Floor is null)
            return;
        foreach (var d in TroveGen.Doors(Floor, room)) {
            var door = DoorPoint(d);
            foreach (var p in GameHandler.AllPlayerTanks) {
                if (p is null || p.IsDestroyed || Vector2.Distance(p.Position, door) > DOOR_REACH)
                    continue;
                Send(EV_MOVE, (int)d);
                Move(d);
                return;
            }
        }
    }

    /// <summary>The middle of a room's edge on that side (game units).</summary>
    public static Vector2 DoorPoint(TroveDir d) => d switch {
        TroveDir.North => new(0, Graphics.GameScene.MIN_Z),
        TroveDir.South => new(0, Graphics.GameScene.MAX_Z),
        TroveDir.West => new(Graphics.GameScene.MIN_X, 0),
        _ => new(Graphics.GameScene.MAX_X, 0),
    };

    // ------------------------------------------------------------------ actions (run on every phone)

    private static void FadeThen(Action act) {
        _atBlack = act;
        _fadeDir = 1;
    }

    private static void Move(TroveDir d) {
        if (Floor is null)
            return;
        var s = TroveGen.Step(d);
        var target = new Point(Current.X + s.X, Current.Y + s.Y);
        if (!Floor.Has(target))
            return;
        FadeThen(() => {
            Current = target;
            LoadRoom(TroveGen.Opposite(d));
        });
    }

    private static void Retry() => FadeThen(() => LoadRoom(_entry));

    private static void LoadRoom(TroveDir? entry) {
        if (Floor is null || Room is not { } room)
            return;
        _entry = entry;
        _roomTime = 0;
        _deadTimer = 0;
        MarkSeen();
        ReviveSystem.Clear();
        if (!room.Cleared)
            room.Wave2Spawned = false; // a challenge starts over from wave 1
        var mission = TroveGen.BuildMission(Floor, room, entry);
        CampaignGlobals.LoadedCampaign.LoadMission(mission);
        CampaignGlobals.LoadedCampaign.SetupLoadedMission(true);
        ApplyItems();
        TroveItems.SpawnBuddies();
        RestartMusic();
        // what normally happens when a mission starts (invisible tanks vanish, free-for-all teams)
        foreach (var t in GameHandler.AllTanks)
            if (t is not null && !t.IsDestroyed)
                t.DoInvisibilityGFXandSFX();
        BuffBoss(room);
    }

    private static void ApplyItems() {
        foreach (var p in GameHandler.AllPlayerTanks)
            if (p is not null && !p.IsDestroyed && TroveItems.IsLocal(p.PlayerId))
                TroveItems.ApplyTo(p);
    }

    private static void TakePickup(int playerId) {
        if (Room is not { Pickup: { } item } room || room.PickupTaken)
            return;
        room.PickupTaken = true;
        TroveItems.Give(playerId, item);
    }

    /// <summary>The boss: bigger, armored (a health bar at the top), and better armed than a normal tank of its type.</summary>
    private static void BuffBoss(TroveRoom room) {
        BossTank = null;
        if (TroveGen.BossPosition(room) is not { } pos)
            return;
        var boss = GameHandler.AllAITanks.Where(t => t is not null && !t.IsDestroyed && t.Team != TeamID.Red)
            .OrderBy(t => Vector2.Distance(t!.Position, pos)).FirstOrDefault();
        if (boss is null)
            return;
        if (room.Kind == TroveRoomKind.MiniBoss) {
            // a tougher-than-usual tank: armored, a bit bigger, one more shell
            boss.Scaling = new Vector3(1.25f);
            boss.Properties.Armor = new TankArmor(boss, 2 + FloorNumber);
            BossTank = boss;
            BossMaxHits = 2 + FloorNumber + 1;
            boss.Properties.ShellLimit += 1;
            BossTitle = "Mini-boss";
            return;
        }
        boss.Scaling = new Vector3(1.45f);
        boss.Properties.Armor = new TankArmor(boss, 4 + FloorNumber * 2);
        BossTank = boss;
        BossMaxHits = 4 + FloorNumber * 2 + 1;
        boss.Properties.ShellLimit += 2;
        boss.Properties.MineLimit += 1;
        BossTitle = BossNames[Math.Clamp(FloorNumber - 1, 0, BossNames.Length - 1)];
    }

    private static void NextFloor() {
        FloorNumber++;
        TroveItems.NewFloor();
        BossTank = null;
        Floor = TroveGen.Generate(Seed, FloorNumber);
        Current = Floor.Start;
        TroveItems.OnNewFloor();
        _entry = null;
        _roomTime = 0;
        MarkSeen();
        RevealMap();
        ReviveSystem.Clear();
        // the campaign's own "next mission" sequence shows the floor banner and sets the new room up
        CampaignGlobals.LoadedCampaign.CachedMissions[FloorNumber - 1] = TroveGen.BuildMission(Floor, Room!, null);
        IntermissionHandler.PrepareIntermission(true);
    }

    private static void End(EndKind kind) {
        Ended = kind;
        TankMusicSystem.StopAll();
        SoundPlayer.PlaySoundInstance(kind == EndKind.Victory ? "Assets/music/fanfares/mission_complete_M100.ogg"
            : "Assets/music/fanfares/gameover_playerdeath.ogg", SoundContext.Effect, 0.5f);
        TroveHud.EndShown();
    }

    // ------------------------------------------------------------------ end screen choices

    /// <summary>Try again (host or solo): a new run.</summary>
    public static void TryAgain() {
        Stop();
        GameUI.QuitOut(netSent: true);
        StartFromMenu();
    }

    /// <summary>Back to the menus (host or solo: everyone).</summary>
    public static void ToMenu() {
        Send(EV_MENU);
        Stop();
        GameUI.QuitOut(netSent: true);
        if (!Client.IsConnected())
            MainMenuUI.EnterCampaigns();
    }

    /// <summary>A joined player leaves the game.</summary>
    public static void Leave() {
        Stop();
        if (Net.Online.OnlineSession.Active)
            Net.Online.OnlineSession.Leave();
        else
            LanSession.Leave();
        GameUI.QuitOut(netSent: true);
        MainMenuUI.MenuState = MainMenuUI.UIState.MPMode;
    }

    // ------------------------------------------------------------------ multiplayer

    private static void Send(int ev, int a = 0) {
        if (Client.IsConnected() && Client.IsHost())
            Client.SendTroveEvent(ev, a);
    }
    /// <summary>Events any phone can send (the server passes them to everyone else).</summary>
    private static void SendAny(int ev, int a = 0) {
        if (Client.IsConnected())
            Client.SendTroveEvent(ev, a);
    }

    /// <summary>Joined players: an event from the host.</summary>
    public static void OnEvent(int ev, int a) {
        switch (ev) {
            case EV_START:
                if (!MainMenuUI.IsActive) {
                    Stop();
                    GameUI.QuitOut(netSent: true);
                }
                Begin(a);
                break;
            case EV_MOVE:
                if (Active) Move((TroveDir)a);
                break;
            case EV_RETRY:
                if (Active) Retry();
                break;
            case EV_NEXT_FLOOR:
                if (Active) NextFloor();
                break;
            case EV_RUN_OVER:
                if (Active) End(EndKind.RunOver);
                break;
            case EV_VICTORY:
                if (Active) End(EndKind.Victory);
                break;
            case EV_ITEM:
                if (Active) TakePickup(a);
                break;
            case EV_BLOCK_BUST:
                if (Active && CampaignGlobals.InMission) TroveItems.BustBlocks(a);
                break;
            case EV_REROLL:
                if (Active) SetPickup((TroveItemId)a);
                break;
            case EV_SECRET:
                if (Active) RevealSecret();
                break;
            case EV_WAVE2:
                if (Active && CampaignGlobals.InMission && Room is { } wr) SpawnWave2(wr);
                break;
            case EV_STOPWATCH:
                if (Active) TroveItems.StartFreeze(a);
                break;
            case EV_NUKE:
                if (Active && CampaignGlobals.InMission) TroveItems.Nuke(a);
                break;
            case EV_DECOY:
                if (Active && CampaignGlobals.InMission) TroveItems.SpawnDecoy(a);
                break;
            case EV_INVINCIBLE:
                if (Active) TroveItems.StartInvincible(a);
                break;
            case EV_WALL:
                if (Active && CampaignGlobals.InMission) TroveItems.BuildWall(a);
                break;
            case EV_CONVERT:
                if (Active && CampaignGlobals.InMission) TroveItems.ConvertTank(a);
                break;
            case EV_SHRINK:
                if (Active && CampaignGlobals.InMission) TroveItems.Shrink();
                break;
            case EV_BLAST:
                if (Active && CampaignGlobals.InMission) TroveItems.RemoteBlast(a);
                break;
            case EV_MENU:
                if (Active) {
                    Stop();
                    GameUI.QuitOut(netSent: true);
                }
                break;
        }
    }
}
