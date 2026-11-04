using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.Globals;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.AI;
using TanksRebirth.GameContent.Systems.Coordinates;
using TanksRebirth.GameContent.Systems.TankSystem;
using TanksRebirth.Internals.Common.Framework.Audio;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems.Troves;

public enum BattleMatch { Deathmatch, CaptureTheFlag, KingOfTheHill }

/// <summary>
/// Battle: up to 4 tanks against each other over several rounds, each round in a new arena with its own look
/// (forest, ice, desert, volcano, haunted, all cork...). Everyone starts in a corner. Items lie on the map or hide in
/// cork, and what you pick up carries over to the next rounds. Win a round by being the last tank standing, or by the
/// match type's goal: bring the flag home (Capture the Flag) or hold the hill (King of the Hill).
/// Same-device co-op is 2 players; Wi-Fi and online games take up to 4. With nobody else to play against, CPU tanks join.
/// Built on Tank Troves (its rooms, themes, items and multiplayer events); the host decides when rounds end.
/// </summary>
public static class BattleMode {
    // ---- the options chosen before starting
    public static int Rounds = 5;
    public static int HitPoints = 1;
    public static bool Items = true;
    public static BattleMatch Match = BattleMatch.Deathmatch;
    /// <summary>CPU tanks (offline only).</summary>
    public static int Cpus = 1;

    public const float HILL_RADIUS = 29f, HILL_SECONDS = 12f;
    /// <summary>King of the Hill: the hill teleports this often (seconds), or drifts at this speed (units per second).</summary>
    public const float HILL_JUMP_EVERY = 16f, HILL_DRIFT_SPEED = 14f, HILL_WARN = 3f;
    public static Vector2 HillAt { get; private set; }
    /// <summary>Where a teleporting hill goes next (shown a few seconds before it jumps), or null.</summary>
    public static Vector2? HillNext { get; private set; }
    public static bool HillDrifts { get; private set; }
    private static readonly List<Vector2> _hillSpots = [];
    private static double _roundGo;
    private static int _hillJump;
    public const float FLAG_REACH = 24f, BASE_REACH = 34f;
    public const double STANDINGS_SECONDS = 4.5;

    public static readonly string[] MatchNames = ["Deathmatch", "Capture the Flag", "King of the Hill"];
    public static string MatchName => MatchNames[(int)Match];

    public sealed class Fighter {
        public string Name = "";
        public Color Color;
        /// <summary>The player (0-3), or -1 for a CPU tank.</summary>
        public int PlayerId = -1;
        public int CpuTier;
        public int Corner;
        public Tank? Cpu;
        public int Wins, Kills;
        public Vector2 Base;
        public float Hill;
        public bool IsCpu => PlayerId < 0;
        public Tank? Tank => IsCpu ? Cpu : PlayerId is >= 0 and < 4 ? GameHandler.AllPlayerTanks[PlayerId] : null;
        public bool Alive => Tank is { IsDestroyed: false };
    }

    public static readonly List<Fighter> Fighters = [];
    public static int Round { get; internal set; } = 1;
    /// <summary>The round is over: the standings show until then (play time); -1 while playing.</summary>
    public static double StandingsUntil = -1;
    public static bool RoundOver => StandingsUntil >= 0;
    public static double RoundEndedAt;
    /// <summary>Who won the last round (fighter index, -1 = nobody).</summary>
    public static int LastWinner = -1;
    // capture the flag: one flag in the middle; take it home to your corner
    public static int FlagCarrier = -1;
    public static Vector2 FlagAt;
    private static float _lastStanding;

    // the corners, in the order fighters get them: 2 players opposite, then the other two
    private static readonly Point[] CornerCells = [new(2, 2), new(19, 14), new(19, 2), new(2, 14)];
    private static readonly Color[] HumanColors = [new(60, 120, 255), new(230, 60, 60), new(60, 200, 80), new(240, 200, 40)];
    private static readonly (int Tier, string Name, Color Color)[] CpuKinds = [
        (TankID.Violet, "CPU Violet", new Color(160, 90, 210)), (TankID.Marine, "CPU Marine", new Color(40, 170, 170)), (TankID.Ash, "CPU Ash", new Color(150, 150, 150)),
    ];

