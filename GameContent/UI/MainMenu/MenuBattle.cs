using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.ID;
using TanksRebirth.GameContent.Systems;
using TanksRebirth.GameContent.Systems.AI;

namespace TanksRebirth.GameContent.UI.MainMenu;

/// <summary>
/// Livens up the main menu's background battles: the tanks split into 2-4 teams, each painted its own color
/// (one team is sometimes all black), so you can tell who's fighting whom. Now and then a tank is a special
/// version (gold, chrome, neon or rainbow) or carries an item (faster, faster shooting, more shells, extra bounce),
/// shown by a colored glow under it.
/// </summary>
public static class MenuBattle {
    static readonly Color[] _teamColors = [
        new(215, 55, 50),   // red
        new(55, 105, 230),  // blue
        new(60, 170, 75),   // green
        new(235, 200, 45),  // yellow
        new(240, 130, 40),  // orange
        new(150, 80, 210),  // purple
        new(225, 225, 232), // white
        new(40, 200, 210),  // cyan
    ];
    static readonly Color Black = new(52, 52, 60);

    enum Special { None, Gold, Chrome, Neon, Rainbow }
    enum Item { None, Speed, RapidFire, MoreShells, Bounce }

    sealed class Look {
        public AITank Tank = null!;
        public float[] Lum = [];
        public Color TeamColor;
        public Special Special;
        public Item Item;
    }

    static readonly List<Look> _looks = [];
    static readonly Random _rng = new();
    static int _frame;

    /// <summary>Called right after a menu map is set up.</summary>
    // the textures painted for the last battle (thrown away when the next one starts)
    static readonly List<Texture2D> _owned = [];

    public static void Decorate() {
        _looks.Clear();
        foreach (var o in _owned)
            if (!GameHandler.AllAITanks.Any(t => t is not null && t.TankTexture == o))
                o.Dispose();
        _owned.RemoveAll(o => o.IsDisposed);
        var tanks = GameHandler.AllAITanks.Where(t => t is { IsDestroyed: false }).ToList();
        if (tanks.Count < 2)
            return;

        // teams: keep the map's own teams; a free-for-all map is split into 2-4 teams by where the tanks start
        var teams = new Dictionary<AITank, int>();
        var mapTeams = tanks.Select(t => t.Team).Where(t => t != TeamID.NoTeam).Distinct().ToList();
        if (mapTeams.Count >= 2) {
            foreach (var t in tanks)
                teams[t] = t.Team == TeamID.NoTeam ? mapTeams[_rng.Next(mapTeams.Count)] : t.Team;
        }
        else {
            int k = tanks.Count >= 8 && _rng.NextDouble() < 0.35 ? 4 : tanks.Count >= 6 && _rng.NextDouble() < 0.4 ? 3 : 2;
            k = Math.Min(k, tanks.Count);
            // walk around the board's center, so each team starts together
            var center = new Vector2(tanks.Average(t => t.Position.X), tanks.Average(t => t.Position.Y));
            float spin = (float)(_rng.NextDouble() * MathHelper.TwoPi);
            var sorted = tanks.OrderBy(t => MathHelper.WrapAngle(MathF.Atan2(t.Position.Y - center.Y, t.Position.X - center.X) + spin)).ToList();
            for (int i = 0; i < sorted.Count; i++)
                teams[sorted[i]] = TeamID.Red + i * k / sorted.Count;
        }

        // a color per team (one is sometimes black)
        var colors = _teamColors.OrderBy(_ => _rng.Next()).ToList();
        var teamIds = teams.Values.Distinct().ToList();
        if (_rng.NextDouble() < 0.4)
            colors.Insert(_rng.Next(Math.Min(teamIds.Count, colors.Count)), Black);
        var colorOf = new Dictionary<int, Color>();
        for (int i = 0; i < teamIds.Count; i++)
            colorOf[teamIds[i]] = colors[i % colors.Count];

        int specials = 0;
        foreach (var t in tanks) {
            t.Team = teams[t];
            if (t.TankTexture is not { IsDisposed: false } tex || tex.Width * tex.Height > 64 * 64)
                continue;
            var data = new Color[tex.Width * tex.Height];
            tex.GetData(data);
            // paint our own copy (the tank's texture may be shared with every tank of its kind)
            var own = new Texture2D(tex.GraphicsDevice, tex.Width, tex.Height);
            _owned.Add(own);
            t.SwapTankTexture(own);
            tex = own;
            var look = new Look { Tank = t, TeamColor = colorOf[t.Team], Lum = new float[data.Length] };
            float avg = 0;
            for (int i = 0; i < data.Length; i++) {
                look.Lum[i] = (data[i].R * 0.299f + data[i].G * 0.587f + data[i].B * 0.114f) / 255f;
                avg += look.Lum[i];
            }
            avg = Math.Max(0.05f, avg / data.Length);
            for (int i = 0; i < data.Length; i++)
                look.Lum[i] /= avg; // 1 = the texture's average brightness
            if (specials < 2 && _rng.NextDouble() < 0.12) {
                look.Special = (Special)_rng.Next(1, 5);
                specials++;
            }
            if (_rng.NextDouble() < 0.25)
                GiveItem(t, look.Item = (Item)_rng.Next(1, 5));
            Paint(look, tex);
            _looks.Add(look);
        }
    }

