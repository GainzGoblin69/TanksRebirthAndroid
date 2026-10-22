using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TanksRebirth.Net.Online;

/// <summary>
/// Asks a public STUN server what address and port this phone's traffic appears to come from on the
/// internet (its router's public address). Used for direct connections between phones.
/// </summary>
public static class Stun {
    public static readonly (string Host, int Port)[] Servers = [
        ("stun.l.google.com", 19302), ("stun1.l.google.com", 19302), ("stun.cloudflare.com", 3478)
    ];

    /// <summary>The public endpoint for UDP traffic from <paramref name="localPort"/>, or null if no STUN server answered.</summary>
    public static async Task<IPEndPoint?> QueryAsync(int localPort, int timeoutMs = 1500) {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
        foreach (var (host, port) in Servers) {
            try {
                var addrs = await Dns.GetHostAddressesAsync(host);
                var addr = Array.Find(addrs, a => a.AddressFamily == AddressFamily.InterNetwork);
                if (addr is null)
                    continue;
                var request = new byte[20];
                request[1] = 0x01; // binding request
                request[4] = 0x21; request[5] = 0x12; request[6] = 0xA4; request[7] = 0x42; // magic cookie
                var id = new byte[12];
                Random.Shared.NextBytes(id);
                Buffer.BlockCopy(id, 0, request, 8, 12);
                for (int attempt = 0; attempt < 2; attempt++) {
                    await udp.SendAsync(request, request.Length, new IPEndPoint(addr, port));
                    var recv = udp.ReceiveAsync();
                    if (await Task.WhenAny(recv, Task.Delay(timeoutMs)) != recv)
                        continue;
                    var ep = Parse(recv.Result.Buffer, id);
                    if (ep is not null)
                        return ep;
                }
            }
            catch { /* try the next server */ }
        }
        return null;
    }

    private static IPEndPoint? Parse(byte[] b, byte[] id) {
        if (b.Length < 20 || b[0] != 0x01 || b[1] != 0x01)
            return null;
        for (int i = 0; i < 12; i++)
            if (b[8 + i] != id[i])
                return null;
        int pos = 20;
        while (pos + 4 <= b.Length) {
            int type = (b[pos] << 8) | b[pos + 1];
            int len = (b[pos + 2] << 8) | b[pos + 3];
            int v = pos + 4;
            if (v + len > b.Length)
                break;
            if ((type == 0x0020 || type == 0x0001) && len >= 8 && b[v + 1] == 0x01) {
                int p = (b[v + 2] << 8) | b[v + 3];
                var ip = new byte[] { b[v + 4], b[v + 5], b[v + 6], b[v + 7] };
                if (type == 0x0020) { // XOR-MAPPED-ADDRESS
                    p ^= 0x2112;
                    ip[0] ^= 0x21; ip[1] ^= 0x12; ip[2] ^= 0xA4; ip[3] ^= 0x42;
                }
                return new IPEndPoint(new IPAddress(ip), p);
            }
            pos = v + ((len + 3) & ~3);
        }
        return null;
    }
}
