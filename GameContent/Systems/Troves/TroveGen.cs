using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.Coordinates;

namespace TanksRebirth.GameContent.Systems.Troves;

public enum TroveRoomKind { Start, Normal, Boss, Item, Challenge, Secret }
public enum TroveShape { Open, Pillars, Wide, Tall, Small, Cross, Corner }

/// <summary>Directions between rooms. North is the board edge at the lowest Z (grid row 0).</summary>
public enum TroveDir { North, East, South, West }

public sealed class TroveRoom {
    public Point Cell;
    public TroveRoomKind Kind;
    public TroveShape Shape;
    public bool Cleared, Visited, Seen;
    /// <summary>Solid blocks (grid x, y) and their block type / height.</summary>
    public List<(int X, int Y, int Type, int Height)> Blocks = [];
    /// <summary>Teleporter pads (campaign rooms): grid cell and link number (two pads share one).</summary>
    public List<(int X, int Y, int Link)> Teleporters = [];
    public List<(int X, int Y, int Tier, float Rotation)> Enemies = [];
    /// <summary>Index into <see cref="Enemies"/> of the boss, or -1.</summary>
    public int BossIndex = -1;
    /// <summary>Challenge rooms: the second wave (comes in once the first is beaten).</summary>
    public List<(int X, int Y, int Tier, float Rotation)> Wave2 = [];
    public bool Wave2Spawned;
    /// <summary>An item in the middle of the room: on a pedestal in item rooms, or left behind once a fight room is cleared.</summary>
    public TroveItemId? Pickup;
    public bool PickupTaken;
    /// <summary>The campaign mission this room's layout came from, or null for a made-up room.</summary>
    public string? CampaignMap;
    /// <summary>Whether the pickup is showing (item rooms always; other rooms after they're cleared).</summary>
    public bool PickupVisible => Pickup is not null && !PickupTaken && (Kind is TroveRoomKind.Item or TroveRoomKind.Secret || Cleared);
}

public sealed class TroveFloor {
    public int Number;
    public string Name = "";
    public Dictionary<Point, TroveRoom> Rooms = [];
    public Point Start, Boss;
    public Point? ItemRoom;
    /// <summary>A hidden room: its doorway is walled up with cork until an explosion goes off near it.</summary>
    public Point? Secret;
    public bool SecretFound;

    public bool Has(Point p) => Rooms.ContainsKey(p);
}

/// <summary>
/// Builds Tank Troves floors: an Isaac-style map of rooms (a start room, fights, a boss at the far end), each
/// room shaped with walls of blocks inside the normal arena, and enemies that get tougher every floor.
/// Everything comes from one seed, so every phone in a multiplayer game builds the same dungeon.
/// </summary>
public static class TroveGen {
    public const int FLOORS = 5;
    public const int GW = BlockMapPosition.MAP_WIDTH_169; // 22
    public const int GH = BlockMapPosition.MAP_HEIGHT;     // 17

    public static readonly string[] FloorNames = ["The Cellar", "The Pantry", "The Workshop", "The Armory", "The Vault"];

    // enemy pools per floor (repeats = more common)
    private static readonly int[][] Pools = [
        [TankID.Brown, TankID.Brown, TankID.Brown, TankID.Ash, TankID.Ash, TankID.Ash, TankID.Marine],
        [TankID.Ash, TankID.Marine, TankID.Yellow, TankID.Pink, TankID.Pink],
        [TankID.Marine, TankID.Pink, TankID.Yellow, TankID.Violet, TankID.Green],
        [TankID.Violet, TankID.Green, TankID.White, TankID.Black, TankID.Pink],
        [TankID.Black, TankID.White, TankID.Green, TankID.Sapphire, TankID.Ruby, TankID.Citrine, TankID.Amethyst, TankID.Emerald],
    ];
    /// <summary>The toughest tank a floor's rooms can have (campaign rooms swap anything tougher for one from the floor's pool).</summary>
    private static readonly int[] MaxTier = [TankID.Marine, TankID.Yellow, TankID.Green, TankID.Black, TankID.Obsidian];
    private static readonly int[] Bosses = [TankID.Marine, TankID.Violet, TankID.Green, TankID.Black, TankID.Obsidian];

    // ------------------------------------------------------------------ campaign rooms

