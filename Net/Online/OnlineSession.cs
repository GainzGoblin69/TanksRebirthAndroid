using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib.Utils;
using TanksRebirth.Internals;

namespace TanksRebirth.Net.Online;

/// <summary>
/// Online games with a 6-digit code. Firebase (Realtime Database) is only the meeting point: the host puts
/// its addresses under the code, a joining phone adds its own, and both try to reach each other directly
/// (UDP hole punching). If the networks don't allow that, the game's packets travel through Firebase instead
/// (slower, but works anywhere). After that it's the normal multiplayer code.
/// </summary>
public static class OnlineSession {
    /// <summary>The Firebase Realtime Database URL (set up in the Firebase console).</summary>
    public const string DatabaseUrl = "";

    public static bool Configured => !string.IsNullOrWhiteSpace(DatabaseUrl);

    public enum Link { None, Direct, Relay }

    public static bool Active { get; private set; }
    public static bool IsHost { get; private set; }
    public static bool Busy { get; private set; }
    public static string? Code { get; private set; }
    public static string Status { get; private set; } = "";
    public static string? Error { get; private set; }
    public static Link Connection { get; private set; }

    private static FirebaseRest? _fb;
    private static CancellationTokenSource? _cts;
    private static string? _joinerId;
    private static int _port;
    private static readonly ConcurrentQueue<Action> _main = new();
    private static readonly Dictionary<string, RelayHostPeer> _relayPeers = [];
    private static readonly HashSet<string> _punching = [];
    private static RelayJoinProxy? _proxy;

    private static string Room => $"rooms/{Code}";

    /// <summary>Runs queued work on the game thread; call once per frame.</summary>
    public static void Update() {
        while (_main.TryDequeue(out var a)) {
            try { a(); }
            catch (Exception e) { TankGame.ClientLog.Write($"Online: {e}", LogType.Warn); }
        }
    }

