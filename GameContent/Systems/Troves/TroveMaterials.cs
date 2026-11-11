using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.ID;
using B = TanksRebirth.GameContent.Systems.Troves.TroveBlockID;

namespace TanksRebirth.GameContent.Systems.Troves;

/// <summary>
/// Dresses Tank Troves rooms in the extra block types. Once a room is laid out (made up, or a campaign mission's
/// map), it may stay plain wood, get a mix of a few materials here and there, or be themed: a whole room (or one
/// half of it) built from one set of materials, like a junkyard of crates and barrels or a frozen room of ice.
/// Only wooden blocks change, so the paths, cork and holes the layout relies on stay as they were, and floor pieces
/// only go on open floor away from doors, the middle, spawns and enemies.
/// Rolled with its own dice, so the rest of the dungeon is the same on every phone and doesn't change.
/// </summary>
internal static class TroveMaterials {
    /// <summary>Testing ("materials" on the 8th line of troves_test.txt): a showcase of every block in the first and boss
    /// rooms, and every other room themed or mixed.</summary>
    public static bool TestShowcase;

    private enum Style { Plain, Mixed, Themed }

    private sealed record Theme(string Name, (int Type, int Weight)[] Walls, int[] Sprinkle, (int Type, int Count)[] Floors);

    private static readonly Theme[] Themes = [
        new("Junkyard", [(B.Crate, 3), (B.Steel, 3), (B.Rotten, 3)], [B.Barrel, B.Crate], [(B.Mud, 2), (B.Spikes, 1)]),
        new("Glasshouse", [(B.Glass, 5), (B.Steel, 2), (B.Bush, 2)], [B.Bush], [(B.Mud, 1)]),
        new("Frozen", [(B.Ice, 5), (B.Glass, 2), (B.Steel, 1)], [B.Glass], [(B.IceFloor, 3)]),
        new("Foundry", [(B.Steel, 5), (B.Grate, 3)], [B.Magnet], [(B.Conveyor, 2), (B.Lava, 1), (B.Spikes, 1)]),
        new("Farm", [(B.Hay, 5), (B.Rotten, 3), (B.Crate, 1), (B.Bush, 2)], [B.Crate], [(B.Mud, 2)]),
        new("Funhouse", [(B.Mirror, 3), (B.Rubber, 4), (B.Glass, 2)], [B.Mirror], [(B.Conveyor, 2)]),
        new("Prison", [(B.Steel, 4), (B.Grate, 3), (B.Gate, 3)], [B.Grate], [(B.Plate, 1), (B.Spikes, 2)]),
        new("Overgrown", [(B.Bush, 4), (B.Rotten, 3), (B.Hay, 2)], [B.Bush], [(B.Mud, 2)]),
    ];

    // what a "mixed" room can draw from
    private static readonly int[] MixWalls = [B.Rotten, B.Glass, B.Crate, B.Ice, B.Steel, B.Rubber, B.Mirror, B.Hay, B.Grate, B.Bush];
    private static readonly int[] MixFloors = [B.Mud, B.IceFloor, B.Conveyor, B.Spikes, B.Lava];