    /// <summary>Some fight rooms use a map from the Vanilla campaign (deeper floors use later missions).
    /// The seed's lowest bit says whether this run does that, so every phone in a game builds the same rooms.</summary>
    public const double CAMPAIGN_ROOM_CHANCE = 0.35;
    public static bool SeedUsesCampaign(int seed) => (seed & 1) == 1;
    private static Mission[]? _campaignMaps;
    private static bool _triedCampaign;
    public static Mission[]? CampaignMaps {
        get {
            if (!_triedCampaign) {
                _triedCampaign = true;
                try {
                    // the app's own fixed copy (the same on every phone), else the installed campaign
                    var path = System.IO.Path.Combine(TankGame.SaveDirectory, "Troves", "Vanilla.campaign");
                    if (!System.IO.File.Exists(path))
                        path = System.IO.Path.Combine(TankGame.SaveDirectory, "Campaigns", "Vanilla.campaign");
                    if (System.IO.File.Exists(path))
                        _campaignMaps = Campaign.Load(path).CachedMissions;
                }
                catch (Exception e) {
                    TankGame.ClientLog.Write($"Troves: couldn't read the campaign for rooms: {e.Message}", Internals.LogType.Warn);
                }
            }
            return _campaignMaps;
        }
    }
    public static bool CampaignAvailable => CampaignMaps is { Length: >= FLOORS };
    /// <summary>A new run's seed (its lowest bit: campaign rooms on, if this phone has the campaign).</summary>
    public static int NewSeed() => (Random.Shared.Next(1, int.MaxValue) & ~1) | (CampaignAvailable ? 1 : 0);

    private static Point ToGrid(Vector2 world) {
        Vector2 origin = new BlockMapPosition(0, 0);
        return new Point((int)MathF.Round((world.X - origin.X) / Block.SIDE_LENGTH), (int)MathF.Round((world.Y - origin.Y) / Block.SIDE_LENGTH));
    }

    public static Point Step(TroveDir d) => d switch {
        TroveDir.North => new(0, -1),
        TroveDir.East => new(1, 0),
        TroveDir.South => new(0, 1),
        _ => new(-1, 0),
    };
    public static TroveDir Opposite(TroveDir d) => (TroveDir)(((int)d + 2) % 4);

    /// <summary>The doors you can see and use (a secret room's doorway only once it's been found).</summary>
    public static IEnumerable<TroveDir> Doors(TroveFloor f, TroveRoom r) {
        foreach (TroveDir d in Enum.GetValues<TroveDir>()) {
            var s = Step(d);
            var n = new Point(r.Cell.X + s.X, r.Cell.Y + s.Y);
            if (f.Has(n) && (n != f.Secret || f.SecretFound || r.Kind == TroveRoomKind.Secret))
                yield return d;
        }
    }
    /// <summary>Every doorway, hidden ones too (for building rooms).</summary>
    public static IEnumerable<TroveDir> DoorsAll(TroveFloor f, TroveRoom r) {
        foreach (TroveDir d in Enum.GetValues<TroveDir>()) {
            var s = Step(d);
            if (f.Has(new Point(r.Cell.X + s.X, r.Cell.Y + s.Y)))
                yield return d;
        }
    }
    /// <summary>The direction from this room to the floor's secret room, if they're next to each other.</summary>
    public static TroveDir? SecretDoor(TroveFloor f, TroveRoom r) {
        if (f.Secret is not { } sc || r.Kind == TroveRoomKind.Secret)
            return null;
        foreach (TroveDir d in Enum.GetValues<TroveDir>()) {
            var s = Step(d);
            if (r.Cell.X + s.X == sc.X && r.Cell.Y + s.Y == sc.Y)
                return d;
        }
        return null;
    }

    // ------------------------------------------------------------------ the floor map

    public static TroveFloor Generate(int seed, int floor) {
        var rng = new Random(unchecked(seed * 31 + floor * 7919));
        int target = 6 + (int)MathF.Round(floor * 1.5f); // 8 .. 14 rooms
        for (int attempt = 0; attempt < 50; attempt++) {
            var f = TryGenerate(rng, floor, target);
            if (f is not null) {
                BuildRooms(f, seed);
                return f;
            }
        }
        // never really happens; a straight line of rooms works anyway
        var line = new TroveFloor { Number = floor, Name = FloorNames[floor - 1] };
        for (int i = 0; i < target; i++)
            line.Rooms[new Point(i, 0)] = new TroveRoom { Cell = new Point(i, 0), Kind = i == 0 ? TroveRoomKind.Start : i == target - 1 ? TroveRoomKind.Boss : TroveRoomKind.Normal };
        line.Start = new Point(0, 0);
        line.Boss = new Point(target - 1, 0);
        BuildRooms(line, seed);
        return line;
    }

