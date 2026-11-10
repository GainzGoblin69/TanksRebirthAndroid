using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TanksRebirth.GameContent.Systems.Troves;
using TanksRebirth.Internals;
using TanksRebirth.Internals.Common.GameUI;

namespace TanksRebirth.GameContent.UI;

/// <summary>
/// The Tank Troves item icons, drawn once into one sheet (a glossy badge in the item's color, its symbol and a soft
/// shadow, smoothed by drawing at twice the size) and from then on shown as a single picture each: sharper at every
/// size than the shapes were, and much less to draw every frame.
/// </summary>
public static class TroveIcons {
    private const int Cell = 128, Cols = 16, SS = 2;
    /// <summary>The badge's radius inside its cell (the rest is room for the shadow).</summary>
    private const float R0 = 52f;

    private static Texture2D? _sheet;
    private static bool _failed, _hooked;
    private static int _count;

    /// <summary>The finished sheet (for tests).</summary>
    public static Texture2D? Sheet => _sheet;

    /// <summary>Draws an item's icon from the sheet; false if there's no sheet (the caller draws it by hand).</summary>
    public static bool Draw(SpriteBatch sb, TroveItemId id, Vector2 c, float r) {
        if (_sheet is not { IsDisposed: false } sheet || (int)id >= _count)
            return false;
        int i = (int)id;
        var src = new Rectangle(i % Cols * Cell, i / Cols * Cell, Cell, Cell);
        sb.Draw(sheet, c, src, Color.White, 0f, new Vector2(Cell / 2f), r / R0, SpriteEffects.None, 0f);
        return true;
    }

    /// <summary>Builds the sheet if it's missing. Call at the start of a frame's drawing (it switches render targets).</summary>
    public static void EnsureBuilt(GraphicsDevice gd) {
        if (_failed || _sheet is { IsDisposed: false })
            return;
        if (!_hooked) {
            _hooked = true;
            gd.DeviceReset += (_, _) => { _sheet?.Dispose(); _sheet = null; };
        }
        try {
            Build(gd);
        }
        catch (Exception e) {
            _failed = true;
            _sheet = null;
            TankGame.ClientLog?.Write($"Item icon sheet failed (icons are drawn the old way): {e.Message}", LogType.Warn);
        }
        finally {
            ToggleSwitch.StraightAlpha = false;
            ToggleSwitch.Override = null;
            ToggleSwitch.Shift = Vector2.Zero;
        }
    }

    // colors add up as "color x coverage" and alpha as coverage, so the sheet's see-through edges come out right
    private static readonly BlendState _blend = new() {
        ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };

    private static void Build(GraphicsDevice gd) {
        var ids = Enum.GetValues<TroveItemId>();
        int rows = (ids.Length + Cols - 1) / Cols;
        int w = Cols * Cell, h = 1;
        while (h < rows * Cell)
            h *= 2; // (mipmaps want power-of-two sizes on older phones)
        // premultiplied pixels of the whole sheet
        var pm = new byte[w * h * 4];
        int big = Cell * SS;
        using var rt = new RenderTarget2D(gd, Cols * big, big, false, SurfaceFormat.Color, DepthFormat.None);
        using var sb = new SpriteBatch(gd);
        var row = new Color[rt.Width * rt.Height];
        ToggleSwitch.StraightAlpha = true;
        for (int ry = 0; ry < rows; ry++) {
            gd.SetRenderTarget(rt);
            gd.Clear(Color.Transparent);
            sb.Begin(SpriteSortMode.Deferred, _blend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            for (int cx = 0; cx < Cols; cx++) {
                int i = ry * Cols + cx;
                if (i >= ids.Length)
                    break;
                DrawFancy(sb, ids[i], new Vector2((cx + 0.5f) * big, big / 2f), R0 * SS);
            }
            sb.End();
            gd.SetRenderTarget(null);
            rt.GetData(row);
            // shrink by SS (averaging), into the sheet
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < w; x++) {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++) {
                            var p = row[(y * SS + sy) * rt.Width + x * SS + sx];
                            r += p.R; g += p.G; b += p.B; a += p.A;
                        }
                    int o = ((ry * Cell + y) * w + x) * 4;
                    int n = SS * SS;
                    pm[o] = (byte)(r / n); pm[o + 1] = (byte)(g / n); pm[o + 2] = (byte)(b / n); pm[o + 3] = (byte)(a / n);
                }
        }
        ToggleSwitch.StraightAlpha = false;