    private const int GW = TroveGen.GW, GH = TroveGen.GH;
    private static readonly Point[] Dirs = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];

    public static void Apply(TroveFloor f, TroveRoom room, int seed) {
        if (TroveRun.Battle)
            return;
        bool showcase = TestShowcase && room.Kind is TroveRoomKind.Start or TroveRoomKind.Boss;
        if (room.Kind == TroveRoomKind.Start && !showcase)
            return; // the first room of a floor stays as it always was
        var rng = new Random(unchecked(seed * 6007 + f.Number * 50021 + room.Cell.X * 701 + room.Cell.Y * 1931 + 17));
        var map = new Map(f, room);
        if (showcase) {
            Showcase(map, rng);
            map.Save();
            return;
        }

        bool campaign = room.CampaignMap is not null;
        double r = rng.NextDouble();
        Style style;
        if (TestShowcase)
            style = r < 0.6 ? Style.Themed : Style.Mixed;
        else if (room.CorkStyle)
            style = r < 0.6 ? Style.Plain : Style.Mixed;
        else if (campaign)
            style = r < 0.6 ? Style.Plain : r < 0.9 ? Style.Mixed : Style.Themed;   // mostly the levels as they are
        else if (f.Number == 1)
            style = r < 0.5 ? Style.Plain : r < 0.8 ? Style.Mixed : Style.Themed;
        else
            style = r < 0.35 ? Style.Plain : r < 0.7 ? Style.Mixed : Style.Themed;

        switch (style) {
            case Style.Mixed: Mixed(map, rng); break;
            case Style.Themed: Themed(map, rng, wholeRoom: !campaign && rng.NextDouble() < 0.55); break;
            default: return;
        }
        map.Save();
    }

    // ------------------------------------------------------------------ the room as a grid

    private sealed class Map {
        public readonly TroveRoom Room;
        public readonly int[,] Type = new int[GW, GH];
        public readonly int[,] Height = new int[GW, GH];
        public readonly int[,] Floor = new int[GW, GH];      // a floor piece's type (0 = none)
        public readonly int[,] FloorH = new int[GW, GH];
        public readonly bool[,] Reserved = new bool[GW, GH]; // open floor that must stay bare
        public int Barrels;

        public Map(TroveFloor f, TroveRoom room) {
            Room = room;
            foreach (var b in room.Blocks) {
                if (B.IsFloor(b.Type)) { Floor[b.X, b.Y] = b.Type; FloorH[b.X, b.Y] = b.Height; }
                else { Type[b.X, b.Y] = b.Type; Height[b.X, b.Y] = b.Height; }
            }
            var doors = TroveGen.DoorsAll(f, room).ToList();
            void Res(int x, int y, int r) {
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        if (In(x + dx, y + dy))
                            Reserved[x + dx, y + dy] = true;
            }
            for (int x = 0; x < GW; x++)
                for (int y = 0; y < GH; y++) {
                    if (x is >= 7 and <= 14 && y is >= 5 and <= 11)
                        Reserved[x, y] = true; // the middle: spawns, pickups, the item pedestal
                    foreach (var d in doors)
                        if (TroveGen.InCorridor(x, y, d))
                            Reserved[x, y] = true;
                    if (x == 0 || y == 0 || x == GW - 1 || y == GH - 1)
                        Reserved[x, y] = true;
                }
            foreach (TroveDir? d in new TroveDir?[] { null, TroveDir.North, TroveDir.East, TroveDir.South, TroveDir.West })
                foreach (var p in TroveGen.SpawnCells(d))
                    Res(p.X, p.Y, 1);
            foreach (var e in room.Enemies.Concat(room.Wave2).Concat(room.Wave3))
                Res(e.X, e.Y, 1);
            foreach (var t in room.Teleporters)
                Res(t.X, t.Y, 1);
        }

        public static bool In(int x, int y) => x >= 0 && y >= 0 && x < GW && y < GH;
        public bool Wood(int x, int y) => In(x, y) && Height[x, y] > 0 && Type[x, y] == BlockID.Wood;
        public bool Open(int x, int y) => In(x, y) && Height[x, y] == 0 && Floor[x, y] == 0;
        public bool FloorOk(int x, int y) => Open(x, y) && !Reserved[x, y];

        /// <summary>Can a tank get from <paramref name="x"/>,<paramref name="y"/> to the middle (breaking breakable blocks on the way)?</summary>
        public int Reachable() {
            bool Pass(int x, int y) {
                if (Floor[x, y] == B.Lava)
                    return false;
                if (Height[x, y] == 0)
                    return true;
                int t = Type[x, y];
                return t == BlockID.Cork || t == B.Bush || t == B.Gate || B.IsBreakable(t);
            }
            var seen = new bool[GW, GH];
            var q = new Queue<Point>();
            q.Enqueue(new Point(10, 8));
            seen[10, 8] = true;
            int n = 0;
            while (q.Count > 0) {
                var c = q.Dequeue();
                n++;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) {
                    int x = c.X + dx, y = c.Y + dy;
                    if (In(x, y) && !seen[x, y] && Pass(x, y)) {
                        seen[x, y] = true;
                        q.Enqueue(new Point(x, y));
                    }
                }
                foreach (var t in Room.Teleporters)
                    if (t.X == c.X && t.Y == c.Y)
                        foreach (var o in Room.Teleporters)
                            if (o.Link == t.Link && !seen[o.X, o.Y]) {
                                seen[o.X, o.Y] = true;
                                q.Enqueue(new Point(o.X, o.Y));
                            }
            }
            return n;
        }

        public void Save() {
            Room.Blocks.Clear();
            for (int x = 0; x < GW; x++)
                for (int y = 0; y < GH; y++) {
                    if (Height[x, y] > 0)
                        Room.Blocks.Add((x, y, Type[x, y], Height[x, y]));
                    if (Floor[x, y] != 0)
                        Room.Blocks.Add((x, y, Floor[x, y], Math.Max(1, FloorH[x, y])));
                }
        }

        /// <summary>The room's wooden walls, as groups of touching blocks.</summary>
        public List<List<Point>> WoodClusters(Func<int, int, bool>? where = null) {
            var seen = new bool[GW, GH];
            var all = new List<List<Point>>();
            for (int x = 0; x < GW; x++)
                for (int y = 0; y < GH; y++) {
                    if (seen[x, y] || !Wood(x, y) || where?.Invoke(x, y) == false)
                        continue;
                    var c = new List<Point>();
                    var q = new Queue<Point>();
                    q.Enqueue(new Point(x, y));
                    seen[x, y] = true;
                    while (q.Count > 0) {
                        var p = q.Dequeue();
                        c.Add(p);
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) {
                            int nx = p.X + dx, ny = p.Y + dy;
                            if (Wood(nx, ny) && !seen[nx, ny] && where?.Invoke(nx, ny) != false) {
                                seen[nx, ny] = true;
                                q.Enqueue(new Point(nx, ny));
                            }
                        }
                    }
                    all.Add(c);
                }
            return all;
        }

        public void Set(Point p, int type) {
            Type[p.X, p.Y] = type;
            if (type is B.Gate or B.Magnet or B.Barrel)
                Height[p.X, p.Y] = 1;
            if (type == B.Barrel)
                Barrels++;
        }
    }

    // ------------------------------------------------------------------ walls

    /// <summary>Some material suits a group of blocks better than others (no huge walls of crates or barrels).</summary>
    private static int Fit(int type, int size, Random rng, int fallback) => type switch {
        B.Crate when size > 6 => fallback,
        B.Bush when size > 6 => fallback,
        _ => type,
    };

    private static int Pick((int Type, int Weight)[] list, Random rng) {
        int total = list.Sum(l => l.Weight), roll = rng.Next(total);
        foreach (var (t, w) in list) {
            if (roll < w)
                return t;
            roll -= w;
        }
        return list[0].Type;
    }

    private static void Sprinkle(Map m, List<Point> c, int type, Random rng, double chance) {
        foreach (var p in c) {
            if (rng.NextDouble() >= chance)
                continue;
            if (type == B.Barrel && m.Barrels >= 3)
                continue;
            // only on a wall's edge, where it can be shot (and a magnet only on steel)
            if (type == B.Magnet && m.Type[p.X, p.Y] != B.Steel)
                continue;
            bool edge = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d => m.Open(p.X + d.Item1, p.Y + d.Item2));
            if (edge)
                m.Set(p, type);
        }
    }

    private static void Themed(Map m, Random rng, bool wholeRoom) {
        var theme = Themes[rng.Next(Themes.Length)];
        // a section: one half of the room (or the whole thing)
        int side = rng.Next(4);
        bool InSection(int x, int y) => wholeRoom || side switch {
            0 => x < GW / 2,
            1 => x >= GW / 2,
            2 => y < GH / 2,
            _ => y >= GH / 2,
        };
        var clusters = m.WoodClusters(InSection);
        int fallback = theme.Walls.OrderByDescending(w => w.Weight).First(w => w.Type is not (B.Bush or B.Crate or B.Gate)).Type;
        foreach (var c in clusters) {
            int t = Fit(Pick(theme.Walls, rng), c.Count, rng, fallback);
            if (t == B.Gate) {
                // gates are doorways, not walls: two blocks side by side on the group's edge
                foreach (var p in c)
                    m.Set(p, fallback);
                // (where there's open floor on both sides, so an open gate is a way through)
                var edge = c.Where(p => m.Open(p.X - 1, p.Y) && m.Open(p.X + 1, p.Y) || m.Open(p.X, p.Y - 1) && m.Open(p.X, p.Y + 1)).ToList();
                if (edge.Count > 0) {
                    var g = edge[rng.Next(edge.Count)];
                    m.Set(g, B.Gate);
                    if (c.FirstOrDefault(p => Math.Abs(p.X - g.X) + Math.Abs(p.Y - g.Y) == 1 && edge.Contains(p)) is var g2 && g2 != g && edge.Contains(g2))
                        m.Set(g2, B.Gate);
                }
                continue;
            }
            // a big wall now and then gets a second material in it, in bands
            int second = c.Count >= 6 && rng.NextDouble() < 0.4 ? Fit(Pick(theme.Walls, rng), c.Count, rng, fallback) : t;
            if (second is B.Gate) second = t;
            bool rows = rng.Next(2) == 0;
            foreach (var p in c)
                m.Set(p, ((rows ? p.Y : p.X) / 2) % 2 == 0 ? t : second);
        }
        foreach (var c in clusters)
            Sprinkle(m, c, theme.Sprinkle[rng.Next(theme.Sprinkle.Length)], rng, 0.12);
        // explosive barrels where there's junk
        if (theme.Name == "Junkyard")
            foreach (var c in clusters.OrderBy(_ => rng.Next()).Take(2))
                Sprinkle(m, c, B.Barrel, rng, 0.25);

        foreach (var (type, count) in theme.Floors) {
            int n = wholeRoom ? count + rng.Next(2) : Math.Max(1, count - 1 + rng.Next(2));
            for (int i = 0; i < n; i++)
                FloorPatch(m, type, rng, InSection);
        }
        // a pressure plate only matters with gates in the room
        if (!Any(m, B.Gate))
            for (int x = 0; x < GW; x++)
                for (int y = 0; y < GH; y++)
                    if (m.Floor[x, y] == B.Plate)
                        m.Floor[x, y] = 0;
    }

    private static void Mixed(Map m, Random rng) {
        // one or two materials, in some of the room's walls and cover
        var picks = MixWalls.OrderBy(_ => rng.Next()).Take(1 + rng.Next(2)).ToArray();
        var clusters = m.WoodClusters();
        foreach (var c in clusters) {
            if (rng.NextDouble() >= 0.45)
                continue;
            int t = Fit(picks[rng.Next(picks.Length)], c.Count, rng, B.Steel);
            if (c.Count > 10 && rng.NextDouble() < 0.6) {
                // a long wall: just a stretch of it
                var start = c[rng.Next(c.Count)];
                foreach (var p in c)
                    if (Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) <= 3)
                        m.Set(p, t);
            }
            else
                foreach (var p in c)
                    m.Set(p, t);
        }
        // a barrel or two by the cover, sometimes
        if (rng.NextDouble() < 0.3)
            foreach (var c in clusters.Where(c => c.Count <= 8).OrderBy(_ => rng.Next()).Take(1))
                Sprinkle(m, c, B.Barrel, rng, 0.3);
        // and a patch of something on the floor
        if (rng.NextDouble() < 0.55)
            FloorPatch(m, MixFloors[rng.Next(MixFloors.Length)], rng, null);
    }

    private static bool Any(Map m, int type) {
        for (int x = 0; x < GW; x++)
            for (int y = 0; y < GH; y++)
                if (m.Height[x, y] > 0 && m.Type[x, y] == type)
                    return true;
        return false;
    }

    // ------------------------------------------------------------------ floor pieces

    /// <summary>A patch of a floor piece on open floor (a strip, for conveyors).</summary>
    private static bool FloorPatch(Map m, int type, Random rng, Func<int, int, bool>? where) {
        for (int attempt = 0; attempt < 40; attempt++) {
            int w, h, dir = 0;
            switch (type) {
                case B.Conveyor:
                    dir = rng.Next(4);
                    int len = 3 + rng.Next(3);
                    (w, h) = dir is 0 or 2 ? (1 + rng.Next(2), len) : (len, 1 + rng.Next(2));
                    break;
                case B.Plate: w = h = 1; break;
                case B.Lava: (w, h) = rng.Next(2) == 0 ? (2, 2 + rng.Next(2)) : (2 + rng.Next(2), 2); break;
                case B.Spikes: (w, h) = rng.Next(2) == 0 ? (2, 1) : (2, 2); break;
                default: (w, h) = (2 + rng.Next(3), 2 + rng.Next(2)); break; // mud, ice
            }
            int x0 = rng.Next(1, GW - w), y0 = rng.Next(1, GH - h);
            bool ok = true;
            for (int x = x0; x < x0 + w && ok; x++)
                for (int y = y0; y < y0 + h && ok; y++)
                    ok = m.FloorOk(x, y) && where?.Invoke(x, y) != false;
            if (!ok)
                continue;
            int before = type == B.Lava ? m.Reachable() : 0;
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++) {
                    m.Floor[x, y] = type;
                    m.FloorH[x, y] = type == B.Conveyor ? dir + 1 : 1;
                }
            // lava can't shut any part of the room off
            if (type == B.Lava && m.Reachable() < before - w * h) {
                for (int x = x0; x < x0 + w; x++)
                    for (int y = y0; y < y0 + h; y++)
                        m.Floor[x, y] = 0;
                continue;
            }
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ testing

    /// <summary>Every block type in one room, along the top and bottom, with the floor pieces beside them.</summary>
    private static void Showcase(Map m, Random rng) {
        int[] walls = [B.Rotten, B.Glass, B.Crate, B.Barrel, B.Ice, B.Steel, B.Rubber, B.Mirror, B.Hay, B.Grate, B.Bush, B.Magnet, B.Gate];
        var spots = new List<Point>();
        foreach (int y in new[] { 2, GH - 3, 1, GH - 2, 3, GH - 4 })
            for (int x = 1; x < GW - 1; x += 2)
                spots.Add(new Point(x, y));
        int i = 0;
        foreach (var s in spots) {
            if (i >= walls.Length)
                break;
            bool ok = true;
            for (int dx = 0; dx < 2; dx++)
                ok &= Map.In(s.X + dx, s.Y) && m.Open(s.X + dx, s.Y) && !m.Reserved[s.X + dx, s.Y];
            if (!ok)
                continue;
            for (int dx = 0; dx < 2; dx++) {
                m.Height[s.X + dx, s.Y] = 1;
                m.Set(new Point(s.X + dx, s.Y), walls[i]);
            }
            i++;
        }
        // and turn any wooden walls into something
        foreach (var c in m.WoodClusters())
            foreach (var p in c)
                m.Set(p, walls[((p.X / 2 + p.Y / 2) % 6) switch { 0 => 5, 1 => 6, 2 => 7, 3 => 1, 4 => 9, _ => 0 }]);
        int[] floors = [B.Mud, B.IceFloor, B.Conveyor, B.Spikes, B.Lava, B.Plate];
        Point[] at = [new(2, 4), new(GW - 4, 4), new(2, GH - 6), new(GW - 4, GH - 6), new(5, 4), new(GW - 7, GH - 6)];
        for (int k = 0; k < floors.Length; k++) {
            var a = at[k];
            int s = floors[k] == B.Plate ? 1 : 2;
            bool ok = true;
            for (int x = a.X; x < a.X + s; x++)
                for (int y = a.Y; y < a.Y + s; y++)
                    ok &= m.FloorOk(x, y);
            if (!ok) {
                FloorPatch(m, floors[k], rng, null);
                continue;
            }
            int before = m.Reachable();
            for (int x = a.X; x < a.X + s; x++)
                for (int y = a.Y; y < a.Y + s; y++) {
                    m.Floor[x, y] = floors[k];
                    m.FloorH[x, y] = floors[k] == B.Conveyor ? 2 : 1; // conveyors push right
                }
            if (floors[k] == B.Lava && m.Reachable() < before - s * s)
                for (int x = a.X; x < a.X + s; x++)
                    for (int y = a.Y; y < a.Y + s; y++)
                        m.Floor[x, y] = 0;
        }
    }
}