    private static int Neighbors(HashSet<Point> cells, Point p) {
        int n = 0;
        foreach (TroveDir d in Enum.GetValues<TroveDir>()) {
            var s = Step(d);
            if (cells.Contains(new Point(p.X + s.X, p.Y + s.Y)))
                n++;
        }
        return n;
    }

    private static TroveFloor? TryGenerate(Random rng, int floor, int target) {
        var start = new Point(4, 4);
        var cells = new HashSet<Point> { start };
        var queue = new Queue<Point>();
        queue.Enqueue(start);
        int guard = 0;
        while (cells.Count < target) {
            if (++guard > 2000)
                return null; // boxed in: start over
            if (queue.Count == 0) {
                // grow again from a random room
                queue.Enqueue(cells.ElementAt(rng.Next(cells.Count)));
            }
            var c = queue.Dequeue();
            foreach (var d in Enum.GetValues<TroveDir>().OrderBy(_ => rng.Next())) {
                if (cells.Count >= target)
                    break;
                var s = Step(d);
                var n = new Point(c.X + s.X, c.Y + s.Y);
                if (n.X < 0 || n.Y < 0 || n.X > 8 || n.Y > 7 || cells.Contains(n))
                    continue;
                if (Neighbors(cells, n) > 1 || rng.NextDouble() < 0.45)
                    continue;
                cells.Add(n);
                queue.Enqueue(n);
            }
        }

        // boss: the dead end farthest from the start
        var dist = new Dictionary<Point, int> { [start] = 0 };
        var bfs = new Queue<Point>();
        bfs.Enqueue(start);
        while (bfs.Count > 0) {
            var c = bfs.Dequeue();
            foreach (TroveDir d in Enum.GetValues<TroveDir>()) {
                var s = Step(d);
                var n = new Point(c.X + s.X, c.Y + s.Y);
                if (cells.Contains(n) && !dist.ContainsKey(n)) {
                    dist[n] = dist[c] + 1;
                    bfs.Enqueue(n);
                }
            }
        }
        var deadEnds = cells.Where(c => c != start && Neighbors(cells, c) == 1).ToList();
        if (deadEnds.Count == 0)
            return null;
        var boss = deadEnds.OrderByDescending(c => dist[c]).ThenBy(c => c.X).ThenBy(c => c.Y).First();
        if (dist[boss] < 2)
            return null;

        // item room: another dead end if there is one (else a quiet room off to the side)
        var itemOptions = deadEnds.Where(c => c != boss).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        if (itemOptions.Count == 0)
            itemOptions = cells.Where(c => c != start && c != boss && Neighbors(cells, c) <= 2).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        Point? item = itemOptions.Count > 0 ? itemOptions[rng.Next(itemOptions.Count)] : null;

        // challenge room: another dead end, if there's one left
        var challengeOptions = deadEnds.Where(c => c != boss && c != item).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
        Point? challenge = challengeOptions.Count > 0 ? challengeOptions[rng.Next(challengeOptions.Count)] : null;

        // secret room: an empty spot touching the dungeon (most neighbours first), never next to the boss or the start
        var secretOptions = new List<(Point P, int N)>();
        for (int x = 0; x <= 8; x++)
            for (int y = 0; y <= 7; y++) {
                var p = new Point(x, y);
                if (cells.Contains(p))
                    continue;
                int n = Neighbors(cells, p);
                bool bad = Enum.GetValues<TroveDir>().Any(d => { var s = Step(d); var q = new Point(x + s.X, y + s.Y); return q == boss || q == start; });
                if (n > 0 && !bad)
                    secretOptions.Add((p, n));
            }
        Point? secret = null;
        if (secretOptions.Count > 0) {
            int best = secretOptions.Max(o => o.N);
            var top = secretOptions.Where(o => o.N == best).Select(o => o.P).OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
            secret = top[rng.Next(top.Count)];
        }

        var floorMap = new TroveFloor { Number = floor, Name = FloorNames[floor - 1], Start = start, Boss = boss, ItemRoom = item, Secret = secret };
        foreach (var c in cells.OrderBy(c => c.Y).ThenBy(c => c.X))
            floorMap.Rooms[c] = new TroveRoom {
                Cell = c,
                Kind = c == start ? TroveRoomKind.Start : c == boss ? TroveRoomKind.Boss : c == item ? TroveRoomKind.Item
                    : c == challenge ? TroveRoomKind.Challenge : TroveRoomKind.Normal,
            };
        if (secret is { } sp)
            floorMap.Rooms[sp] = new TroveRoom { Cell = sp, Kind = TroveRoomKind.Secret };
        return floorMap;
    }