    // ------------------------------------------------------------------ starting

    /// <summary>The settings and seed in one number, for the multiplayer start event.</summary>
    public static int Pack(int seed) => (seed & 0x7FFFF) << 12 | Math.Clamp(Rounds, 1, 15) | (Items ? 1 : 0) << 4 | (Math.Clamp(HitPoints, 1, 5) - 1) << 5 | (int)Match << 8 | Math.Clamp(Cpus, 0, 3) << 10;
    public static int SeedFrom(int packed) => Math.Max(1, (packed >> 12) & 0x7FFFF);
    public static void ReadConfig(int packed) {
        Rounds = Math.Clamp(packed & 15, 1, 15);
        Items = (packed >> 4 & 1) == 1;
        HitPoints = (packed >> 5 & 7) + 1;
        Match = (BattleMatch)Math.Clamp(packed >> 8 & 3, 0, 2);
        Cpus = packed >> 10 & 3;
    }

    public static int HumanCount => Client.IsConnected() ? Math.Clamp(Server.CurrentClientCount, 1, 4) : LocalCoop.Active ? LocalCoop.PlayerCount : 1;
    /// <summary>How many CPU tanks join (offline only, and always at least one opponent).</summary>
    public static int CpuCount => Client.IsConnected() ? 0 : Math.Clamp(Math.Max(Cpus, HumanCount < 2 ? 1 : 0), 0, 4 - HumanCount);

    /// <summary>The Start button on the Battle screen (online, only the host can start).</summary>
    public static void StartFromMenu() {
        if (Client.IsConnected() && !Client.IsHost()) {
            ChatSystem.SendMessage("Only the host can start a battle.", Color.Orange);
            SoundPlayer.SoundError();
            return;
        }
        int seed = Random.Shared.Next(1, 0x7FFFF);
        int packed = Pack(seed);
        if (Client.IsConnected())
            Client.SendTroveEvent(TroveRun.EV_BATTLE_START, packed);
        ReadConfig(packed);
        TroveRun.BeginBattle(SeedFrom(packed));
    }

    /// <summary>Who's playing (the same on every phone).</summary>
    public static void Setup(int seed) {
        Fighters.Clear();
        Round = 1;
        StandingsUntil = -1;
        LastWinner = -1;
        int humans = HumanCount, cpus = CpuCount;
        for (int i = 0; i < humans; i++) {
            string name = Client.IsConnected() && Server.ConnectedClients is { } cc && i < cc.Length && cc[i] is { } c && !string.IsNullOrWhiteSpace(c.Name)
                ? c.Name : $"Player {i + 1}";
            int ci = PlayerColors.ColorIndexFor(i);
            var col = ci >= 0 && ci < PlayerColors.Palette.Length ? PlayerColors.Palette[ci].Main : HumanColors[i];
            Fighters.Add(new Fighter { Name = name, Color = col, PlayerId = i });
        }
        for (int i = 0; i < cpus; i++) {
            var k = CpuKinds[i % CpuKinds.Length];
            Fighters.Add(new Fighter { Name = k.Name, Color = k.Color, CpuTier = k.Tier });
        }
        for (int i = 0; i < Fighters.Count; i++)
            Fighters[i].Corner = i;
    }

    public static Color BannerColor(TroveFloor f) => f.CorkFloor ? new Color(150, 105, 60) : TroveThemes.Def(f.Theme)?.Banner ?? new Color(150, 60, 50);

    // ------------------------------------------------------------------ the arenas

