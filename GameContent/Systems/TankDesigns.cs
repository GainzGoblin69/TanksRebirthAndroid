using System.Collections.Generic;
using Microsoft.Xna.Framework;
using TanksRebirth.GameContent.Systems.Troves;
using static TanksRebirth.GameContent.Systems.TankPaint;

namespace TanksRebirth.GameContent.Systems;

/// <summary>The paint jobs: one for each special player skin (by its name), and one for each Tank Troves boss.</summary>
public static class TankDesigns {
    private static Color C(int r, int g, int b) => new(r, g, b);

    private static readonly Dictionary<string, Design> _player = new() {
        // WWII and later camouflage
        ["Type 97"] = new(Scheme.Outlined, C(84, 110, 62), C(128, 92, 60), C(150, 146, 108), C(226, 194, 58), Mark.Sun, Scale: 1.1f, Barrel: C(74, 80, 64)),
        ["Panzer"] = new(Scheme.SoftBlotch, C(200, 174, 106), C(124, 66, 46), C(92, 108, 58), C(0, 0, 0), Mark.Cross, Barrel: C(160, 138, 86)),
        ["Winter"] = new(Scheme.Whitewash, C(104, 114, 88), C(236, 240, 242), C(0, 0, 0), C(0, 0, 0), Barrel: C(206, 210, 212)),
        ["Desert"] = new(Scheme.SoftBlotch, C(220, 194, 142), C(170, 128, 86), C(238, 224, 186), C(0, 0, 0), Mark.Flash, Scale: 0.8f, Barrel: C(172, 132, 90)),
        ["Woodland"] = new(Scheme.Blotch, C(76, 108, 54), C(108, 76, 48), C(36, 36, 32), C(76, 108, 54), Scale: 0.95f, Barrel: C(60, 74, 44)),
        ["Tiger"] = new(Scheme.TigerStripes, C(242, 140, 38), C(26, 24, 22), C(250, 236, 208), C(0, 0, 0), Barrel: C(30, 28, 26)),
        // showpieces
        ["Rainbow"] = new(Scheme.Rainbow, C(255, 255, 255), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Barrel: C(240, 240, 240), Wheels: C(230, 230, 235)),
        ["Neon"] = new(Scheme.Neon, C(26, 22, 44), C(0, 240, 255), C(255, 40, 200), C(0, 0, 0), Barrel: C(40, 34, 64), Wheels: C(60, 50, 96)),
        ["Gold"] = new(Scheme.Gold, C(236, 184, 48), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Mark.Crown, C(255, 244, 190), Barrel: C(214, 164, 40), Wheels: C(204, 156, 46), Trim: C(150, 104, 22)),
        ["Chrome"] = new(Scheme.Chrome, C(206, 212, 220), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Barrel: C(190, 196, 206), Wheels: C(176, 182, 192), Trim: C(96, 102, 112)),
        // famous real tanks
        ["Renault FT"] = new(Scheme.Outlined, C(98, 110, 64), C(188, 158, 96), C(112, 84, 56), C(34, 32, 28), Mark.Card, Scale: 1.25f, Barrel: C(58, 58, 48)),
        ["Mark I"] = new(Scheme.Outlined, C(112, 116, 74), C(142, 122, 86), C(164, 142, 130), C(64, 52, 40), Mark.Flash, Scale: 0.9f, Barrel: C(74, 68, 54)),
        ["Tiger I"] = new(Scheme.Plain, C(92, 98, 102), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Mark.Cross, Barrel: C(72, 76, 80)),
        ["T-34"] = new(Scheme.Plain, C(80, 100, 54), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Mark.RedStar, Barrel: C(66, 82, 46)),
        ["M1 Abrams"] = new(Scheme.SoftBlotch, C(200, 174, 132), C(184, 156, 114), C(210, 186, 146), C(0, 0, 0), Mark.Chevron, C(40, 38, 34), Scale: 0.7f, Barrel: C(176, 150, 110)),
        ["Leopard 2"] = new(Scheme.Blotch, C(78, 90, 54), C(96, 72, 52), C(34, 34, 30), C(78, 90, 54), Mark.IronCross, Scale: 0.75f, Barrel: C(70, 82, 50)),
        ["Challenger 2"] = new(Scheme.Bands, C(62, 74, 50), C(28, 30, 26), C(62, 74, 50), C(0, 0, 0), Mark.Flash, Scale: 1.3f, Barrel: C(52, 62, 42)),
        ["Merkava IV"] = new(Scheme.Plain, C(146, 143, 116), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Mark.Chevron, C(240, 240, 232), Barrel: C(128, 125, 100)),
        ["Centurion"] = new(Scheme.Plain, C(54, 66, 44), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Mark.WhiteStar, Barrel: C(44, 54, 36)),
        ["T-90"] = new(Scheme.Bands, C(92, 108, 64), C(178, 158, 106), C(42, 42, 36), C(0, 0, 0), Mark.Diamond, C(240, 240, 232), Scale: 1.6f, Barrel: C(76, 90, 54)),
    };

