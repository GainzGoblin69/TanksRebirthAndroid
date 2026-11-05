using System;
using System.Collections.Generic;
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
        return v >= 0 && v < Count && IsUnlocked(v) ? v : DefaultFor(slot);
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

    // ------------------------------------------------------------------ special skins (unlocked by beating things)

    public enum SkinStyle { Camo, Rainbow, Neon, Gold, Chrome }

    /// <summary>
    /// A special tank skin. The player tank model samples its texture at a handful of points (one per part:
    /// turret, deck, lower hull, wheels, fender sides, barrel, muzzle) plus one 32x5 strip that runs along both
    /// fenders, so a skin is a color per part and a pattern for that strip.
    /// </summary>
    public sealed record Skin(string Name, string Unlock, SkinStyle Style, Color Turret, Color Deck, Color Lower, Color Wheels,
        Color Fender, Color Barrel, Color Muzzle, Dictionary<char, Color>? StripColors = null, string[]? Strip = null,
        string Origin = "", string Blurb = "") {
        public bool Animated => Style != SkinStyle.Camo;
    }

    public static readonly Skin[] Skins = [
        new("Type 97", "campaign", SkinStyle.Camo, new(79, 106, 60), new(122, 90, 62), new(94, 106, 72), new(59, 63, 54), new(140, 138, 106), new(74, 80, 64), new(79, 106, 60),
            new() { ['g'] = new(79, 106, 60), ['b'] = new(122, 90, 62), ['s'] = new(140, 138, 106), ['y'] = new(226, 194, 58), ['d'] = new(59, 63, 54) }, ["ggggggbbbbyysssssggggyybbbbbsssg", "ggggggbbbbyysssssggggyybbbbbsssg", "ggggggbbbbyysssssggggyybbbbbsssg", "ggggggbbbbyysssssggggyybbbbbsssg", "ggggggbbbbyysssssggggyybbbbbsssg"],
            Origin: "Japan, 1938", Blurb: "Japan's most common medium tank of WWII, painted green, brown and khaki with a bright yellow band to break up its outline."),
        new("Panzer", "kills1000", SkinStyle.Camo, new(196, 170, 102), new(179, 154, 92), new(93, 107, 58), new(58, 58, 52), new(122, 63, 44), new(156, 135, 84), new(93, 107, 58),
            new() { ['y'] = new(196, 170, 102), ['g'] = new(93, 107, 58), ['r'] = new(122, 63, 44) }, ["yyyyggyyyyrrryyyyyggggyyyrrryyyy", "yyygggyyyrrrryyyyygggyyyyrrryyyy", "yyggggyyyrrryyyyyyyggyyyrrrryyyy", "yyygggyyyyrryyyyyyygggyyyrryyyyy", "yyyyggyyyyyryyyyyyyyggyyyyryyyyy"],
            Origin: "Germany, 1943", Blurb: "Late-war German camouflage: a dark yellow base, sprayed in the field with red-brown and olive green."),
        new("Winter", "levels25", SkinStyle.Camo, new(232, 236, 239), new(223, 228, 232), new(107, 116, 92), new(74, 78, 72), new(184, 190, 194), new(138, 145, 132), new(232, 236, 239),
            new() { ['w'] = new(232, 236, 239), ['g'] = new(107, 116, 92), ['s'] = new(184, 190, 194) }, ["wwwwggwwwwwwsswwwwwggwwwwwwsswww", "wwwwggwwwwwwsswwwwwggwwwwwwsswww", "wwwwggwwwwwwsswwwwwggwwwwwwsswww", "wwwwggwwwwwwsswwwwwggwwwwwwsswww", "wwwwggwwwwwwsswwwwwggwwwwwwsswww"],
            Origin: "Eastern Front, WWII", Blurb: "Crews brushed a temporary lime whitewash over their tanks to vanish into the snow. By spring it had worn off in patches."),
        new("Desert", "troves3", SkinStyle.Camo, new(215, 189, 138), new(201, 171, 118), new(166, 124, 82), new(90, 74, 56), new(234, 220, 180), new(166, 124, 82), new(215, 189, 138),
            new() { ['t'] = new(215, 189, 138), ['b'] = new(166, 124, 82), ['s'] = new(234, 220, 180) }, ["ttttbbbttttsssttttbbbbttttsssstt", "ttttbbbttttsssttttbbbbttttsssstt", "ttttbbbttttsssttttbbbbttttsssstt", "ttttbbbttttsssttttbbbbttttsssstt", "ttttbbbttttsssttttbbbbttttsssstt"],
            Origin: "North Africa", Blurb: "Sand and tan for open desert, where the enemy can spot you from miles away."),
        new("Woodland", "rooms100", SkinStyle.Camo, new(74, 107, 52), new(107, 74, 46), new(38, 38, 34), new(38, 38, 34), new(125, 138, 76), new(60, 74, 44), new(74, 107, 52),
            new() { ['g'] = new(74, 107, 52), ['b'] = new(107, 74, 46), ['k'] = new(38, 38, 34), ['l'] = new(125, 138, 76) }, ["gggkkbbbgggllgggkkkbbggllggkbbgg", "gggkkbbbgggllgggkkkbbggllggkbbgg", "gggkkbbbgggllgggkkkbbggllggkbbgg", "gggkkbbbgggllgggkkkbbggllggkbbgg", "gggkkbbbgggllgggkkkbbggllggkbbgg"],
            Origin: "Cold War", Blurb: "Green, brown and black blotches made to melt into forests and hedgerows."),
        new("Tiger", "secret", SkinStyle.Camo, new(240, 138, 36), new(243, 155, 58), new(28, 26, 24), new(28, 26, 24), new(28, 26, 24), new(240, 138, 36), new(28, 26, 24),
            new() { ['o'] = new(240, 138, 36), ['k'] = new(28, 26, 24) }, ["oookkooookkoooookkooookkoooookko", "ookkooookkoooookkooookkoooookkoo", "okkooookkoooookkooookkoooookkooo", "ookkooookkoooookkooookkoooookkoo", "oookkooookkoooookkooookkoooookko"],
            Origin: "Your own design", Blurb: "Bold orange and black stripes. Not regulation, but nobody forgets it."),
        new("Rainbow", "troves", SkinStyle.Rainbow, new(255, 70, 70), new(255, 200, 60), new(60, 200, 255), new(40, 40, 48), new(120, 255, 120), new(200, 90, 255), new(255, 255, 255),
            Origin: "Tank Troves champion", Blurb: "Every color at once, always moving. Proof you beat Tank Troves."),
        new("Neon", "bosses10", SkinStyle.Neon, new(34, 28, 56), new(28, 24, 46), new(46, 20, 64), new(14, 12, 20), new(0, 240, 255), new(44, 38, 70), new(255, 40, 200),
            Origin: "Boss hunter", Blurb: "A dark hull with light racing along its sides. It glows on dark floors, so the bosses see you coming."),
        new("Gold", "battle", SkinStyle.Gold, new(232, 182, 48), new(214, 164, 36), new(150, 104, 22), new(90, 64, 20), new(246, 206, 92), new(196, 148, 34), new(255, 224, 120),
            Origin: "Battle champion", Blurb: "Solid gold, with a shine that never stops. Only for Battle winners."),
        new("Chrome", "achievements15", SkinStyle.Chrome, new(200, 206, 214), new(176, 184, 194), new(110, 116, 126), new(60, 64, 70), new(226, 232, 238), new(150, 156, 166), new(235, 240, 245),
            Origin: "Achievement hunter", Blurb: "Mirror-polished steel for players who chase every achievement."),
        // famous real tanks
        new("Renault FT", "missions50", SkinStyle.Camo, new(95, 107, 62), new(184, 154, 92), new(110, 82, 54), new(58, 52, 40), new(184, 154, 92), new(58, 58, 48), new(95, 107, 62),
            new() { ['g'] = new(95, 107, 62), ['o'] = new(184, 154, 92), ['b'] = new(110, 82, 54), ['k'] = new(38, 36, 30) }, ["gggkooookbbbbkggggkooookbbbkgggg", "ggkooooookbbbkggggkooooookbbkggg", "gkoooooookbbkggggggkoooookbkgggg", "ggkooooookbbbkggggkoooookbbbkggg", "gggkoooookbbbbkggggkoookbbbbkggg"],
            Origin: "France, 1917", Blurb: "The first tank with the modern layout: engine at the rear, driver at the front and a fully rotating turret. It became the blueprint for most tanks that followed."),
        new("Mark I", "floor1", SkinStyle.Camo, new(110, 113, 72), new(138, 119, 82), new(92, 70, 50), new(60, 50, 38), new(138, 119, 82), new(74, 68, 54), new(110, 113, 72),
            new() { ['k'] = new(138, 119, 82), ['b'] = new(92, 70, 50), ['g'] = new(110, 113, 72), ['p'] = new(160, 140, 128) }, ["kkkkbbbbggggppkkkkbbbgggpppkkkbb", "kkkbbbbggggpppkkkbbbbgggppkkkkbb", "kkbbbbggggppppkkbbbbggggppkkkkbb", "kkkbbbggggpppkkkkbbbgggpppkkkkbb", "kkkkbbgggppppkkkkbbggggppkkkkkbb"],
            Origin: "United Kingdom, 1916", Blurb: "The very first tank to go into battle, in World War I. It brought armored vehicles to the battlefield and shaped early armored warfare."),
        new("Tiger I", "bosses25", SkinStyle.Camo, new(90, 96, 100), new(82, 88, 92), new(62, 66, 70), new(42, 44, 46), new(106, 112, 116), new(90, 96, 100), new(58, 62, 64),
            new() { ['g'] = new(90, 96, 100), ['l'] = new(122, 128, 132), ['w'] = new(232, 232, 232), ['k'] = new(30, 30, 32) }, ["gggggggggggggggggggggggggggggggg", "ggggggwwwwgggggggggggggwwwwggggg", "ggggggwkkwgggggggggggggwkkwggggg", "ggggggwwwwgggggggggggggwwwwggggg", "gggggggggggggggggggggggggggggggg"],
            Origin: "Germany, WWII", Blurb: "Famous for its thick armor, 88 mm gun and advanced suspension. A feared opponent that influenced later heavy tank design."),
        new("T-34", "kills5000", SkinStyle.Camo, new(78, 98, 52), new(74, 92, 48), new(62, 78, 42), new(46, 52, 34), new(78, 98, 52), new(68, 84, 46), new(78, 98, 52),
            new() { ['g'] = new(78, 98, 52), ['d'] = new(62, 78, 42), ['w'] = new(236, 234, 224) }, ["gggggggggggggggggggggggggggggggg", "ggggggggwwwwwwwwwwwwwwwwgggggggg", "ggggggggddddddddddddddddgggggggg", "ggggggggwwwwwwwwwwwwwwwwgggggggg", "gggggggggggggggggggggggggggggggg"],
            Origin: "Soviet Union, WWII", Blurb: "Sloped armor, a 76.2 mm gun and a simple design that could be built in huge numbers made it one of the most effective and influential tanks ever built."),
        new("M1 Abrams", "troves5", SkinStyle.Camo, new(196, 170, 128), new(187, 160, 119), new(168, 142, 102), new(58, 54, 48), new(208, 184, 142), new(179, 154, 114), new(58, 54, 48),
            new() { ['t'] = new(196, 170, 128), ['d'] = new(168, 142, 102), ['k'] = new(58, 54, 48) }, ["tttttttttttttttttttttttttttttttt", "ttttddddttttttttddddtttttttddddt", "tttttttttttttttttttttttttttttttt", "tttttttttttttttttttttttttttttttt", "dddddddddddddddddddddddddddddddd"],
            Origin: "United States, 1980s", Blurb: "A modern powerhouse with composite armor, a gas turbine engine, a 120 mm smoothbore gun and advanced optics."),
        new("Leopard 2", "battle5", SkinStyle.Camo, new(76, 88, 52), new(94, 70, 50), new(34, 34, 30), new(34, 34, 30), new(76, 88, 52), new(76, 88, 52), new(34, 34, 30),
            new() { ['g'] = new(76, 88, 52), ['b'] = new(94, 70, 50), ['k'] = new(34, 34, 30) }, ["ggggbbbbbkkkggggggbbbbkkkkggggbb", "gggbbbbbkkkggggggbbbbkkkkggggbbb", "ggbbbbbkkkggggggbbbbkkkkggggbbbb", "gbbbbbkkkggggggbbbbkkkkggggbbbbb", "bbbbbkkkggggggbbbbkkkkggggbbbbbg"],
            Origin: "Germany, late 1970s", Blurb: "A balance of firepower, protection and mobility, with composite armor, a 120 mm smoothbore gun and advanced fire control."),
        new("Challenger 2", "rooms500", SkinStyle.Camo, new(60, 72, 48), new(54, 66, 40), new(28, 30, 26), new(28, 30, 26), new(60, 72, 48), new(60, 72, 48), new(28, 30, 26),
            new() { ['g'] = new(60, 72, 48), ['k'] = new(28, 30, 26) }, ["gggggkkkkgggggggkkkkkggggggkkkgg", "ggggkkkkggggggggkkkkggggggkkkkgg", "gggkkkkggggggggkkkkggggggkkkkggg", "ggkkkkgggggggggkkkkgggggggkkkggg", "gkkkkggggggggggkkkggggggggkkgggg"],
            Origin: "United Kingdom, 1990s", Blurb: "Known for its Chobham armor, 120 mm rifled gun and high accuracy, and effective in very different environments."),
        new("Merkava IV", "secrets5", SkinStyle.Camo, new(143, 140, 114), new(134, 131, 106), new(118, 115, 92), new(58, 58, 50), new(154, 151, 124), new(134, 131, 106), new(44, 44, 38),
            new() { ['s'] = new(143, 140, 114), ['d'] = new(118, 115, 92), ['k'] = new(44, 44, 38) }, ["ssssssssssssssssssssssssssssssss", "sdsdsdsdsdsdsdsdsdsdsdsdsdsdsdsd", "ssssssssssssssssssssssssssssssss", "dsdsdsdsdsdsdsdsdsdsdsdsdsdsdsds", "kkkkkkkkkkkkkkkkkkkkkkkkkkkkkkkk"],
            Origin: "Israel, 2000s", Blurb: "Built around keeping its crew alive, with modular composite armor, an engine at the front and advanced electronics."),
        new("Centurion", "achievements30", SkinStyle.Camo, new(52, 64, 42), new(48, 58, 38), new(38, 46, 30), new(30, 34, 26), new(70, 84, 58), new(52, 64, 42), new(38, 46, 30),
            new() { ['g'] = new(52, 64, 42), ['l'] = new(70, 84, 58), ['w'] = new(230, 226, 214), ['r'] = new(176, 48, 42) }, ["gggggggggggggggggggggggggggggggg", "gggglllllllllllllllllllllllggggg", "ggggwwrrwwgggggggggggggwwrrwwggg", "gggglllllllllllllllllllllllggggg", "gggggggggggggggggggggggggggggggg"],
            Origin: "United Kingdom, 1945", Blurb: "A versatile, long-lived design upgraded again and again, which set the standard for modern tanks."),
        new("T-90", "missions100", SkinStyle.Camo, new(90, 106, 62), new(176, 156, 104), new(42, 42, 36), new(42, 42, 36), new(90, 106, 62), new(90, 106, 62), new(42, 42, 36),
            new() { ['g'] = new(90, 106, 62), ['s'] = new(176, 156, 104), ['k'] = new(42, 42, 36) }, ["ggsssskkggggsssggggkkksssgggkkgg", "gsssskkggggsssggggkkksssgggkkggg", "sssskkggggsssggggkkksssgggkkgggs", "gsssskkggggsssggggkkksssgggkkggg", "ggsssskkggggsssggggkkksssgggkkgg"],
            Origin: "Russia, 1990s", Blurb: "Combines newer technology with a proven design, with a 125 mm gun and improved survivability."),
    ];

    /// <summary>Plain colors and special skins together: indexes past the palette are skins.</summary>
    public static int Count => Palette.Length + Skins.Length;
    public static Skin? SkinAt(int index) => index >= Palette.Length && index < Count ? Skins[index - Palette.Length] : null;
    public static bool IsAnimated(int index) => SkinAt(index)?.Animated == true;
    public static bool IsUnlocked(int index) => index >= 0 && index < Count && (SkinAt(index) is not { } sk || Unlocks.Has(sk.Unlock));
    public static string NameOf(int index) => SkinAt(index)?.Name ?? (index >= 0 && index < Palette.Length ? Palette[index].Name : "");

    /// <summary>The color that stands for this choice (explosion bits, outlines, name tags).</summary>
    public static Color MainOf(int index) {
        if (SkinAt(index) is { } sk)
            return sk.Style switch {
                SkinStyle.Rainbow => HsvToColor((float)(DateTime.UtcNow.TimeOfDay.TotalSeconds * 0.25 % 1.0), 0.8f, 1f),
                SkinStyle.Neon => sk.Fender,
                _ => sk.Turret,
            };
        return index >= 0 && index < Palette.Length ? Palette[index].Main : Color.White;
    }

    /// <summary>A light this skin gives off in the dark (null = none).</summary>
    public static Color? GlowOf(int index) => SkinAt(index)?.Style switch {
        SkinStyle.Neon => Color.Lerp(new Color(0, 240, 255), new Color(255, 40, 200), 0.5f + 0.5f * MathF.Sin(Time * 2f)),
        SkinStyle.Rainbow => MainOf(index),
        _ => null,
    };

    private static float Time => (float)(DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600.0);

    /// <summary>A texture for any choice (plain color or skin).</summary>
    public static Texture2D MakeTexture(GraphicsDevice gd, int index) {
        if (SkinAt(index) is null)
            return MakeTexture(gd, Palette[Math.Clamp(index, 0, Palette.Length - 1)]);
        var tex = new Texture2D(gd, 32, 32);
        Animate(tex, index);
        return tex;
    }

    private static readonly Color[] _skinData = new Color[32 * 32];
    /// <summary>Redraws a skin texture (animated skins call this every frame).</summary>
    public static void Animate(Texture2D tex, int index) {
        if (SkinAt(index) is not { } sk || tex.IsDisposed)
            return;
        lock (_skinData) {
            FillSkin(_skinData, sk, Time);
            tex.SetData(_skinData);
        }
    }

    private static void FillSkin(Color[] d, Skin sk, float t) {
        Color turret = sk.Turret, deck = sk.Deck, lower = sk.Lower, wheels = sk.Wheels, fender = sk.Fender, barrel = sk.Barrel, muzzle = sk.Muzzle;
        if (sk.Style == SkinStyle.Rainbow) {
            float h = t * 0.25f;
            turret = HsvToColor(h, 0.75f, 1f);
            deck = HsvToColor(h + 0.12f, 0.7f, 1f);
            lower = HsvToColor(h + 0.3f, 0.75f, 0.85f);
            fender = HsvToColor(h + 0.5f, 0.75f, 1f);
            barrel = HsvToColor(h + 0.65f, 0.7f, 0.95f);
            muzzle = HsvToColor(h + 0.8f, 0.6f, 1f);
        }
        else if (sk.Style == SkinStyle.Neon) {
            float pulse = 0.75f + 0.25f * MathF.Sin(t * 4f);
            fender = Color.Lerp(Color.Black, sk.Fender, pulse);
            muzzle = Color.Lerp(Color.Black, sk.Muzzle, 0.6f + 0.4f * pulse);
        }
        // everything starts as the deck color, then the fender strip and each part's sample point
        for (int i = 0; i < d.Length; i++)
            d[i] = deck;
        for (int y = 4; y <= 8; y++)
            for (int x = 0; x < 32; x++)
                d[y * 32 + x] = StripPixel(sk, x, y - 4, t);
        Part(d, 6, 5, turret, false);
        Part(d, 12, 14, deck, true);
        Part(d, 5, 12, lower, true);
        Part(d, 15, 15, wheels, true);
        Part(d, 5, 5, fender, false);
        Part(d, 4, 3, fender, false);
        Part(d, 4, 29, barrel, true);
        Part(d, 8, 11, muzzle, true);
        Part(d, 14, 5, muzzle, false);
    }

    private static void Part(Color[] d, int x, int y, Color c, bool block) {
        if (!block) {
            d[y * 32 + x] = c;
            return;
        }
        for (int yy = Math.Max(0, y - 1); yy <= Math.Min(31, y + 1); yy++)
            for (int xx = Math.Max(0, x - 1); xx <= Math.Min(31, x + 1); xx++)
                d[yy * 32 + xx] = c;
    }

    private static Color StripPixel(Skin sk, int x, int row, float t) {
        switch (sk.Style) {
            case SkinStyle.Rainbow:
                return HsvToColor(t * 0.4f + x / 32f, 0.8f, 1f);
            case SkinStyle.Neon: {
                // light running along the fenders, cyan into pink
                float wave = 0.5f + 0.5f * MathF.Sin(x * 0.55f - t * 6f);
                var c = Color.Lerp(new Color(0, 240, 255), new Color(255, 40, 200), wave);
                return row is 0 or 4 ? Color.Lerp(c, Color.Black, 0.55f) : c;
            }
            case SkinStyle.Gold:
            case SkinStyle.Chrome: {
                // a shine sweeping along the fenders
                float at = t * 18f % 56f - 12f;
                float shine = MathF.Max(0f, 1f - MathF.Abs(x - at + row * 0.8f) / 3f);
                return Color.Lerp(sk.Fender, Color.White, shine * 0.85f);
            }
        }
        if (sk.Strip is { } strip && sk.StripColors is { } cols && cols.TryGetValue(strip[row][x], out var col))
            return col;
        return sk.Fender;
    }

    public static Color HsvToColor(float h, float s, float v) {
        h = (h % 1f + 1f) % 1f * 6f;
        int i = (int)h;
        float f = h - i, p = v * (1 - s), q = v * (1 - s * f), u = v * (1 - s * (1 - f));
        var (r, g, b) = i switch { 0 => (v, u, p), 1 => (q, v, p), 2 => (p, v, u), 3 => (p, q, v), 4 => (u, p, v), _ => (v, p, q) };
        return new Color(r, g, b);
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
