using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using TanksRebirth.Internals;

namespace TanksRebirth.Net;

/// <summary>
/// Wi-Fi games between phones on the same network: one phone hosts (the game's normal server, plus a reply to
/// "who's hosting?" broadcasts) and the others find it in a list and join, with no IP typing. The existing
/// multiplayer code does the rest.
/// </summary>
public static class LanSession {
    public const int Port = 7777;
    private const string ASK = "TanksRebirth?LAN1";
    private const string REPLY = "TanksRebirth!LAN1";

    /// <summary>Device model, filled in by the Android launcher; used for default names.</summary>
    public static string DeviceName { get; set; } = "Phone";

    public sealed record FoundGame(string Name, int Players, int MaxPlayers, IPEndPoint EndPoint, DateTime LastSeen);
    public static readonly List<FoundGame> Found = [];

    public static bool Searching { get; private set; }
    private static DateTime _lastBroadcast = DateTime.MinValue;

    public static string PlayerName {
        get {
            var n = TankGame.Settings?.LanName;
            return string.IsNullOrWhiteSpace(n) ? DeviceName : n!;
        }
    }

    /// <summary>Start hosting a Wi-Fi game; this phone joins its own game as player 1.</summary>
    public static bool Host() {
        try {
            Server.CreateServer();
            Server.NetManager.BroadcastReceiveEnabled = true;
            Server.NetManager.UnconnectedMessagesEnabled = true;
            Server.NetListener.NetworkReceiveUnconnectedEvent += OnServerUnconnected;

            NetPlay.ServerName = $"{PlayerName}'s game";
            Server.StartServer(NetPlay.ServerName, Port, "0.0.0.0", string.Empty);

            Client.CreateClient(PlayerName);
            Client.AttemptConnectionTo("127.0.0.1", Port, string.Empty);
            Server.ConnectedClients[0] = NetPlay.CurrentClient;
            return true;
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Couldn't host a Wi-Fi game: {e}", LogType.Warn);
            Leave();
            return false;
        }
    }

    private static void OnServerUnconnected(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type) {
        try {
            if (reader.AvailableBytes <= 0 || reader.GetString() != ASK)
                return;
            var w = new NetDataWriter();
            w.Put(REPLY);
            w.Put(NetPlay.ServerName ?? "Tanks game");
            w.Put(Server.CurrentClientCount);
            w.Put((int)Server.MaxClients);
            Server.NetManager?.SendUnconnectedMessage(w, remote);
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"LAN discovery reply failed: {e.Message}", LogType.Warn);
        }
        finally {
            reader.Recycle();
        }
    }

    /// <summary>Start looking for hosted games (call <see cref="Update"/> every frame while searching).</summary>
    public static void StartSearch() {
        Found.Clear();
        if (NetPlay.CurrentClient is null || Client.ClientManager is null) {
            Client.CreateClient(PlayerName);
            Client.ClientManager.UnconnectedMessagesEnabled = true;
            Client.ClientListener.NetworkReceiveUnconnectedEvent += OnClientUnconnected;
        }
        Searching = true;
        _lastBroadcast = DateTime.MinValue;
    }

    public static void StopSearch() => Searching = false;

    private static void OnClientUnconnected(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type) {
        try {
            if (reader.AvailableBytes <= 0 || reader.GetString() != REPLY)
                return;
            var name = reader.GetString();
            var players = reader.GetInt();
            var max = reader.GetInt();
            Found.RemoveAll(g => g.EndPoint.Equals(remote));
            Found.Add(new FoundGame(name, players, max, remote, DateTime.UtcNow));
        }
        catch { /* not one of ours */ }
        finally {
            reader.Recycle();
        }
    }

    public static void Update() {
        if (!Searching || Client.ClientManager is null)
            return;
        Found.RemoveAll(g => (DateTime.UtcNow - g.LastSeen).TotalSeconds > 5);
        if ((DateTime.UtcNow - _lastBroadcast).TotalSeconds < 1)
            return;
        _lastBroadcast = DateTime.UtcNow;
        try {
            var w = new NetDataWriter();
            w.Put(ASK);
            Client.ClientManager.SendBroadcast(w, Port);
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"LAN broadcast failed: {e.Message}", LogType.Warn);
        }
    }

    public static void Join(string address, int port = Port) {
        StopSearch();
        if (NetPlay.CurrentClient is null || Client.ClientManager is null)
            Client.CreateClient(PlayerName);
        Client.AttemptConnectionTo(address, port, string.Empty);
    }

    /// <summary>Leave (or stop hosting) and forget the network session.</summary>
    public static void Leave() {
        StopSearch();
        try {
            if (Client.NetClient is not null)
                Client.Disconnect();
            else
                NetPlay.TryUnmapServerNetworking();
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Leaving the Wi-Fi game: {e.Message}", LogType.Warn);
        }
        try { Client.ClientManager?.Stop(); } catch { }
        if (Client.ClientListener is not null)
            Client.ClientListener.NetworkReceiveUnconnectedEvent -= OnClientUnconnected;
        Client.ClientManager = null!;
        Client.NetClient = null!;
        NetPlay.CurrentClient = null!;
        NetPlay.CurrentServer = null!;
        Server.NetManager = null!;
        Server.ConnectedClients = null!;
        Server.CurrentClientCount = 0;
        Found.Clear();
    }

    /// <summary>This phone's address on the Wi-Fi network (for joining by typing it in).</summary>
    public static string LocalAddress() {
        try {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses) {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a.Address))
                        continue;
                    var s = a.Address.ToString();
                    if (s.StartsWith("192.168.") || s.StartsWith("10.") || s.StartsWith("172."))
                        return s;
                }
            }
            var any = Dns.GetHostAddresses(Dns.GetHostName()).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return any?.ToString() ?? "unknown";
        }
        catch {
            return "unknown";
        }
    }
}
