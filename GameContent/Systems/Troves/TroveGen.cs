using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems.Coordinates;

namespace TanksRebirth.GameContent.Systems.Troves;

public enum TroveRoomKind { Start, Normal, Boss }
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
    public List<(int X, int Y, int Tier, float Rotation)> Enemies = [];
    /// <summary>Index into <see cref="Enemies"/> of the boss, or -1.</summary>
    public int BossIndex = -1;
}

public sealed class TroveFloor {
    public int Number;
    public string Name = "";
    public Dictionary<Point, TroveRoom> Rooms = [];
    public Point Start, Boss;

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
        [TankID.Brown, TankID.Brown, TankID.Ash, TankID.Ash, TankID.Marine],
        [TankID.Ash, TankID.Marine, TankID.Yellow, TankID.Pink, TankID.Pink],
        [TankID.Marine, TankID.Pink, TankID.Yellow, TankID.Violet, TankID.Green],
        [TankID.Violet, TankID.Green, TankID.White, TankID.Black, TankID.Pink],
        [TankID.Black, TankID.White, TankID.Green, TankID.Sapphire, TankID.Ruby, TankID.Citrine, TankID.Amethyst, TankID.Emerald],
    ];
    private static readonly int[] Bosses = [TankID.Marine, TankID.Violet, TankID.Green, TankID.Black, TankID.Obsidian];

    public static Point Step(TroveDir d) => d switch {
        TroveDir.North => new(0, -1),
        TroveDir.East => new(1, 0),
        TroveDir.South => new(0, 1),
        _ => new(-1, 0),
    };
    public static TroveDir Opposite(TroveDir d) => (TroveDir)(((int)d + 2) % 4);

    public static IEnumerable<TroveDir> Doors(TroveFloor f, TroveRoom r) {
        foreach (TroveDir d in Enum.GetValues<TroveDir>()) {
            var s = Step(d);
            if (f.Has(new Point(r.Cell.X + s.X, r.Cell.Y + s.Y)))
                yield return d;
        }
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

        var floorMap = new TroveFloor { Number = floor, Name = FloorNames[floor - 1], Start = start, Boss = boss };
        foreach (var c in cells.OrderBy(c => c.Y).ThenBy(c => c.X))
            floorMap.Rooms[c] = new TroveRoom {
                Cell = c,
                Kind = c == start ? TroveRoomKind.Start : c == boss ? TroveRoomKind.Boss : TroveRoomKind.Normal,
            };
        return floorMap;
    }

    // ------------------------------------------------------------------ inside each room

    private static void BuildRooms(TroveFloor f, int seed) {
        foreach (var room in f.Rooms.Values) {
            var rng = new Random(unchecked(seed * 131 + f.Number * 977 + room.Cell.X * 61 + room.Cell.Y * 13));
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

    private static void BuildRoom(TroveFloor f, TroveRoom room, Random rng) {
        var doors = Doors(f, room).ToList();
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
            TroveRoomKind.Start => TroveShape.Open,
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
        int pieces = room.Kind == TroveRoomKind.Start ? 2 : rng.Next(2, 5);
        for (int i = 0; i < pieces; i++) {
            int x = rng.Next(2, 9), y = rng.Next(1, 7);
            int h = rng.Next(1, 3);
            int t = rng.NextDouble() < 0.35 ? BlockID.Cork : BlockID.Wood;
            Mirror(x, y, h, t);
            if (rng.Next(2) == 0) Mirror(x + 1, y, h, t);
            else Mirror(x, y + 1, h, t);
        }

        // keep every door's path to the middle open (and the middle itself)
        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++) {
                bool keep = x is >= 8 and <= 13 && y is >= 6 and <= 10;
                foreach (var d in doors)
                    keep |= InCorridor(x, y, d);
                if (keep)
                    solid[x, y] = 0;
            }

        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++)
                if (solid[x, y] > 0)
                    room.Blocks.Add((x, y, type[x, y], solid[x, y]));

        // enemies
        if (room.Kind == TroveRoomKind.Start)
            return;
        var free = new List<Point>();
        for (int x = 1; x < GW - 1; x++)
            for (int y = 1; y < GH - 1; y++)
                if (solid[x, y] == 0)
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
        int count = 2 + f.Number / 2 + rng.Next(0, 2);
        for (int i = 0; i < count; i++)
            if ((Pick(6) ?? Pick(3)) is { } ep)
                room.Enemies.Add((ep.X, ep.Y, pool[rng.Next(pool.Length)], rng.NextSingle() * MathHelper.TwoPi));
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
        }).ToArray();
        return new Mission(tanks.ToArray(), blocks) {
            Name = $"Floor {f.Number}: {f.Name}",
            Note = string.Empty,
        };
    }

    /// <summary>World position of a room's boss spawn (to find the boss tank after the room loads).</summary>
    public static Vector2? BossPosition(TroveRoom room) =>
        room.BossIndex >= 0 && !room.Cleared ? (Vector2)new BlockMapPosition(room.Enemies[room.BossIndex].X, room.Enemies[room.BossIndex].Y) : null;
}
