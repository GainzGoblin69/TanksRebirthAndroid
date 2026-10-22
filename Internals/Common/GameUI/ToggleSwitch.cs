using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TanksRebirth.Internals.Common.GameUI;

/// <summary>Draws a phone-style on/off switch: a rounded track (green when on, grey when off) with a white knob.</summary>
public static class ToggleSwitch {
    public static readonly Color OnColor = new(52, 199, 89);
    public static readonly Color OffColor = new(120, 120, 128);

    private static Texture2D? _circle;
    private static Texture2D? _pixel;

    /// <summary>The switch's rectangle at the right end of <paramref name="row"/>, vertically centered.</summary>
    public static Rectangle RightOf(Rectangle row, float heightFraction = 0.6f) {
        int h = Math.Max(8, (int)(row.Height * heightFraction));
        int w = (int)(h * 1.75f);
        int pad = Math.Max(4, row.Height / 5);
        return new Rectangle(row.Right - w - pad, row.Center.Y - h / 2, w, h);
    }

    public static void Draw(SpriteBatch sb, Rectangle box, bool on) {
        EnsureTextures(sb.GraphicsDevice);
        float r = box.Height / 2f;
        var track = on ? OnColor : OffColor;

        // track: rectangle between two end caps (non-premultiplied alpha, matching the UI's blend state)
        sb.Draw(_pixel!, new Rectangle((int)(box.X + r), box.Y, (int)(box.Width - 2 * r), box.Height), track);
        DrawCircle(sb, new Vector2(box.X + r, box.Y + r), r, track);
        DrawCircle(sb, new Vector2(box.Right - r, box.Y + r), r, track);

        // knob
        float knobR = r * 0.82f;
        var knobCenter = new Vector2(on ? box.Right - r : box.X + r, box.Y + r);
        DrawCircle(sb, knobCenter + new Vector2(0, r * 0.08f), knobR, Color.Black * 0.25f); // soft shadow
        DrawCircle(sb, knobCenter, knobR, Color.White);
    }

    /// <summary>A filled circle (straight alpha; use with BlendState.NonPremultiplied).</summary>
    public static void DrawDisc(SpriteBatch sb, Vector2 center, float radius, Color color) {
        EnsureTextures(sb.GraphicsDevice);
        DrawCircle(sb, center, radius, color);
    }

    public static void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, float thickness, Color color) {
        EnsureTextures(sb.GraphicsDevice);
        var d = b - a;
        sb.Draw(_pixel!, a, null, color, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(d.Length(), thickness), SpriteEffects.None, 0f);
    }

    /// <summary>A check mark in a filled circle.</summary>
    public static void DrawCheckBadge(SpriteBatch sb, Vector2 center, float radius, Color fill) {
        DrawDisc(sb, center + new Vector2(0, radius * 0.08f), radius, Color.Black * 0.35f);
        DrawDisc(sb, center, radius, fill);
        float t = radius * 0.24f;
        var p1 = center + new Vector2(-0.48f, 0.02f) * radius;
        var p2 = center + new Vector2(-0.12f, 0.38f) * radius;
        var p3 = center + new Vector2(0.52f, -0.36f) * radius;
        DrawLine(sb, p1, p2, t, Color.White);
        DrawLine(sb, p2 - Vector2.Normalize(p3 - p2) * t * 0.4f, p3, t, Color.White);
    }

    private static void DrawCircle(SpriteBatch sb, Vector2 center, float radius, Color color) {
        var scale = radius * 2f / _circle!.Width;
        sb.Draw(_circle, center, null, color, 0f, new Vector2(_circle.Width / 2f), scale, SpriteEffects.None, 0f);
    }

    private static void EnsureTextures(GraphicsDevice gd) {
        if (_circle is { IsDisposed: false } && _pixel is { IsDisposed: false })
            return;

        const int size = 128;
        const float rad = size / 2f;
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(rad));
                float a = MathHelper.Clamp(rad - 0.5f - d, 0f, 1f);
                data[y * size + x] = new Color(255, 255, 255, (int)(a * 255)); // straight alpha
            }
        _circle = new Texture2D(gd, size, size);
        _circle.SetData(data);

        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }
}
