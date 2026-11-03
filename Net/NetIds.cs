using System.Collections.Generic;
using TanksRebirth.GameContent.Systems.TankSystem;

namespace TanksRebirth.Net;

/// <summary>(from 1.9) Network ids for shells and mines that mean the same object on every device.
/// The old ids were a random byte (shells) or the local array slot (mines), which differ between devices,
/// so the wrong object got destroyed: "ghost" bullets and mines, and points going to the wrong player.
/// An id is the owner's world id plus a running count of that owner's shots on the device that fired them.</summary>
public static class NetIds {
    static readonly int[] _shellCount = new int[256], _mineCount = new int[256];
    static readonly HashSet<int> _dead = [];
    static readonly Queue<int> _deadOrder = new();

    static int Key(Tank? owner) => owner is null ? 255 : owner.WorldId & 0xFF;
    public static int NextShell(Tank? owner) { int k = Key(owner); return (k << 20) | (_shellCount[k]++ & 0xFFFFF); }
    public static int NextMine(Tank? owner) { int k = Key(owner); return (k << 20) | (_mineCount[k]++ & 0xFFFFF); }

    /// <summary>A destroy message arrived for a shell this device doesn't have (yet): remember it,
    /// so the fire message, if it turns up late, doesn't make a shell that lives forever.</summary>
    public static void MarkDead(int id) {
        if (!_dead.Add(id)) return;
        _deadOrder.Enqueue(id);
        while (_deadOrder.Count > 1024) _dead.Remove(_deadOrder.Dequeue());
    }
    public static bool WasDestroyed(int id) => _dead.Contains(id);
}