    /// <summary>The paint job for a special player skin, or null if it has none.</summary>
    public static Design? ForSkin(string name) => _player.TryGetValue(name, out var d) ? d : null;

    private static readonly Dictionary<BossKind, Design> _boss = new() {
        [BossKind.CaptainMarine] = new(Scheme.Sailor, C(34, 62, 128), C(240, 244, 248), C(0, 0, 0), C(0, 0, 0), Mark.Anchor, C(255, 214, 90), Barrel: C(28, 48, 100), Wheels: C(200, 206, 214)),
        [BossKind.MamaBrown] = new(Scheme.Dots, C(126, 84, 52), C(246, 232, 204), C(0, 0, 0), C(0, 0, 0), Mark.Card, Barrel: C(98, 64, 40)),
        [BossKind.Burrower] = new(Scheme.Hazard, C(108, 82, 58), C(240, 196, 40), C(30, 28, 26), C(0, 0, 0), Mark.Skull, C(240, 232, 214), Barrel: C(80, 80, 84), Wheels: C(110, 96, 80)),
        [BossKind.TwinAsh] = new(Scheme.Cracks, C(66, 64, 66), C(255, 120, 30), C(0, 0, 0), C(0, 0, 0), Mark.Skull, C(232, 226, 220), Barrel: C(48, 46, 48), Glow: C(255, 120, 30)),
        [BossKind.VioletViper] = new(Scheme.Scales, C(110, 52, 170), C(170, 110, 230), C(238, 220, 120), C(0, 0, 0), Mark.Eye, C(255, 220, 60), Barrel: C(84, 40, 132), Wheels: C(90, 70, 120)),
        [BossKind.JellyRoller] = new(Scheme.Candy, C(255, 130, 190), C(255, 214, 232), C(0, 0, 0), C(0, 0, 0), Mark.Card, Barrel: C(230, 96, 160), Wheels: C(255, 236, 244)),
        [BossKind.PepperPot] = new(Scheme.Specks, C(246, 214, 70), C(34, 30, 26), C(214, 58, 44), C(0, 0, 0), Mark.Bolt, C(214, 58, 44), Barrel: C(200, 170, 50)),
        [BossKind.GreenGoliath] = new(Scheme.Plates, C(64, 132, 66), C(196, 214, 160), C(0, 0, 0), C(0, 0, 0), Mark.Skull, C(226, 240, 200), Barrel: C(46, 98, 48), Wheels: C(70, 90, 70)),
        [BossKind.DrillSergeant] = new(Scheme.Blotch, C(92, 104, 60), C(72, 82, 48), C(110, 96, 62), C(92, 104, 60), Mark.Rank, C(250, 206, 60), Barrel: C(70, 80, 46)),
        [BossKind.MagnetMaster] = new(Scheme.Halves, C(214, 50, 46), C(48, 96, 214), C(206, 210, 218), C(0, 0, 0), Mark.Bolt, C(255, 236, 110), Barrel: C(190, 194, 202), Wheels: C(170, 174, 182)),
        [BossKind.LaserLathe] = new(Scheme.Circuit, C(70, 74, 82), C(255, 70, 50), C(0, 0, 0), C(0, 0, 0), Mark.Eye, C(255, 70, 50), Barrel: C(120, 124, 132), Glow: C(255, 70, 50)),
        [BossKind.BlackBaron] = new(Scheme.Plain, C(34, 32, 36), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Mark.IronCross, Barrel: C(170, 30, 30), Wheels: C(150, 36, 36), Trim: C(170, 30, 30)),
        [BossKind.ArtilleryMajor] = new(Scheme.SoftBlotch, C(98, 106, 62), C(74, 84, 50), C(124, 112, 72), C(0, 0, 0), Mark.WhiteStar, Barrel: C(60, 66, 44), Trim: C(214, 176, 60)),
        [BossKind.ShieldCommander] = new(Scheme.Hex, C(98, 60, 164), C(120, 230, 255), C(150, 110, 220), C(0, 0, 0), Mark.Square, C(120, 230, 255), Barrel: C(76, 46, 130), Wheels: C(120, 100, 170)),
        [BossKind.ObsidianOverlord] = new(Scheme.Cracks, C(24, 22, 32), C(170, 80, 255), C(0, 0, 0), C(0, 0, 0), Mark.Crown, C(200, 150, 255), Scale: 0.8f, Barrel: C(36, 32, 50), Wheels: C(60, 50, 86), Glow: C(170, 80, 255)),
        [BossKind.VaultKeeper] = new(Scheme.Plates, C(104, 112, 126), C(236, 196, 80), C(0, 0, 0), C(0, 0, 0), Mark.Diamond, C(236, 196, 80), Barrel: C(84, 90, 102), Wheels: C(196, 160, 60)),
        [BossKind.MirrorTank] = new(Scheme.Chrome, C(206, 212, 220), C(0, 0, 0), C(0, 0, 0), C(0, 0, 0), Mark.Eye, C(255, 60, 60), Barrel: C(190, 196, 206), Wheels: C(176, 182, 192), Trim: C(96, 102, 112)),
        [BossKind.GoldHoarder] = new(Scheme.Coins, C(232, 180, 44), C(255, 228, 120), C(0, 0, 0), C(0, 0, 0), Mark.Crown, C(255, 244, 190), Barrel: C(206, 156, 36), Wheels: C(196, 150, 40), Trim: C(140, 96, 20)),
        [BossKind.Hive] = new(Scheme.Hex, C(246, 190, 40), C(40, 30, 16), C(255, 226, 120), C(0, 0, 0), Scale: 0.85f, Barrel: C(46, 36, 20), Wheels: C(70, 56, 30)),
        [BossKind.SplitterKing] = new(Scheme.Diamonds, C(255, 140, 200), C(250, 244, 250), C(0, 0, 0), C(0, 0, 0), Mark.Crown, C(255, 214, 80), Barrel: C(220, 100, 170)),
        [BossKind.BigBertha] = new(Scheme.Rust, C(122, 128, 136), C(150, 84, 44), C(92, 98, 106), C(0, 0, 0), Mark.Skull, C(246, 226, 110), Barrel: C(70, 74, 82), Wheels: C(96, 90, 86)),
        [BossKind.PyroBaron] = new(Scheme.Flames, C(30, 26, 28), C(214, 50, 26), C(255, 140, 30), C(255, 226, 90), Mark.Skull, C(255, 226, 150), Barrel: C(44, 36, 36), Wheels: C(120, 50, 30), Glow: C(255, 120, 30)),
        [BossKind.ChronoTank] = new(Scheme.Rings, C(36, 58, 128), C(140, 220, 255), C(236, 206, 110), C(0, 0, 0), Barrel: C(196, 170, 90), Wheels: C(196, 170, 90)),
        [BossKind.Juggernaut] = new(Scheme.Plates, C(176, 182, 192), C(96, 100, 110), C(0, 0, 0), C(0, 0, 0), Mark.IronCross, Barrel: C(120, 126, 136), Wheels: C(130, 134, 144)),
        [BossKind.Phantom] = new(Scheme.Wisps, C(196, 208, 226), C(240, 246, 255), C(170, 220, 255), C(0, 0, 0), Mark.Eye, C(120, 200, 255), Barrel: C(170, 184, 206), Wheels: C(206, 214, 228), Glow: C(170, 220, 255)),
        [BossKind.ShadowClones] = new(Scheme.Circuit, C(20, 30, 26), C(80, 255, 140), C(0, 0, 0), C(0, 0, 0), Mark.Eye, C(80, 255, 140), Barrel: C(30, 46, 38), Wheels: C(40, 70, 54), Glow: C(80, 255, 140)),
    };

    /// <summary>The paint job for a Tank Troves boss, or null if it keeps its tank's own colors.</summary>
    public static Design? ForBoss(BossKind kind) => _boss.TryGetValue(kind, out var d) ? d : null;

    // (for drawing them all side by side)
    public static List<(string Name, Design Design)> PlayerPreview() {
        var l = new List<(string, Design)>();
        foreach (var kv in _player) l.Add((kv.Key, kv.Value));
        return l;
    }
    public static List<(string Name, Design Design)> BossPreview() {
        var l = new List<(string, Design)>();
        foreach (var kv in _boss) l.Add((kv.Key.ToString(), kv.Value));
        return l;
    }
}
