using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TanksRebirth.Net.Online;

/// <summary>
/// Carries UDP datagrams one way through the Firebase database: batches of datagrams are written into a
/// small ring of slots, and the other side streams that node. Lost or reordered batches are fine, the game's
/// networking already copes with that (it's built for UDP).
/// </summary>
public sealed class RelayPipe {
    private const int SLOTS = 64;
    private const int MAX_BATCH = 12_000;
    private readonly FirebaseRest _fb;
    private readonly string _sendPath, _recvPath;
    private readonly Action<byte[]> _onReceive;
    private readonly ConcurrentQueue<byte[]> _out = new();
    private readonly SemaphoreSlim _inFlight = new(8);
    private readonly HashSet<long> _seen = [];
    private readonly Queue<long> _seenOrder = new();
    private long _seq;

    public long BytesSent, BytesReceived;

    public RelayPipe(FirebaseRest fb, string sendPath, string recvPath, Action<byte[]> onReceive) {
        _fb = fb;
        _sendPath = sendPath;
        _recvPath = recvPath;
        _onReceive = onReceive;
    }

    public void Send(byte[] datagram) => _out.Enqueue(datagram);

    public void Start(CancellationToken ct) {
        _ = Task.Run(() => SendLoop(ct), ct);
        _ = Task.Run(() => _fb.StreamAsync(_recvPath, OnChange, ct), ct);
    }

    private async Task SendLoop(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try { await Task.Delay(20, ct); } catch { return; }
            while (!_out.IsEmpty) {
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) {
                    while (ms.Length < MAX_BATCH && _out.TryDequeue(out var d)) {
                        bw.Write((ushort)d.Length);
                        bw.Write(d);
                    }
                }
                var seq = Interlocked.Increment(ref _seq);
                var json = $"{{\"s\":{seq},\"d\":\"{Convert.ToBase64String(ms.ToArray())}\"}}";
                Interlocked.Add(ref BytesSent, ms.Length);
                await _inFlight.WaitAsync(ct);
                _ = Task.Run(async () => {
                    try { await _fb.PutAsync($"{_sendPath}/k{seq % SLOTS}", json, ct); }
                    catch { /* a lost batch is like a lost packet */ }
                    finally { _inFlight.Release(); }
                }, ct);
            }
        }
    }

    private void OnChange(string path, JsonElement data) {
        if (path == "/") {
            if (data.ValueKind == JsonValueKind.Object)
                foreach (var child in data.EnumerateObject())
                    Deliver(child.Value);
        }
        else
            Deliver(data);
    }

    private void Deliver(JsonElement slot) {
        if (slot.ValueKind != JsonValueKind.Object || !slot.TryGetProperty("s", out var s) || !slot.TryGetProperty("d", out var d))
            return;
        long seq = s.GetInt64();
        lock (_seen) {
            if (!_seen.Add(seq))
                return;
            _seenOrder.Enqueue(seq);
            while (_seenOrder.Count > 512)
                _seen.Remove(_seenOrder.Dequeue());
        }
        byte[] batch;
        try { batch = Convert.FromBase64String(d.GetString() ?? ""); }
        catch { return; }
        Interlocked.Add(ref BytesReceived, batch.Length);
        int pos = 0;
        while (pos + 2 <= batch.Length) {
            int len = batch[pos] | (batch[pos + 1] << 8);
            pos += 2;
            if (pos + len > batch.Length)
                break;
            var dg = new byte[len];
            Buffer.BlockCopy(batch, pos, dg, 0, len);
            pos += len;
            try { _onReceive(dg); } catch { }
        }
    }
}

/// <summary>
/// Joining phone, relay mode: a local UDP port the game connects to as if it were the host. Everything sent
/// to it goes up through Firebase, and the host's replies come back out of it.
/// </summary>
public sealed class RelayJoinProxy : IDisposable {
    private readonly UdpClient _udp;
    private readonly RelayPipe _pipe;
    private IPEndPoint? _game;
    private readonly CancellationTokenSource _cts = new();
    public int Port { get; }
    public RelayPipe Pipe => _pipe;

    public RelayJoinProxy(FirebaseRest fb, string roomPath, string joinerId) {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;
        _pipe = new RelayPipe(fb, $"{roomPath}/r/{joinerId}/u", $"{roomPath}/r/{joinerId}/d", dg => {
            if (_game is not null)
                try { _udp.Send(dg, dg.Length, _game); } catch { }
        });
        _pipe.Start(_cts.Token);
        _ = Task.Run(ReceiveLoop);
    }

    private async Task ReceiveLoop() {
        while (!_cts.IsCancellationRequested) {
            try {
                var r = await _udp.ReceiveAsync(_cts.Token);
                _game = r.RemoteEndPoint;
                _pipe.Send(r.Buffer);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch { }
        }
    }

    public void Dispose() {
        _cts.Cancel();
        _udp.Dispose();
    }
}

/// <summary>
/// Hosting phone, relay mode: one per relayed player. Their datagrams (from Firebase) are handed to the game's
/// server from a local port, and the server's replies to that port go back through Firebase.
/// </summary>
public sealed class RelayHostPeer : IDisposable {
    private readonly UdpClient _udp;
    private readonly RelayPipe _pipe;
    private readonly IPEndPoint _server;
    private readonly CancellationTokenSource _cts = new();
    public RelayPipe Pipe => _pipe;

    public RelayHostPeer(FirebaseRest fb, string roomPath, string joinerId, int serverPort) {
        _server = new IPEndPoint(IPAddress.Loopback, serverPort);
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _pipe = new RelayPipe(fb, $"{roomPath}/r/{joinerId}/d", $"{roomPath}/r/{joinerId}/u", dg => {
            try { _udp.Send(dg, dg.Length, _server); } catch { }
        });
        _pipe.Start(_cts.Token);
        _ = Task.Run(ReceiveLoop);
    }

    private async Task ReceiveLoop() {
        while (!_cts.IsCancellationRequested) {
            try {
                var r = await _udp.ReceiveAsync(_cts.Token);
                _pipe.Send(r.Buffer);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch { }
        }
    }

    public void Dispose() {
        _cts.Cancel();
        _udp.Dispose();
    }
}