    // ------------------------------------------------------------------ inside each room

    private static void BuildRooms(TroveFloor f, int seed) {
        bool campaign = SeedUsesCampaign(seed) && CampaignAvailable;
        foreach (var room in f.Rooms.Values) {
            var rng = new Random(unchecked(seed * 131 + f.Number * 977 + room.Cell.X * 61 + room.Cell.Y * 13));
            if (room.Kind == TroveRoomKind.Normal && campaign && rng.NextDouble() < CAMPAIGN_ROOM_CHANCE && BuildCampaignRoom(f, room, rng))
                continue;
            BuildRoom(f, room, rng);
        }
        f.Rooms[f.Start].Cleared = true;
        f.Rooms[f.Start].Visited = true;
    }

    // corridor cells from each door to the middle, always kept clear
    private static bool InCorridor(int x, int y, TroveDir d) => d switch {
        TroveDir.North => x is >= 9 and <= 12 && y <= 9,
        TroveDir.South => x is >= 9 and <= 12 && y >= 7,
        TroveDir.West => y is >= 7 and <= 9 && x <= 12,
        _ => y is >= 7 and <= 9 && x >= 9,
    };

    /// <summary>Clears the middle of the room and a path from every door to it.</summary>
    /// <summary>A cork wall across a hidden doorway (blow it open, or find the room with an explosion).</summary>
    public static bool InCrack(int x, int y, TroveDir dir) => dir switch {
        TroveDir.North => x is >= 9 and <= 12 && y <= 1,
        TroveDir.South => x is >= 9 and <= 12 && y >= GH - 2,
        TroveDir.West => y is >= 7 and <= 9 && x <= 1,
        _ => y is >= 7 and <= 9 && x >= GW - 2,
    };
    private static void SecretCrack(int[,] solid, int[,] type, TroveDir? d) {
        if (d is not { } dir)
            return;
        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++) {
                if (InCrack(x, y, dir)) {
                    solid[x, y] = 1;
                    type[x, y] = BlockID.Cork;
                }
            }
    }

    private static void Carve(int[,] solid, List<TroveDir> doors) {
        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++) {
                bool keep = x is >= 8 and <= 13 && y is >= 6 and <= 10;
                foreach (var d in doors)
                    keep |= InCorridor(x, y, d);
                if (keep)
                    solid[x, y] = 0;
            }
    }

    /// <summary>
    /// Makes sure every open part of the room can be reached from the middle, by driving or by shooting through
    /// breakable (cork) blocks: where solid walls shut a part off, the cheapest way through them turns into cork,
    /// two blocks wide so a tank fits once it's broken. Teleporters count as a way through.
    /// </summary>
    /// <returns>Open cells that still can't be reached (shut in by holes): nothing should be put there.</returns>
    private static HashSet<Point> ConnectRegions(int[,] solid, int[,] type, List<(int X, int Y, int Link)> teleporters) {
        var stuck = new HashSet<Point>();
        bool Open(int x, int y) => solid[x, y] == 0 || type[x, y] == BlockID.Cork;
        IEnumerable<Point> Next(Point p) {
            if (p.X > 0) yield return new(p.X - 1, p.Y);
            if (p.X < GW - 1) yield return new(p.X + 1, p.Y);
            if (p.Y > 0) yield return new(p.X, p.Y - 1);
            if (p.Y < GH - 1) yield return new(p.X, p.Y + 1);
            foreach (var t in teleporters)
                if (t.X == p.X && t.Y == p.Y)
                    foreach (var o in teleporters)
                        if (o.Link == t.Link && (o.X != t.X || o.Y != t.Y))
                            yield return new(o.X, o.Y);
        }
        for (int round = 0; round < 40; round++) {
            // what can be reached now
            var reached = new bool[GW, GH];
            var q = new Queue<Point>();
            q.Enqueue(new Point(10, 8));
            reached[10, 8] = true;
            while (q.Count > 0) {
                var c = q.Dequeue();
                foreach (var n in Next(c))
                    if (!reached[n.X, n.Y] && Open(n.X, n.Y)) {
                        reached[n.X, n.Y] = true;
                        q.Enqueue(n);
                    }
            }
            Point? lost = null;
            for (int y = 0; y < GH && lost is null; y++)
                for (int x = 0; x < GW; x++)
                    if (solid[x, y] == 0 && !reached[x, y] && !stuck.Contains(new Point(x, y))) {
                        lost = new Point(x, y);
                        break;
                    }
            if (lost is not { } goal)
                return stuck;

            // cheapest way from the reached part to it: open cells cost nothing, wooden blocks 1 each, holes can't be crossed
            var cost = new int[GW, GH];
            var from = new Point?[GW, GH];
            for (int x = 0; x < GW; x++)
                for (int y = 0; y < GH; y++)
                    cost[x, y] = reached[x, y] ? 0 : int.MaxValue;
            var dq = new LinkedList<Point>();
            for (int x = 0; x < GW; x++)
                for (int y = 0; y < GH; y++)
                    if (reached[x, y])
                        dq.AddLast(new Point(x, y));
            while (dq.Count > 0) {
                var c = dq.First!.Value;
                dq.RemoveFirst();
                foreach (var n in Next(c)) {
                    if (solid[n.X, n.Y] > 0 && type[n.X, n.Y] == BlockID.Hole)
                        continue;
                    int step = Open(n.X, n.Y) ? 0 : 1;
                    if (cost[c.X, c.Y] + step >= cost[n.X, n.Y])
                        continue;
                    cost[n.X, n.Y] = cost[c.X, c.Y] + step;
                    from[n.X, n.Y] = c;
                    if (step == 0) dq.AddFirst(n); else dq.AddLast(n);
                }
            }
            if (cost[goal.X, goal.Y] == int.MaxValue) {
                stuck.Add(goal); // walled in by holes: nothing to do
                continue;
            }
            // turn the walls along the way into cork
            for (Point? c = goal; c is { } p && !reached[p.X, p.Y]; c = from[p.X, p.Y]) {
                if (solid[p.X, p.Y] == 0 || type[p.X, p.Y] != BlockID.Wood)
                    continue;
                type[p.X, p.Y] = BlockID.Cork;
                // and one beside it, so the gap is wide enough to drive through
                var prev = from[p.X, p.Y];
                bool vertical = prev is { } pv && pv.X == p.X;
                int wx = vertical ? p.X + 1 : p.X, wy = vertical ? p.Y : p.Y + 1;
                if (wx < GW && wy < GH && solid[wx, wy] > 0 && type[wx, wy] == BlockID.Wood)
                    type[wx, wy] = BlockID.Cork;
            }
        }
        return stuck;
    }

    private static readonly Vector2[] DoorCells = [new(10.5f, 0), new(21, 8), new(10.5f, 16), new(0, 8)];

    /// <summary>A fight room laid out like a campaign mission (its blocks and its tanks), with paths cut to the doors.</summary>
    private static bool BuildCampaignRoom(TroveFloor f, TroveRoom room, Random rng) {
        var maps = CampaignMaps!;
        int per = maps.Length / FLOORS;
        var m = maps[(f.Number - 1) * per + rng.Next(per)];
        if (m.Blocks is null || m.Tanks is null)
            return false;
        var doors = DoorsAll(f, room).ToList();
        var solid = new int[GW, GH];
        var type = new int[GW, GH];
        var pads = new List<(int X, int Y, int Link)>();
        foreach (var b in m.Blocks) {
            var g = ToGrid(b.Position);
            if (g.X < 0 || g.Y < 0 || g.X >= GW || g.Y >= GH)
                continue;
            if (b.Type == BlockID.Teleporter) {
                // not in the middle (where pickups and the floor's first spawn are)
                if (!(g.X is >= 8 and <= 13 && g.Y is >= 6 and <= 10))
                    pads.Add((g.X, g.Y, b.TpLink));
                continue;
            }
            if (b.Type is not (BlockID.Wood or BlockID.Cork or BlockID.Hole))
                continue;
            solid[g.X, g.Y] = Math.Max(1, (int)b.Stack);
            type[g.X, g.Y] = b.Type;
        }
        Carve(solid, doors);
        SecretCrack(solid, type, SecretDoor(f, room));
        // teleporters work in pairs: keep only complete pairs, on open floor
        pads.RemoveAll(t => solid[t.X, t.Y] > 0);
        pads = pads.GroupBy(t => t.Link).Where(g => g.Count() == 2).SelectMany(g => g).ToList();
        room.Teleporters = pads;
        var stuck = ConnectRegions(solid, type, pads);
        room.Shape = TroveShape.Open;
        room.CampaignMap = m.Name;
        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++)
                if (solid[x, y] > 0)
                    room.Blocks.Add((x, y, type[x, y], type[x, y] == BlockID.Hole ? 1 : solid[x, y]));

        if (rng.NextDouble() < 0.3)
            room.Pickup = TroveItems.DropPool[rng.Next(TroveItems.DropPool.Length)];

        // the mission's own enemies, except ones sitting right by a door (you'd drive into them) or inside a block
        var used = doors.Select(d => DoorCells[(int)d]).ToList();
        int cap = 3 + f.Number;
        foreach (var t in m.Tanks) {
            if (t.IsPlayer || room.Enemies.Count >= cap)
                continue;
            var g = ToGrid(t.Position);
            if (g.X < 1 || g.Y < 1 || g.X >= GW - 1 || g.Y >= GH - 1 || solid[g.X, g.Y] > 0 || stuck.Contains(g) || pads.Any(p => p.X == g.X && p.Y == g.Y))
                continue;
            if (used.Any(dc => Vector2.Distance(dc, new Vector2(g.X, g.Y)) < 5f))
                continue;
            if (g.X is >= 8 and <= 13 && g.Y is >= 6 and <= 10)
                continue; // the middle (where the pickups go)
            // nothing tougher than this floor allows (early campaign missions have purple and green tanks)
            int tier = t.AiTier > MaxTier[f.Number - 1] || t.AiTier < TankID.Brown ? Pools[f.Number - 1][rng.Next(Pools[f.Number - 1].Length)] : t.AiTier;
            room.Enemies.Add((g.X, g.Y, tier, t.Rotation));
        }
        if (room.Enemies.Count == 0) {
            // none left: one from this floor's pool, as far from the doors as possible
            var pool = Pools[f.Number - 1];
            Point best = new(3, 3);
            float bestD = -1;
            for (int x = 1; x < GW - 1; x++)
                for (int y = 1; y < GH - 1; y++)
                    if (solid[x, y] == 0 && !stuck.Contains(new Point(x, y)) && !pads.Any(p => p.X == x && p.Y == y)) {
                        float d = used.Count == 0 ? 0 : used.Min(dc => Vector2.Distance(dc, new Vector2(x, y)));
                        if (d > bestD) { bestD = d; best = new Point(x, y); }
                    }
            room.Enemies.Add((best.X, best.Y, pool[rng.Next(pool.Length)], rng.NextSingle() * MathHelper.TwoPi));
        }
        return true;
    }

    private static void BuildRoom(TroveFloor f, TroveRoom room, Random rng) {
        var doors = DoorsAll(f, room).ToList();
        var solid = new int[GW, GH];   // 0 = free, else height
        var type = new int[GW, GH];

        void Fill(int x0, int y0, int x1, int y1, int h, int t = BlockID.Wood) {
            for (int x = Math.Max(0, x0); x <= Math.Min(GW - 1, x1); x++)
                for (int y = Math.Max(0, y0); y <= Math.Min(GH - 1, y1); y++) {
                    solid[x, y] = h;
                    type[x, y] = t;
                }
        }
        void Mirror(int x, int y, int h, int t) {
            Fill(x, y, x, y, h, t);
            Fill(GW - 1 - x, y, GW - 1 - x, y, h, t);
            Fill(x, GH - 1 - y, x, GH - 1 - y, h, t);
            Fill(GW - 1 - x, GH - 1 - y, GW - 1 - x, GH - 1 - y, h, t);
        }

        room.Shape = room.Kind switch {
            TroveRoomKind.Start or TroveRoomKind.Item or TroveRoomKind.Secret or TroveRoomKind.Challenge => TroveShape.Open,
            TroveRoomKind.Boss => rng.Next(2) == 0 ? TroveShape.Open : TroveShape.Pillars,
            _ => (TroveShape)rng.Next(Enum.GetValues<TroveShape>().Length),
        };

        const int WALL = 3;
        switch (room.Shape) {
            case TroveShape.Wide:
                Fill(0, 0, GW - 1, 3, WALL);
                Fill(0, GH - 4, GW - 1, GH - 1, WALL);
                break;
            case TroveShape.Tall:
                Fill(0, 0, 5, GH - 1, WALL);
                Fill(GW - 6, 0, GW - 1, GH - 1, WALL);
                break;
            case TroveShape.Small:
                Fill(0, 0, GW - 1, 2, WALL);
                Fill(0, GH - 3, GW - 1, GH - 1, WALL);
                Fill(0, 0, 4, GH - 1, WALL);
                Fill(GW - 5, 0, GW - 1, GH - 1, WALL);
                break;
            case TroveShape.Cross:
                Fill(0, 0, 6, 4, WALL);
                Fill(GW - 7, 0, GW - 1, 4, WALL);
                Fill(0, GH - 5, 6, GH - 1, WALL);
                Fill(GW - 7, GH - 5, GW - 1, GH - 1, WALL);
                break;
            case TroveShape.Corner: {
                // an L-shaped room: one quarter is wall
                bool right = rng.Next(2) == 0, bottom = rng.Next(2) == 0;
                Fill(right ? GW - 9 : 0, bottom ? GH - 7 : 0, right ? GW - 1 : 8, bottom ? GH - 1 : 6, WALL);
                break;
            }
            case TroveShape.Pillars:
                foreach (var (px, py) in new[] { (4, 3), (16, 3), (4, 12), (16, 12) })
                    Fill(px, py, px + 1, py + 1, 2);
                break;
        }

        // cover inside the open part: a few mirrored pieces, some breakable
        int pieces = room.Kind is TroveRoomKind.Start or TroveRoomKind.Item or TroveRoomKind.Secret ? 2 : rng.Next(2, 5);
        for (int i = 0; i < pieces; i++) {
            int x = rng.Next(2, 9), y = rng.Next(1, 7);
            int h = rng.Next(1, 3);
            int t = rng.NextDouble() < 0.35 ? BlockID.Cork : BlockID.Wood;
            Mirror(x, y, h, t);
            if (rng.Next(2) == 0) Mirror(x + 1, y, h, t);
            else Mirror(x, y + 1, h, t);
        }

        // keep every door's path to the middle open (and the middle itself), and no part of the room shut off
        Carve(solid, doors);
        SecretCrack(solid, type, SecretDoor(f, room));
        var stuck = ConnectRegions(solid, type, []);

        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++)
                if (solid[x, y] > 0)
                    room.Blocks.Add((x, y, type[x, y], solid[x, y]));

        // item room: an item on a pedestal and nothing to fight (secret rooms: a rarer one)
        if (room.Kind == TroveRoomKind.Item) {
            room.Pickup = TroveItems.PedestalPool[rng.Next(TroveItems.PedestalPool.Length)];
            room.Cleared = true;
            return;
        }
        if (room.Kind == TroveRoomKind.Secret) {
            room.Pickup = TroveItems.SecretPool[rng.Next(TroveItems.SecretPool.Length)];
            room.Cleared = true;
            return;
        }
        if (room.Kind == TroveRoomKind.Challenge)
            room.Pickup = TroveItems.ChallengePool[rng.Next(TroveItems.ChallengePool.Length)];
        // enemies
        if (room.Kind == TroveRoomKind.Start)
            return;
        // some fight rooms leave a heart or a shield once they're cleared
        if (room.Kind == TroveRoomKind.Normal && rng.NextDouble() < 0.3)
            room.Pickup = TroveItems.DropPool[rng.Next(TroveItems.DropPool.Length)];
        var free = new List<Point>();
        for (int x = 1; x < GW - 1; x++)
            for (int y = 1; y < GH - 1; y++)
                if (solid[x, y] == 0 && !stuck.Contains(new Point(x, y)))
                    free.Add(new Point(x, y));
        var doorCells = new[] { new Vector2(10.5f, 0), new Vector2(21, 8), new Vector2(10.5f, 16), new Vector2(0, 8) };
        float Far(Point p) => doorCells.Min(dc => Vector2.Distance(dc, new Vector2(p.X, p.Y)));

        var pool = Pools[f.Number - 1];
        var taken = new List<Point>();
        Point? Pick(float minDoor) {
            var options = free.Where(p => Far(p) >= minDoor && taken.All(t => Math.Abs(t.X - p.X) + Math.Abs(t.Y - p.Y) >= 3)).ToList();
            if (options.Count == 0)
                return null;
            var p = options[rng.Next(options.Count)];
            taken.Add(p);
            return p;
        }

        if (room.Kind == TroveRoomKind.Boss) {
            // boss in the middle-ish, a couple of guards
            var bp = Pick(7) ?? Pick(4) ?? new Point(10, 8);
            room.BossIndex = 0;
            room.Enemies.Add((bp.X, bp.Y, Bosses[f.Number - 1], rng.NextSingle() * MathHelper.TwoPi));
            int guards = 1 + f.Number / 2;
            for (int i = 0; i < guards; i++)
                if ((Pick(6) ?? Pick(3)) is { } gp)
                    room.Enemies.Add((gp.X, gp.Y, pool[rng.Next(pool.Length)], rng.NextSingle() * MathHelper.TwoPi));
            return;
        }
        // floor 1: lots of easy tanks; deeper floors: fewer but tougher
        int count = f.Number == 1 ? rng.Next(3, 6) : 2 + f.Number / 2 + rng.Next(0, 2);
        for (int i = 0; i < count; i++)
            if ((Pick(6) ?? Pick(3)) is { } ep)
                room.Enemies.Add((ep.X, ep.Y, pool[rng.Next(pool.Length)], rng.NextSingle() * MathHelper.TwoPi));
        if (room.Kind == TroveRoomKind.Challenge) {
            // a second wave, a bit bigger, from wherever's free (the first wave's spots are fine: they're gone by then)
            taken.Clear();
            for (int i = 0; i < count + 1; i++)
                if ((Pick(6) ?? Pick(3)) is { } wp)
                    room.Wave2.Add((wp.X, wp.Y, pool[rng.Next(pool.Length)], rng.NextSingle() * MathHelper.TwoPi));
        }
    }

    // ------------------------------------------------------------------ turning a room into a playable mission

    /// <summary>Where players appear when they come in through <paramref name="entry"/> (null = start of floor).</summary>
    private static Point[] SpawnCells(TroveDir? entry) => entry switch {
        TroveDir.North => [new(10, 2), new(11, 2), new(9, 3), new(12, 3)],
        TroveDir.South => [new(10, 14), new(11, 14), new(9, 13), new(12, 13)],
        TroveDir.West => [new(2, 8), new(2, 7), new(3, 9), new(3, 7)],
        TroveDir.East => [new(19, 8), new(19, 7), new(18, 9), new(18, 7)],
        _ => [new(10, 8), new(11, 8), new(10, 9), new(11, 9)],
    };

    public static Mission BuildMission(TroveFloor f, TroveRoom room, TroveDir? entry) {
        var tanks = new List<TankTemplate>();
        var spawns = SpawnCells(entry);
        float facing = entry switch {
            TroveDir.North => 0f,
            TroveDir.South => MathHelper.Pi,
            TroveDir.West => MathHelper.PiOver2 * 3,
            TroveDir.East => MathHelper.PiOver2,
            _ => 0f,
        };
        for (int p = 0; p < 4; p++)
            tanks.Add(new TankTemplate {
                IsPlayer = true,
                PlayerType = p,
                Position = new BlockMapPosition(spawns[p].X, spawns[p].Y),
                Rotation = facing,
                Team = TeamID.Red,
            });
        if (!room.Cleared)
            foreach (var e in room.Enemies)
                tanks.Add(new TankTemplate {
                    IsPlayer = false,
                    AiTier = e.Tier,
                    Position = new BlockMapPosition(e.X, e.Y),
                    Rotation = e.Rotation,
                    Team = TeamID.Blue,
                });
        var blocks = room.Blocks.Select(b => new BlockTemplate {
            Type = b.Type,
            Stack = (byte)b.Height,
            Position = new BlockMapPosition(b.X, b.Y),
            TpLink = -1,
        }).Concat(room.Teleporters.Select(t => new BlockTemplate {
            Type = BlockID.Teleporter,
            Stack = 1,
            Position = new BlockMapPosition(t.X, t.Y),
            TpLink = (sbyte)t.Link,
        })).ToArray();
        return new Mission(tanks.ToArray(), blocks) {
            Name = $"Floor {f.Number}: {f.Name}",
            Note = string.Empty,
        };
    }

    /// <summary>World position of a room's boss spawn (to find the boss tank after the room loads).</summary>
    public static Vector2? BossPosition(TroveRoom room) =>
        room.BossIndex >= 0 && !room.Cleared ? (Vector2)new BlockMapPosition(room.Enemies[room.BossIndex].X, room.Enemies[room.BossIndex].Y) : null;
}
