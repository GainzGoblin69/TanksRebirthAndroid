using System;
using System.IO;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.ID;
using TanksRebirth.Internals;
using TanksRebirth.Net;

namespace TanksRebirth.GameContent.Systems;

/// <summary>
/// Player tank colors chosen in the Tank Color screen. The player tank texture is just two shades (a dark band
/// top and bottom, the main color in the middle), so any color can be made at runtime.
/// </summary>
public static class PlayerColors {
    public sealed record TankColor(string Name, Color Main, Color Dark);

    public static readonly TankColor[] Palette = [
        new("Blue", new(0, 107, 156), new(0, 58, 115)),
        new("Red", new(197, 53, 49), new(140, 49, 49)),
        new("Green", new(27, 168, 22), new(28, 111, 25)),
        new("Yellow", new(202, 209, 23), new(144, 149, 14)),
        new("Orange", new(232, 122, 24), new(160, 76, 14)),
        new("Purple", new(132, 72, 204), new(82, 40, 138)),
        new("Pink", new(236, 108, 172), new(170, 60, 116)),
        new("Cyan", new(32, 198, 210), new(14, 128, 144)),
        new("Mint", new(124, 222, 160), new(70, 150, 104)),
        new("Brown", new(152, 102, 56), new(100, 64, 34)),
        new("White", new(236, 232, 222), new(168, 162, 152)),
        new("Black", new(64, 64, 70), new(30, 30, 34)),
    ];

    /// <summary>Palette index for a player id when nothing is picked (Blue, Red, Green, Yellow).</summary>
    public static int DefaultFor(int playerId) => Math.Clamp(playerId, 0, 3);

    /// <summary>The palette index chosen for local player slot 0 or 1.</summary>
    public static int GetChoice(int slot) {
        int v = slot == 0 ? TankGame.Settings.TankColorP1 : TankGame.Settings.TankColorP2;
        return v >= 0 && v < Palette.Length ? v : DefaultFor(slot);
    }

    public static void SetChoice(int slot, int index) {
        if (slot == 0)
            TankGame.Settings.TankColorP1 = index;
        else
            TankGame.Settings.TankColorP2 = index;
        SaveSettings();
    }

    public static void SaveSettings() {
        try {
            File.WriteAllText(Path.Combine(TankGame.SaveDirectory, "settings.json"),
                JsonSerializer.Serialize(TankGame.Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) {
            TankGame.ClientLog.Write($"Couldn't save settings: {e.Message}", LogType.Warn);
        }
    }

    /// <summary>
    /// The palette index this player tank should use on this device, or -1 for its standard texture.
    /// Offline (and same-device co-op) players 1 and 2 use their picks; online, only your own tank does.
    /// </summary>
    public static int ColorIndexFor(int playerId) {
        if (Client.IsConnected())
            return playerId == NetPlay.GetMyClientId() ? GetChoice(0) : -1;
        if (playerId is 0 or 1)
            return GetChoice(playerId);
        return -1;
    }

    public static Texture2D MakeTexture(GraphicsDevice gd, TankColor c) {
        var data = new Color[32 * 32];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                data[y * 32 + x] = y is >= 9 and <= 22 ? c.Main : c.Dark;
        var tex = new Texture2D(gd, 32, 32);
        tex.SetData(data);
        return tex;
    }
}
