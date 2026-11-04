using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TanksRebirth.Internals;

namespace TanksRebirth.Net.Online;

/// <summary>
/// The Join screen's list of online games: every host keeps an entry in a shared list (refreshed every few seconds),
/// and this reads it now and then. Old entries (a host that closed the app) are skipped.
/// </summary>
public static class OnlineBrowser {
    public sealed record Game(string Code, string Host, int Players, int Max, bool Playing, IPEndPoint? Lan);

    public static IReadOnlyList<Game> Games { get; private set; } = [];
    public static bool Looking { get; private set; }
    public static bool Failed { get; private set; }
    private static FirebaseRest? _fb;
    private static CancellationTokenSource? _cts;

    public static void Start() {
        Stop();
        if (!OnlineSession.Configured)
            return;
        Looking = true;
        Failed = false;
        Games = [];
        _cts = new CancellationTokenSource();
        _fb = new FirebaseRest(OnlineSession.DatabaseUrl);
        var ct = _cts.Token;
        var fb = _fb;
        _ = Task.Run(async () => {
            while (!ct.IsCancellationRequested) {
                try {
                    var data = await fb.GetAsync(OnlineSession.Directory, ct);
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var list = new List<Game>();
                    if (data is { ValueKind: JsonValueKind.Object } d)
                        foreach (var g in d.EnumerateObject()) {
                            var v = g.Value;
                            if (v.ValueKind != JsonValueKind.Object)
                                continue;
                            long t = v.TryGetProperty("t", out var tv) && tv.TryGetInt64(out var tl) ? tl : 0;
                            if (now - t > 45_000) {
                                // a host that's gone: tidy it away after a while
                                if (now - t > 600_000)
                                    _ = fb.DeleteAsync($"{OnlineSession.Directory}/{g.Name}");
                                continue;
                            }
                            string host = v.TryGetProperty("host", out var hv) ? hv.GetString() ?? "Game" : "Game";
                            int n = v.TryGetProperty("n", out var nv) && nv.TryGetInt32(out var ni) ? ni : 1;
                            int max = v.TryGetProperty("max", out var mv) && mv.TryGetInt32(out var mi) ? mi : 4;
                            bool playing = v.TryGetProperty("p", out var pv) && pv.ValueKind == JsonValueKind.True;
                            var lan = v.TryGetProperty("lan", out var lv) && IPEndPoint.TryParse(lv.GetString() ?? "", out var le) ? le : null;
                            list.Add(new Game(g.Name, host, n, max, playing, lan));
                        }
                    Games = list.OrderBy(x => x.Playing).ThenBy(x => x.Host).ToList();
                    Failed = false;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception e) {
                    Failed = true;
                    TankGame.ClientLog.Write($"Online list: {e.Message}", LogType.Warn);
                }
                try { await Task.Delay(3000, ct); } catch { return; }
            }
        }, ct);
    }

    public static void Stop() {
        Looking = false;
        _cts?.Cancel();
        _cts = null;
        var fb = _fb;
        _fb = null;
        if (fb is not null)
            _ = Task.Run(async () => { await Task.Delay(500); fb.Dispose(); });
    }
}
