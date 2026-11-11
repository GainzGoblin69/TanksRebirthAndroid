using System;
using System.Diagnostics;
using System.Text;

namespace TanksRebirth.Internals;

/// <summary>
/// A light frame profiler: every few seconds it writes one log line with the average time each part of a frame took,
/// the slowest frame, and how much memory a frame allocates (garbage means collector pauses, felt as stutter).
/// </summary>
public static class Perf {
    public enum S {
        // update
        UpdMisc, Players, AI, Shots, Blocks, Particles, MenuUpd, PostUpd, Logic, Physics, Net, TrItems, TrBoss, TrEnemies, TrThemes, TrRun, UpdateRest,
        // draw
        Room, World, Tanks, BlocksDraw, ShotsDraw, PartDraw, Lights, PostDraw, Scene3D, MenuText, Present, MenuUI, GameUI, Touch, Count
    }

    static readonly long[] _ticks = new long[(int)S.Count];
    static readonly long[] _bytes = new long[(int)S.Count];
    static long _markAlloc;
    static long _mark, _worstFrame, _alloc;
    static int _frames, _gen0, _gen1;
    static double _sinceLog;
    /// <summary>What's on screen ("menu", "mission", "troves"...), set by the game each frame.</summary>
    public static Func<string>? Scene;
    /// <summary>Called after each part of the frame (the NaN watch).</summary>
    public static Action<S>? NanCheck;

    /// <summary>Everything since the last mark belongs to <paramref name="s"/>.</summary>
    public static void Mark(S s) {
        NanCheck?.Invoke(s);
        long now = Stopwatch.GetTimestamp();
        _ticks[(int)s] += now - _mark;
        _mark = now;
        long mem = GC.GetAllocatedBytesForCurrentThread();
        _bytes[(int)s] += mem - _markAlloc;
        _markAlloc = mem;
    }

    static long _openAt, _openAlloc, _frameTicks;

    /// <summary>Starts timing a stretch of the frame (the update, or the draw).</summary>
    public static void Open() {
        _openAt = _mark = Stopwatch.GetTimestamp();
        _openAlloc = _markAlloc = GC.GetAllocatedBytesForCurrentThread();
    }

    public static void Close() {
        _frameTicks += Stopwatch.GetTimestamp() - _openAt;
        _alloc += GC.GetAllocatedBytesForCurrentThread() - _openAlloc;
    }

    /// <summary>Once per drawn frame, after the draw's <see cref="Close"/>.</summary>
    public static void FrameEnd(double elapsedSeconds) {
        if (_frameTicks > _worstFrame)
            _worstFrame = _frameTicks;
        _frameTicks = 0;
        _frames++;
        _sinceLog += elapsedSeconds;
        if (_sinceLog < 5 || _frames < 30)
            return;
        double ms = 1000.0 / Stopwatch.Frequency;
        var sb = new StringBuilder(200);
        sb.Append("PERF ").Append(Scene?.Invoke() ?? "?").Append(": ").Append((_frames / _sinceLog).ToString("0")).Append(" fps");
        double total = 0;
        for (int i = 0; i < _ticks.Length; i++) {
            double v = _ticks[i] * ms / _frames;
            total += v;
            sb.Append(' ').Append((S)i).Append('=').Append(v.ToString("0.00"));
            if (_bytes[i] / _frames >= 512)
                sb.Append('(').Append((_bytes[i] / 1024.0 / _frames).ToString("0.0")).Append("KB)");
            _ticks[i] = 0;
            _bytes[i] = 0;
        }
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1);
        sb.Append(" | total=").Append(total.ToString("0.00")).Append("ms worst=").Append((_worstFrame * ms).ToString("0.0")).Append("ms alloc=")
          .Append((_alloc / 1024.0 / _frames).ToString("0.0")).Append("KB/frame gc0=").Append(g0 - _gen0).Append(" gc1=").Append(g1 - _gen1);
        _gen0 = g0;
        _gen1 = g1;
        _frames = 0;
        _sinceLog = 0;
        _worstFrame = 0;
        _alloc = 0;
        TankGame.ClientLog.Write(sb.ToString(), LogType.Info);
    }
}