    static void GiveItem(AITank t, Item item) {
        switch (item) {
            case Item.Speed:
                t.Properties.MaxSpeed *= 1.5f;
                t.Properties.Acceleration *= 1.5f;
                break;
            case Item.RapidFire:
                t.Properties.ShellCooldown = (uint)Math.Max(5, t.Properties.ShellCooldown * 0.5f);
                break;
            case Item.MoreShells:
                t.Properties.ShellLimit += 2;
                break;
            case Item.Bounce:
                t.Properties.RicochetCount += 1;
                break;
        }
    }

    static Color[] _buf = [];
    static void Paint(Look look, Texture2D tex) {
        if (_buf.Length != look.Lum.Length)
            _buf = new Color[look.Lum.Length];
        float time = (float)(DateTime.UtcNow.TimeOfDay.TotalSeconds % 3600);
        for (int i = 0; i < _buf.Length; i++) {
            float l = look.Lum[i];
            float shade = MathHelper.Clamp(0.25f + 0.75f * l, 0f, 1.6f);
            _buf[i] = look.Special switch {
                Special.Gold => Scale(new Color(236, 186, 52), shade * 1.05f),
                Special.Chrome => Scale(new Color(205, 212, 222), MathHelper.Clamp(0.15f + 0.95f * l, 0f, 1.5f)),
                Special.Neon => l > 1.15f ? new Color(0, 240, 255) : l > 0.95f ? new Color(255, 40, 200) : Scale(new Color(34, 28, 56), shade),
                Special.Rainbow => Scale(PlayerColors.HsvToColor(time * 0.3f + (i % 32) / 32f, 0.8f, 1f), shade),
                _ => Scale(look.TeamColor, shade),
            };
        }
        tex.SetData(_buf);
    }

    static Color Scale(Color c, float f) => new(Math.Clamp(c.R * f / 255f, 0f, 1f), Math.Clamp(c.G * f / 255f, 0f, 1f), Math.Clamp(c.B * f / 255f, 0f, 1f));

    static Color ItemColor(Item item) => item switch {
        Item.Speed => new Color(255, 150, 40),
        Item.RapidFire => new Color(255, 80, 80),
        Item.MoreShells => new Color(255, 230, 90),
        _ => new Color(110, 255, 150),
    };

    /// <summary>Each frame on the menu: rainbow tanks shift color, and glows under special and item tanks.</summary>
    public static void Update() {
        _frame++;
        float dt = RuntimeData.DeltaTime;
        foreach (var look in _looks) {
            var t = look.Tank;
            if (t.IsDestroyed)
                continue;
            if (look.Special == Special.Rainbow && _frame % 3 == 0 && t.TankTexture is { IsDisposed: false } tex)
                Paint(look, tex);
            // a special keeps showing its team with a glow in the team's color; an item glows in its own color
            Color? glow = look.Item != Item.None ? ItemColor(look.Item) : look.Special != Special.None ? look.TeamColor : null;
            if (glow is { } g && _rng.NextDouble() < 0.35 * dt) {
                var off = new Vector3(_rng.NextSingle() * 16 - 8, 1.5f, _rng.NextSingle() * 16 - 8);
                GameHandler.Particles.MakeShineSpot(t.Position3D + off, g, 0.35f);
            }
            if (look.Item == Item.Speed && t.Velocity.LengthSquared() > 0.1f && _rng.NextDouble() < 0.3 * dt) {
                var back = t.Position3D - new Vector3(t.Velocity.X, 0, t.Velocity.Y) * 6f + new Vector3(0, 5, 0);
                GameHandler.Particles.MakeShineSpot(back, new Color(255, 200, 80), 0.4f);
            }
        }
    }
}