    /// <summary>This round's look: every theme (and an all-cork arena) once before any comes back, in an order from the seed.</summary>
    private static (FloorTheme Theme, bool Cork) LookFor(int seed, int round) {
        var looks = new List<(FloorTheme, bool)> {
            (FloorTheme.Normal, false), (FloorTheme.Forest, false), (FloorTheme.Desert, false), (FloorTheme.IceCave, false),
            (FloorTheme.Volcano, false), (FloorTheme.Haunted, false), (FloorTheme.Normal, true),
            (FloorTheme.Factory, false), (FloorTheme.Swamp, false), (FloorTheme.Crystal, false), (FloorTheme.Blizzard, false),
        };
        var rng = new Random(unchecked(seed * 613 + 17));
        for (int i = looks.Count - 1; i > 0; i--) {
            int j = rng.Next(i + 1);
            (looks[i], looks[j]) = (looks[j], looks[i]);
        }
        return looks[(round - 1) % looks.Count];
    }

    private static bool InCornerZone(int x, int y) => Math.Min(x, TroveGen.GW - 1 - x) <= 3 && Math.Min(y, TroveGen.GH - 1 - y) <= 3;
    private static bool InMiddle(int x, int y) => x is >= 8 and <= 13 && y is >= 6 and <= 10;