        // the sheet with every smaller size (so small icons are smooth), as straight alpha like the game's other pictures
        var sheet = new Texture2D(gd, w, h, true, SurfaceFormat.Color);
        int lw = w, lh = h;
        for (int level = 0; level < sheet.LevelCount; level++) {
            var data = new Color[lw * lh];
            for (int i = 0; i < data.Length; i++) {
                int a = pm[i * 4 + 3];
                data[i] = a == 0 ? default : new Color(Math.Min(255, pm[i * 4] * 255 / a), Math.Min(255, pm[i * 4 + 1] * 255 / a), Math.Min(255, pm[i * 4 + 2] * 255 / a), a);
            }
            sheet.SetData(level, null, data, 0, data.Length);
            if (lw == 1 && lh == 1)
                break;
            int nw = Math.Max(1, lw / 2), nh = Math.Max(1, lh / 2);
            var next = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                    for (int ch = 0; ch < 4; ch++) {
                        int x1 = Math.Min(lw - 1, x * 2 + 1), y1 = Math.Min(lh - 1, y * 2 + 1);
                        next[(y * nw + x) * 4 + ch] = (byte)((pm[(y * 2 * lw + x * 2) * 4 + ch] + pm[(y * 2 * lw + x1) * 4 + ch]
                            + pm[(y1 * lw + x * 2) * 4 + ch] + pm[(y1 * lw + x1) * 4 + ch]) / 4);
                    }
            pm = next;
            lw = nw;
            lh = nh;
        }
        _sheet = sheet;
        _count = ids.Length;
    }

    /// <summary>One icon at full quality: shadow, rim, a lit dome in the item's color, a soft shine, and the symbol with
    /// its own shadow.</summary>
    private static void DrawFancy(SpriteBatch sb, TroveItemId id, Vector2 c, float r) {
        var col = TroveItems.Def(id).Color;
        // a soft shadow under the badge
        for (int k = 0; k < 4; k++)
            ToggleSwitch.DrawDisc(sb, c + new Vector2(0, r * 0.09f), r * (1.13f - k * 0.035f), new Color(0, 0, 0, 22 + k * 10));
        // the rim: dark, with a lighter top edge
        ToggleSwitch.DrawDisc(sb, c, r, Color.Lerp(col, Color.Black, 0.55f));
        ToggleSwitch.DrawDisc(sb, c - new Vector2(0, r * 0.012f), r * 0.965f, Color.Lerp(col, Color.Black, 0.3f));
        // the face: darker at the bottom, brighter toward the top
        ToggleSwitch.DrawDisc(sb, c, r * 0.9f, Color.Lerp(col, Color.Black, 0.16f));
        const int steps = 8;
        for (int k = 0; k < steps; k++) {
            float f = k / (float)(steps - 1);
            ToggleSwitch.DrawDisc(sb, c - new Vector2(0, r * (0.03f + 0.09f * f)), r * (0.885f - 0.2f * f), Color.Lerp(col, Color.White, 0.02f + 0.2f * f));
        }
        // the symbol, with a shadow of its own
        ToggleSwitch.Override = new Color(0, 0, 0, 70);
        ToggleSwitch.Shift = new Vector2(0, r * 0.055f);
        TroveHud.DrawItemSymbol(sb, id, c, r);
        ToggleSwitch.Override = null;
        ToggleSwitch.Shift = Vector2.Zero;
        TroveHud.DrawItemSymbol(sb, id, c, r);
        // a soft shine across the top
        for (int k = 0; k < 3; k++)
            ToggleSwitch.DrawDisc(sb, c - new Vector2(0, r * (0.42f + k * 0.03f)), r * (0.44f - k * 0.07f), new Color(255, 255, 255, 16));
    }
}
