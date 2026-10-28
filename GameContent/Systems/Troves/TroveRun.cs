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
    public const int EV_START = 1, EV_MOVE = 2, EV_RETRY = 3, EV_NEXT_FLOOR = 4, EV_RUN_OVER = 5, EV_VICTORY = 6, EV_MENU = 7, EV_ITEM = 8;
    /// <summary>How close (game units) a tank has to get to an item to pick it up.</summary>
    public const float PICKUP_REACH = 26f;
    public static Vector2 PickupPoint => new Coordinates.BlockMapPosition(10, 8);

    // the boss of the current room (for the health bar at the top)
    public static AI.AITank? BossTank { get; private set; }
    public static int BossMaxHits { get; private set; }
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
        int seed = Random.Shared.Next(1, int.MaxValue);
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
        BossTank = null;
        if (!_hooked) {
            _hooked = true;
            // a floor's first room is set up by the campaign's own sequence: give the players their items then
            CampaignGlobals.OnMissionStart += () => { if (Active) ApplyItems(); };
        }
        ReviveSystem.Clear();
        PlayerTank.StartingLives = Difficulties.Types["GearsOfWar"] ? 1 : STARTING_LIVES;
        IntermissionSystem.BannerColor = new Color(92, 60, 140);
        IntermissionSystem.BackgroundColor = new Color(222, 210, 182);

        MainMenuUI.LaunchCustomGame();
        // rooms and floors are handled here, not by the campaign's "mission complete" logic
        CampaignGlobals.ShouldMissionsProgress = false;
    }

    public static void Stop() {
        Active = false;
        _starting = false;
        Ended = EndKind.None;
        Fade = 0;
        _fadeDir = 0;
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
                $"fade={Fade:0.00}/{_fadeDir} paused={GameUI.Paused} ended={Ended} dt={Dt:0.000} me={me?.Position} nearestDoor={nearest:0}", Internals.LogType.Info);
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
        bool enemies = GameHandler.AllAITanks.Any(t => t is not null && !t.IsDestroyed);
        if (!room.Cleared && !enemies && _roomTime > 0.6f && _fadeDir == 0) {
            room.Cleared = true;
            RoomsCleared++;
            TanksDestroyed += room.Enemies.Count;
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", SoundContext.Effect, 0.8f);
            if (room.Kind == TroveRoomKind.Boss)
                _bossTimer = 2.5f;
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
        if (room.PickupVisible) {
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
        var mission = TroveGen.BuildMission(Floor, room, entry);
        CampaignGlobals.LoadedCampaign.LoadMission(mission);
        CampaignGlobals.LoadedCampaign.SetupLoadedMission(true);
        ApplyItems();
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
        var boss = GameHandler.AllAITanks.Where(t => t is not null && !t.IsDestroyed)
            .OrderBy(t => Vector2.Distance(t!.Position, pos)).FirstOrDefault();
        if (boss is null)
            return;
        boss.Scaling = new Vector3(1.45f);
        boss.Properties.Armor = new TankArmor(boss, 4 + FloorNumber * 2);
        BossTank = boss;
        BossMaxHits = 4 + FloorNumber * 2 + 1;
        boss.Properties.ShellLimit += 2;
        boss.Properties.MineLimit += 1;
    }

    private static void NextFloor() {
        FloorNumber++;
        TroveItems.NewFloor();
        BossTank = null;
        Floor = TroveGen.Generate(Seed, FloorNumber);
        Current = Floor.Start;
        _entry = null;
        _roomTime = 0;
        MarkSeen();
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
            case EV_MENU:
                if (Active) {
                    Stop();
                    GameUI.QuitOut(netSent: true);
                }
                break;
        }
    }
}