    /// <summary>A mirrored arena (fair from every corner) with the corners and the middle kept open.</summary>
    public static TroveFloor BuildArena(int seed, int round) {
        int GW = TroveGen.GW, GH = TroveGen.GH;
        var rng = new Random(unchecked(seed * 131 + round * 7919));
        var look = LookFor(seed, round);
        var f = new TroveFloor { Number = 1, Theme = look.Theme, CorkFloor = look.Cork };
        f.Name = look.Cork ? "The Cork Works" : TroveThemes.Def(look.Theme)?.Name ?? "The Arena";
        var room = new TroveRoom { Cell = Point.Zero, Kind = TroveRoomKind.Normal, Visited = true, Seen = true, CorkStyle = look.Cork };
        f.Rooms[Point.Zero] = room;
        f.Start = f.Boss = Point.Zero;

        var solid = new int[GW, GH];
        var type = new int[GW, GH];
        void Set(int x, int y, int h, int t) {
            if (x < 0 || y < 0 || x >= GW || y >= GH) return;
            solid[x, y] = h;
            type[x, y] = t;
        }
        void Mirror(int x, int y, int h, int t) {
            Set(x, y, h, t);
            Set(GW - 1 - x, y, h, t);
            Set(x, GH - 1 - y, h, t);
            Set(GW - 1 - x, GH - 1 - y, h, t);
        }

        switch (rng.Next(5)) {
            case 1: // pillars
                foreach (var (px, py) in new[] { (5, 4), (6, 4), (5, 5), (6, 5) })
                    Mirror(px, py, 2, BlockID.Wood);
                break;
            case 2: // walls around the middle, with gaps
                for (int x = 7; x <= 9; x++)
                    Mirror(x, 4, 2, BlockID.Wood);
                for (int y = 6; y <= 7; y++)
                    Mirror(6, y, 2, BlockID.Wood);
                break;
            case 3: // a bunker in front of each corner
                for (int i = 0; i < 3; i++) {
                    Mirror(5, 1 + i, 2, BlockID.Wood);
                    Mirror(1 + i, 5, 2, BlockID.Wood);
                }
                break;
            case 4: // long lanes
                for (int x = 5; x <= 8; x++)
                    Mirror(x, 3, 1 + rng.Next(2), rng.NextDouble() < 0.5 ? BlockID.Cork : BlockID.Wood);
                break;
        }
        // cover: a few mirrored pieces, some breakable
        int pieces = rng.Next(3, 6);
        for (int i = 0; i < pieces; i++) {
            int x = rng.Next(1, 10), y = rng.Next(1, 8);
            int h = rng.Next(1, 3);
            int t = rng.NextDouble() < 0.45 ? BlockID.Cork : BlockID.Wood;
            Mirror(x, y, h, t);
            if (rng.Next(2) == 0) Mirror(x + 1, y, h, t);
            else Mirror(x, y + 1, h, t);
        }
        // now and then a pair of holes
        if (rng.NextDouble() < 0.3) {
            int x = rng.Next(5, 9), y = rng.Next(2, 6);
            Mirror(x, y, 1, BlockID.Hole);
        }
        TroveGen.Corkify(room, solid, type);

        // the corners (spawns) and the middle (hill / flag) stay open
        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++)
                if (InCornerZone(x, y) || InMiddle(x, y))
                    solid[x, y] = 0;
        // corners nobody starts in may get a block
        for (int c = Fighters.Count; c < 4; c++) {
            if (rng.NextDouble() > 0.75)
                continue;
            var cc = CornerCells[c];
            int dx = cc.X < GW / 2 ? 1 : -1, dy = cc.Y < GH / 2 ? 1 : -1;
            int bt = look.Cork || rng.Next(3) == 0 ? BlockID.Cork : BlockID.Wood;
            foreach (var (ox, oy) in new[] { (0, 0), (dx, 0), (0, dy), (dx, dy) })
                Set(cc.X + ox, cc.Y + oy, 2, bt);
        }
        // some arenas get a pair of teleporters (not near anyone's corner)
        if (rng.NextDouble() < 0.3) {
            var pads = TroveGen.PlaceTeleporters(solid, [], rng);
            if (pads.All(p => !InCornerZone(p.X, p.Y) && CornerCells.All(cc => Math.Abs(cc.X - p.X) + Math.Abs(cc.Y - p.Y) > 4)))
                room.Teleporters = pads;
        }
        var stuck = TroveGen.ConnectRegions(solid, type, room.Teleporters);

        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++)
                if (solid[x, y] > 0)
                    room.Blocks.Add((x, y, type[x, y], type[x, y] == BlockID.Hole ? 1 : solid[x, y]));

        if (Items) {
            var pool = TroveItems.BattlePool;
            // on the map: mirrored pairs (the same item on opposite sides, so nobody's closer)
            var free = new List<Point>();
            for (int x = 1; x < GW - 1; x++)
                for (int y = 1; y < GH - 1; y++)
                    if (solid[x, y] == 0 && solid[GW - 1 - x, GH - 1 - y] == 0 && !InCornerZone(x, y) && !InMiddle(x, y)
                        && !stuck.Contains(new Point(x, y)) && !stuck.Contains(new Point(GW - 1 - x, GH - 1 - y))
                        && !room.Teleporters.Any(t => Math.Abs(t.X - x) + Math.Abs(t.Y - y) <= 1 || Math.Abs(t.X - (GW - 1 - x)) + Math.Abs(t.Y - (GH - 1 - y)) <= 1)
                        && x + y * GW < (GW - 1 - x) + (GH - 1 - y) * GW)
                        free.Add(new Point(x, y));
            int pairs = 1 + rng.Next(2);
            var used = new List<Point>();
            for (int i = 0; i < pairs && free.Count > 0; i++) {
                var opts = free.Where(p => used.All(u => Math.Abs(u.X - p.X) + Math.Abs(u.Y - p.Y) >= 4)).ToList();
                if (opts.Count == 0) break;
                var p = opts[rng.Next(opts.Count)];
                used.Add(p);
                var item = pool[rng.Next(pool.Length)];
                room.GroundDrops.Add(new TroveGroundDrop { At = new BlockMapPosition(p.X, p.Y), Item = item });
                room.GroundDrops.Add(new TroveGroundDrop { At = new BlockMapPosition(GW - 1 - p.X, GH - 1 - p.Y), Item = item });
            }
            // hidden in cork: shoot it open to find out
            var cork = room.Blocks.Where(b => b.Type == BlockID.Cork).Select(b => new Point(b.X, b.Y)).OrderBy(_ => rng.Next()).ToList();
            int hide = Math.Min(cork.Count, look.Cork ? 6 : 3);
            for (int i = 0; i < hide; i++)
                room.Hidden[cork[i]] = pool[rng.Next(pool.Length)];
        }
        return f;
    }

    public static Mission BuildMission(TroveFloor f, TroveRoom room) {
        var tanks = new List<TankTemplate>();
        // players first, in order (the campaign loader counts them)
        foreach (var fi in Fighters.Where(x => !x.IsCpu).OrderBy(x => x.PlayerId)) {
            var cc = CornerCells[fi.Corner];
            tanks.Add(new TankTemplate {
                IsPlayer = true, PlayerType = fi.PlayerId, Position = new BlockMapPosition(cc.X, cc.Y), Rotation = 0f, Team = TeamID.NoTeam,
            });
        }
        foreach (var fi in Fighters.Where(x => x.IsCpu)) {
            var cc = CornerCells[fi.Corner];
            tanks.Add(new TankTemplate {
                IsPlayer = false, AiTier = fi.CpuTier, Position = new BlockMapPosition(cc.X, cc.Y), Rotation = 0f, Team = TeamID.NoTeam,
            });
        }
        var blocks = room.Blocks.Select(b => new BlockTemplate {
            Type = b.Type, Stack = (byte)b.Height, Position = new BlockMapPosition(b.X, b.Y), TpLink = -1,
        }).Concat(room.Teleporters.Select(t => new BlockTemplate {
            Type = BlockID.Teleporter, Stack = 1, Position = new BlockMapPosition(t.X, t.Y), TpLink = (sbyte)t.Link,
        })).ToArray();
        return new Mission(tanks.ToArray(), blocks) {
            Name = $"Round {Round}: {f.Name}",
            Note = string.Empty,
        };
    }

    /// <summary>A round's tanks just appeared: everyone for themselves, hit points, bases.</summary>
    public static void OnRoundLoaded() {
        StandingsUntil = -1;
        _lastStanding = 0;
        FlagCarrier = -1;
        FlagAt = Vector2.Zero;
        foreach (var t in GameHandler.AllTanks)
            if (t is not null)
                t.Team = TeamID.NoTeam;
        var taken = new HashSet<Tank>();
        foreach (var fi in Fighters) {
            fi.Hill = 0;
            var corner = (Vector2)new BlockMapPosition(CornerCells[fi.Corner].X, CornerCells[fi.Corner].Y);
            if (fi.IsCpu) {
                // (by its kind: Mission Flip can move the corners around)
                fi.Cpu = GameHandler.AllAITanks.Where(t => t is { IsDestroyed: false } && !taken.Contains(t))
                    .OrderBy(t => t!.AiTankType == fi.CpuTier ? 0 : 1).ThenBy(t => Vector2.Distance(t!.Position, corner)).FirstOrDefault();
                if (fi.Cpu is { } cpu) {
                    taken.Add(cpu);
                    if (HitPoints > 1)
                        cpu.Properties.Armor = new TankArmor(cpu, HitPoints - 1);
                }
            }
            else
                TroveItems.Players[fi.PlayerId].BattleHp = HitPoints - 1;
            fi.Base = fi.Tank?.Position ?? corner;
        }
        for (int i = 0; i < PlayerTank.Lives.Length; i++)
            PlayerTank.Lives[i] = 99;
        TroveRun.ReadyUntil = TroveRun.PlayTime + TroveRun.READY_SECONDS;
        _roundGo = TroveRun.ReadyUntil;
        PlanHill();
    }

    /// <summary>The hill's stops this round (the same on every phone): it starts in the middle, then moves between open spots,
    /// either jumping every few seconds or drifting slowly from one to the next.</summary>
    private static void PlanHill() {
        _hillSpots.Clear();
        _hillJump = 0;
        HillNext = null;
        var rng = new Random(unchecked(TroveRun.Seed * 389 + Round * 7741));
        HillDrifts = rng.Next(2) == 0;
        var blocked = new HashSet<Point>();
        if (TroveRun.Room is { } room)
            foreach (var b in room.Blocks)
                blocked.Add(new Point(b.X, b.Y));
        var open = new List<Point>();
        for (int x = 3; x <= TroveGen.GW - 4; x++)
            for (int y = 2; y <= TroveGen.GH - 3; y++)
                if (!InCornerZone(x, y) && !blocked.Contains(new Point(x, y)) && !blocked.Contains(new Point(x + 1, y)) && !blocked.Contains(new Point(x, y + 1)))
                    open.Add(new Point(x, y));
        _hillSpots.Add(Vector2.Zero);
        var last = new Point(10, 8);
        for (int i = 0; i < 6 && open.Count > 0; i++) {
            var far = open.Where(p => Math.Abs(p.X - last.X) + Math.Abs(p.Y - last.Y) >= 6).ToList();
            var pick = (far.Count > 0 ? far : open)[rng.Next(far.Count > 0 ? far.Count : open.Count)];
            _hillSpots.Add(new BlockMapPosition(pick.X, pick.Y));
            last = pick;
        }
        HillAt = Vector2.Zero;
    }

    /// <summary>Where the hill is now (worked out from the round's clock, so every phone agrees).</summary>
    private static void UpdateHill() {
        if (Match != BattleMatch.KingOfTheHill || _hillSpots.Count < 2)
            return;
        float t = (float)Math.Max(0, TroveRun.PlayTime - _roundGo);
        if (HillDrifts) {
            // slowly along the loop of stops (a short rest at the start)
            float d = Math.Max(0, t - 4f) * HILL_DRIFT_SPEED;
            float total = 0;
            for (int i = 0; i < _hillSpots.Count; i++)
                total += Vector2.Distance(_hillSpots[i], _hillSpots[(i + 1) % _hillSpots.Count]);
            d %= Math.Max(1f, total);
            for (int i = 0; i < _hillSpots.Count; i++) {
                var a = _hillSpots[i];
                var b = _hillSpots[(i + 1) % _hillSpots.Count];
                float len = Vector2.Distance(a, b);
                if (d <= len) {
                    HillAt = Vector2.Lerp(a, b, len < 0.01f ? 0 : d / len);
                    break;
                }
                d -= len;
            }
            HillNext = null;
            return;
        }
        int jump = (int)(t / HILL_JUMP_EVERY);
        if (jump != _hillJump) {
            _hillJump = jump;
            SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", SoundContext.Effect, 0.6f, pitchOverride: 0.6f);
            GameHandler.Particles.MakeShineSpot(new Vector3(HillAt.X, 10, HillAt.Y), Color.White, 1.2f);
        }
        HillAt = _hillSpots[jump % _hillSpots.Count];
        HillNext = t % HILL_JUMP_EVERY > HILL_JUMP_EVERY - HILL_WARN ? _hillSpots[(jump + 1) % _hillSpots.Count] : null;
    }

    // ------------------------------------------------------------------ every update (in a round)

    private static int IndexOf(Tank t) => Fighters.FindIndex(f => ReferenceEquals(f.Tank, t));

    public static void Update(float dt, bool decider) {
        if (Fighters.Count == 0)
            return;
        if (RoundOver) {
            if (decider && TroveRun.PlayTime >= StandingsUntil) {
                StandingsUntil = double.MaxValue; // (only once)
                if (Round >= Rounds)
                    TroveRun.BattleOver();
                else {
                    TroveRun.BattleSend(TroveRun.EV_ROUND_NEXT);
                    TroveRun.NextBattleRound();
                }
            }
            return;
        }
        if (TroveRun.InReady)
            return;
        UpdateHill();

        var alive = Enumerable.Range(0, Fighters.Count).Where(i => Fighters[i].Alive).ToList();
        int winner = -2; // -2 = still going

        if (Match == BattleMatch.KingOfTheHill) {
            // only a tank alone on the hill counts
            var on = alive.Where(i => Vector2.Distance(Fighters[i].Tank!.Position, HillAt) < HILL_RADIUS).ToList();
            if (on.Count == 1) {
                var f = Fighters[on[0]];
                int before = (int)f.Hill;
                f.Hill += dt;
                if ((int)f.Hill != before && f.Hill < HILL_SECONDS)
                    SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.4f, pitchOverride: Math.Min(1f, f.Hill / HILL_SECONDS));
                if (f.Hill >= HILL_SECONDS)
                    winner = on[0];
            }
        }
        else if (Match == BattleMatch.CaptureTheFlag) {
            if (FlagCarrier >= 0) {
                var carrier = Fighters[FlagCarrier];
                if (carrier.Alive) {
                    FlagAt = carrier.Tank!.Position;
                    if (decider && Vector2.Distance(FlagAt, carrier.Base) < BASE_REACH)
                        winner = FlagCarrier;
                }
                else if (decider) {
                    TroveRun.BattleSend(TroveRun.EV_BFLAG_DROP, TroveItems.EncodePos(FlagAt));
                    DropFlag(FlagAt);
                }
            }
            else if (decider)
                foreach (var i in alive)
                    if (Vector2.Distance(Fighters[i].Tank!.Position, FlagAt) < FLAG_REACH) {
                        TroveRun.BattleSend(TroveRun.EV_BFLAG_TAKE, i);
                        TakeFlag(i);
                        break;
                    }
        }

        // the last tank standing wins (any match type)
        if (winner == -2 && alive.Count <= 1 && Fighters.Count >= 2) {
            _lastStanding += dt;
            if (_lastStanding > 1.2f)
                winner = alive.Count == 1 ? alive[0] : -1;
        }
        else
            _lastStanding = 0;

        if (winner != -2 && decider) {
            TroveRun.BattleSend(TroveRun.EV_ROUND_END, winner + 1);
            EndRound(winner);
        }
    }

    public static void TakeFlag(int fighter) {
        if (fighter < 0 || fighter >= Fighters.Count)
            return;
        FlagCarrier = fighter;
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/client_join.ogg", SoundContext.Effect, 0.7f, pitchOverride: 0.4f);
        Internals.Common.Framework.Input.Haptics.ItemPickup();
        ChatSystem.SendMessage($"{Fighters[fighter].Name} has the flag!", Fighters[fighter].Color);
    }

    public static void DropFlag(Vector2 at) {
        FlagCarrier = -1;
        FlagAt = at;
        SoundPlayer.PlaySoundInstance("Assets/sounds/menu/menu_tick.ogg", SoundContext.Effect, 0.8f, pitchOverride: -0.4f);
    }

    /// <summary>The round is over (every phone): a point for the winner, then the standings.</summary>
    public static void EndRound(int winner) {
        if (RoundOver)
            return;
        LastWinner = winner >= 0 && winner < Fighters.Count ? winner : -1;
        if (LastWinner >= 0)
            Fighters[LastWinner].Wins++;
        RoundEndedAt = TroveRun.PlayTime;
        StandingsUntil = TroveRun.PlayTime + STANDINGS_SECONDS;
        FlagCarrier = -1;
        TankGame.ClientLog.Write($"Battle: round {Round}/{Rounds} over, winner={(LastWinner >= 0 ? Fighters[LastWinner].Name : "none")} | "
            + string.Join(", ", Fighters.Select(f => $"{f.Name}={f.Wins}")), Internals.LogType.Info);
        SoundPlayer.PlaySoundInstance(LastWinner >= 0 ? "Assets/music/fanfares/mission_complete.ogg" : "Assets/music/fanfares/gameover_playerdeath.ogg",
            SoundContext.Effect, 0.45f);
    }

    /// <summary>Fighters from first to last place.</summary>
    public static List<Fighter> Standings() => Fighters.OrderByDescending(f => f.Wins).ThenBy(f => f.Corner).ToList();

    /// <summary>"Alex is in first!", "Alex and Sam are tied for first!".</summary>
    public static string LeaderLine(bool final) {
        if (Fighters.Count == 0)
            return "";
        int top = Fighters.Max(f => f.Wins);
        var leaders = Fighters.Where(f => f.Wins == top).ToList();
        if (top == 0)
            return final ? "Nobody won a round!" : "Nobody has a point yet";
        if (leaders.Count == 1)
            return final ? $"{leaders[0].Name} wins the battle!" : $"{leaders[0].Name} is in first!";
        string names = string.Join(" & ", leaders.Select(l => l.Name));
        return final ? $"It's a tie: {names}!" : $"{names} are tied for first!";
    }
}