    private static Task OnMain(Action a) {
        var tcs = new TaskCompletionSource();
        _main.Enqueue(() => {
            try { a(); tcs.SetResult(); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task;
    }

    private static void Begin(bool host) {
        Leave();
        Active = true;
        IsHost = host;
        Busy = true;
        Error = null;
        Connection = Link.None;
        _cts = new CancellationTokenSource();
        _fb = new FirebaseRest(DatabaseUrl);
        _port = Random.Shared.Next(20000, 60000);
    }

    private static void Fail(string message) {
        Error = message;
        Status = message;
        Busy = false;
        TankGame.ClientLog.Write($"Online: {message}", LogType.Warn);
    }

    private static string Ep(IPEndPoint? ep) => ep is null ? "" : $"{ep.Address}:{ep.Port}";
    private static IPEndPoint? ParseEp(string? s) => !string.IsNullOrEmpty(s) && IPEndPoint.TryParse(s, out var ep) ? ep : null;
    private static string LanEp() {
        var ip = LanSession.LocalAddress();
        return IPAddress.TryParse(ip, out _) ? $"{ip}:{_port}" : "";
    }
    private static string J(string s) => JsonSerializer.Serialize(s);

    // ------------------------------------------------------------------ host

    public static void Host() {
        Begin(host: true);
        Status = "Getting a code...";
        var ct = _cts!.Token;
        _ = Task.Run(async () => {
            try {
                var pub = await Stun.QueryAsync(_port);
                await OnMain(() => {
                    Server.CreateServer();
                    Server.NetManager.UnconnectedMessagesEnabled = true;
                    NetPlay.ServerName = $"{LanSession.PlayerName}'s game";
                    Server.StartServer(NetPlay.ServerName, _port, "0.0.0.0", string.Empty);
                    Client.CreateClient(LanSession.PlayerName);
                    Client.AttemptConnectionTo("127.0.0.1", _port, string.Empty);
                    Server.ConnectedClients[0] = NetPlay.CurrentClient;
                });

                for (int attempt = 0; attempt < 6 && Code is null; attempt++) {
                    var code = Random.Shared.Next(100000, 1000000).ToString();
                    var existing = await _fb!.GetAsync($"rooms/{code}", ct);
                    if (existing is null)
                        Code = code;
                }
                if (Code is null) {
                    Fail("Couldn't get a code. Try again.");
                    return;
                }
                await _fb!.PutAsync(Room, $"{{\"host\":{J(LanSession.PlayerName)},\"pub\":{J(Ep(pub))},\"lan\":{J(LanEp())},\"v\":1,\"t\":{{\".sv\":\"timestamp\"}}}}", ct);
                Status = pub is null ? "Code ready (others may connect through the relay)" : "Code ready";
                Busy = false;
                Connection = Link.Direct;
                _ = _fb.StreamAsync($"{Room}/j", OnJoinerChange, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception e) {
                Fail("Couldn't reach the online service. Check your internet connection.");
                TankGame.ClientLog.Write($"Online host failed: {e}", LogType.Warn);
            }
        }, ct);
    }

    private static void OnJoinerChange(string path, JsonElement data) {
        // "/" with every joiner, "/<id>" with one joiner, or "/<id>/relay" with one field
        if (path == "/") {
            if (data.ValueKind == JsonValueKind.Object)
                foreach (var j in data.EnumerateObject())
                    HandleJoiner(j.Name, j.Value);
            return;
        }
        var parts = path.Trim('/').Split('/');
        if (parts.Length == 1)
            HandleJoiner(parts[0], data);
        else if (parts.Length == 2 && parts[1] == "relay" && data.ValueKind == JsonValueKind.True)
            StartRelayPeer(parts[0]);
    }

    private static void HandleJoiner(string id, JsonElement j) {
        if (j.ValueKind != JsonValueKind.Object)
            return;
        if (j.TryGetProperty("relay", out var relay) && relay.ValueKind == JsonValueKind.True)
            StartRelayPeer(id);
        lock (_punching) {
            if (!_punching.Add(id))
                return;
        }
        var targets = new List<IPEndPoint>();
        if (j.TryGetProperty("pub", out var p) && ParseEp(p.GetString()) is { } pubEp) targets.Add(pubEp);
        if (j.TryGetProperty("lan", out var l) && ParseEp(l.GetString()) is { } lanEp) targets.Add(lanEp);
        var ct = _cts?.Token ?? CancellationToken.None;
        // open this side's router toward the joiner while they knock (hole punching)
        _ = Task.Run(async () => {
            for (int i = 0; i < 40 && !ct.IsCancellationRequested; i++) {
                await OnMain(() => {
                    var w = new NetDataWriter();
                    w.Put("TanksRebirth-punch");
                    foreach (var t in targets)
                        Server.NetManager?.SendUnconnectedMessage(w, t);
                });
                try { await Task.Delay(250, ct); } catch { return; }
            }
        }, ct);
    }

    private static void StartRelayPeer(string id) {
        lock (_relayPeers) {
            if (_relayPeers.ContainsKey(id) || _fb is null || Code is null)
                return;
            _relayPeers[id] = new RelayHostPeer(_fb, Room, id, _port);
        }
        TankGame.ClientLog.Write($"Online: relaying player {id}", LogType.Info);
    }

    // ------------------------------------------------------------------ join

    public static void Join(string code) {
        Begin(host: false);
        Code = code;
        Status = "Looking up the code...";
        var ct = _cts!.Token;
        _ = Task.Run(async () => {
            try {
                var room = await _fb!.GetAsync(Room, ct);
                if (room is null || room.Value.ValueKind != JsonValueKind.Object) {
                    Fail($"No game found with code {code}.");
                    return;
                }
                var hostPub = room.Value.TryGetProperty("pub", out var hp) ? ParseEp(hp.GetString()) : null;
                var hostLan = room.Value.TryGetProperty("lan", out var hl) ? ParseEp(hl.GetString()) : null;

                Status = "Connecting...";
                var myPub = await Stun.QueryAsync(_port);
                _joinerId = Guid.NewGuid().ToString("N")[..10];
                await _fb.PutAsync($"{Room}/j/{_joinerId}", $"{{\"pub\":{J(Ep(myPub))},\"lan\":{J(LanEp())},\"relay\":false}}", ct);

                // same router (same public address): use the host's local address
                var target = hostPub is not null && myPub is not null && hostPub.Address.Equals(myPub.Address) && hostLan is not null ? hostLan : hostPub ?? hostLan;
                if (target is not null) {
                    await OnMain(() => {
                        Client.CreateClient(LanSession.PlayerName, _port);
                        var w = new NetDataWriter();
                        w.Put("TanksRebirth-punch");
                        Client.ClientManager.SendUnconnectedMessage(w, target);
                        Client.AttemptConnectionTo(target.Address.ToString(), target.Port, string.Empty);
                    });
                    if (await WaitConnected(TimeSpan.FromSeconds(6), ct)) {
                        Connection = Link.Direct;
                        Status = "Connected";
                        Busy = false;
                        return;
                    }
                }

                // direct didn't work (or no address): go through the relay
                Status = "Connecting through the relay...";
                await OnMain(() => {
                    try { Client.ClientManager?.DisconnectAll(); Client.ClientManager?.Stop(); } catch { }
                    Client.NetClient = null!;
                    NetPlay.CurrentClient = null!;
                });
                _proxy = new RelayJoinProxy(_fb, Room, _joinerId);
                await _fb.PatchAsync($"{Room}/j/{_joinerId}", "{\"relay\":true}", ct);
                await OnMain(() => {
                    Client.CreateClient(LanSession.PlayerName);
                    Client.AttemptConnectionTo("127.0.0.1", _proxy.Port, string.Empty);
                });
                if (await WaitConnected(TimeSpan.FromSeconds(15), ct)) {
                    Connection = Link.Relay;
                    Status = "Connected (relay)";
                    Busy = false;
                    return;
                }
                Fail("Couldn't connect to that game. Ask the host to check their connection.");
            }
            catch (OperationCanceledException) { }
            catch (Exception e) {
                Fail("Couldn't reach the online service. Check your internet connection.");
                TankGame.ClientLog.Write($"Online join failed: {e}", LogType.Warn);
            }
        }, ct);
    }

    private static async Task<bool> WaitConnected(TimeSpan timeout, CancellationToken ct) {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until) {
            if (Client.IsConnected())
                return true;
            await Task.Delay(100, ct);
        }
        return Client.IsConnected();
    }

    // ------------------------------------------------------------------ leave

    /// <summary>Stop hosting / leave, and remove this game's entries from Firebase.</summary>
    public static void Leave() {
        if (!Active)
            return;
        Active = false;
        Busy = false;
        var fb = _fb;
        var room = Code is null ? null : Room;
        var joiner = _joinerId;
        bool host = IsHost;
        _cts?.Cancel();
        lock (_relayPeers) {
            foreach (var p in _relayPeers.Values) p.Dispose();
            _relayPeers.Clear();
        }
        lock (_punching) _punching.Clear();
        _proxy?.Dispose();
        _proxy = null;
        LanSession.Leave();
        if (fb is not null && room is not null)
            _ = Task.Run(async () => {
                try {
                    if (host) await fb.DeleteAsync(room);
                    else if (joiner is not null) await fb.DeleteAsync($"{room}/j/{joiner}");
                    if (!host && joiner is not null) await fb.DeleteAsync($"{room}/r/{joiner}");
                }
                catch { }
                finally { fb.Dispose(); }
            });
        _fb = null;
        Code = null;
        _joinerId = null;
        Connection = Link.None;
        Status = "";
    }
}
