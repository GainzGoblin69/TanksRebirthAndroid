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

    // where each switch's knob is (0 = off, 1 = on), so flipping one slides the knob over instead of jumping
    private static readonly System.Collections.Generic.Dictionary<Point, float> _knobs = [];
    private static readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _lastTime;

    public static void Draw(SpriteBatch sb, Rectangle box, bool on) {
        EnsureTextures(sb.GraphicsDevice);
        float r = box.Height / 2f;

        double now = _clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Clamp(now - _lastTime, 0, 0.1);
        _lastTime = now;
        float target = on ? 1f : 0f;
        if (!_knobs.TryGetValue(box.Location, out var k))
            k = target;
        k = MathF.Abs(target - k) < 0.01f ? target : MathHelper.Lerp(k, target, 1f - MathF.Exp(-dt * 18f));
        _knobs[box.Location] = k;

        var track = Color.Lerp(OffColor, OnColor, k);
        float border = MathF.Max(1.5f, box.Height * 0.06f);
        // a darker rim, the track, and a faint top highlight
        DrawRoundedRect(sb, Inflate(box, border), r + border, Color.Lerp(track, Color.Black, 0.28f));
        DrawRoundedRect(sb, box, r, track);
        var shine = new Rectangle(box.X + (int)(r * 0.5f), box.Y + (int)(box.Height * 0.1f), box.Width - (int)r, (int)(box.Height * 0.32f));
        DrawRoundedRect(sb, shine, shine.Height / 2f, Color.White * 0.12f);

        // little I / O marks in the open half of the track
        float mark = box.Height * 0.28f;
        float markA = 0.85f;
        var onMark = new Vector2(box.X + r * 1.05f, box.Center.Y);
        DrawLine(sb, onMark - new Vector2(0, mark / 2f), onMark + new Vector2(0, mark / 2f), MathF.Max(2f, box.Height * 0.08f), Color.White * (markA * k));
        var offMark = new Vector2(box.Right - r * 1.05f, box.Center.Y);
        DrawCircle(sb, offMark, mark * 0.55f, new Color(235, 235, 240) * (markA * (1 - k)));
        DrawCircle(sb, offMark, mark * 0.55f - MathF.Max(2f, box.Height * 0.08f), track);

        // knob, with a soft shadow and a thin edge
        float knobR = r * 0.84f;
        var knobCenter = new Vector2(MathHelper.Lerp(box.X + r, box.Right - r, k), box.Y + r);
        DrawCircle(sb, knobCenter + new Vector2(0, r * 0.14f), knobR * 1.04f, Color.Black * 0.22f);
        DrawCircle(sb, knobCenter, knobR, new Color(214, 214, 220));
        DrawCircle(sb, knobCenter, knobR - MathF.Max(1f, r * 0.05f), Color.White);
        DrawCircle(sb, knobCenter + new Vector2(0, knobR * 0.35f), knobR * 0.55f, new Color(240, 240, 245) * 0.6f);
    }

    private static Rectangle Inflate(Rectangle r, float by) {
        int b = (int)MathF.Round(by);
        return new Rectangle(r.X - b, r.Y - b, r.Width + 2 * b, r.Height + 2 * b);
    }

    /// <summary>A filled circle (straight alpha; use with BlendState.NonPremultiplied).</summary>
    public static void DrawDisc(SpriteBatch sb, Vector2 center, float radius, Color color) {
        EnsureTextures(sb.GraphicsDevice);
        DrawCircle(sb, center + Shift, radius, Tint(color));
    }

    /// <summary>While set, every shape is drawn in this color (as see-through as the shape was) and moved by
    /// <see cref="Shift"/>: drawing something twice, first like this, gives it a drop shadow.</summary>
    public static Color? Override;
    public static Vector2 Shift;
    /// <summary>Circles come from a straight-alpha texture while set (for drawing into a texture with its own blending).</summary>
    public static bool StraightAlpha;

    private static Color Tint(Color c) => Override is { } o ? new Color(o.R, o.G, o.B, (byte)(o.A * c.A / 255)) : c;

    public static void DrawLine(SpriteBatch sb, Vector2 a, Vector2 b, float thickness, Color color) {
        EnsureTextures(sb.GraphicsDevice);
        a += Shift;
        b += Shift;
        color = Tint(color);
        var d = b - a;
        sb.Draw(_pixel!, a, null, color, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(d.Length(), thickness), SpriteEffects.None, 0f);
    }

    /// <summary>A filled rounded rectangle with smooth edges (the pieces don't overlap, so see-through colors work too).</summary>
    public static void DrawRoundedRect(SpriteBatch sb, Rectangle r, float radius, Color color) {
        EnsureTextures(sb.GraphicsDevice);
        if (r.Width <= 0 || r.Height <= 0)
            return;
        if (Override is not null || Shift != Vector2.Zero) {
            r.Offset((int)MathF.Round(Shift.X), (int)MathF.Round(Shift.Y));
            color = Tint(color);
        }
        int ri = (int)MathF.Round(MathF.Min(radius, MathF.Min(r.Width, r.Height) / 2f));
        if (ri <= 0) {
            sb.Draw(_pixel!, r, color);
            return;
        }
        const int H = TexSize / 2;
        var circle = CircleFor(sb);
        int midW = r.Width - 2 * ri, midH = r.Height - 2 * ri;
        // corners: the four quarters of the circle
        sb.Draw(circle, new Rectangle(r.X, r.Y, ri, ri), new Rectangle(0, 0, H, H), color);
        sb.Draw(circle, new Rectangle(r.Right - ri, r.Y, ri, ri), new Rectangle(H, 0, H, H), color);
        sb.Draw(circle, new Rectangle(r.X, r.Bottom - ri, ri, ri), new Rectangle(0, H, H, H), color);
        sb.Draw(circle, new Rectangle(r.Right - ri, r.Bottom - ri, ri, ri), new Rectangle(H, H, H, H), color);
        // edges: a one-texel slice through the circle's middle, stretched
        if (midW > 0) {
            sb.Draw(circle, new Rectangle(r.X + ri, r.Y, midW, ri), new Rectangle(H, 0, 1, H), color);
            sb.Draw(circle, new Rectangle(r.X + ri, r.Bottom - ri, midW, ri), new Rectangle(H, H, 1, H), color);
        }
        if (midH > 0) {
            sb.Draw(circle, new Rectangle(r.X, r.Y + ri, ri, midH), new Rectangle(0, H, H, 1), color);
            sb.Draw(circle, new Rectangle(r.Right - ri, r.Y + ri, ri, midH), new Rectangle(H, H, H, 1), color);
        }
        if (midW > 0 && midH > 0)
            sb.Draw(_pixel!, new Rectangle(r.X + ri, r.Y + ri, midW, midH), color);
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
        var tex = CircleFor(sb);
        var scale = radius * 2f / tex.Width;
        sb.Draw(tex, center, null, color, 0f, new Vector2(tex.Width / 2f), scale, SpriteEffects.None, 0f);
    }

    private const int TexSize = 256;
    private static Texture2D? _circleStraight;
    // which blending the sprite batch was started with (it doesn't say, so it's read from inside it)
    private static readonly System.Reflection.FieldInfo? _blendField =
        typeof(SpriteBatch).GetField("_blendState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>The circle picture that blends correctly in this batch: straight alpha for NonPremultiplied batches
    /// (the premultiplied one left a thin dark line around every shape there), premultiplied otherwise.</summary>
    /// <summary>True if this batch blends straight-alpha pictures (NonPremultiplied); false for premultiplied ones.</summary>
    public static bool IsStraight(SpriteBatch sb) {
        try {
            return ReferenceEquals(_blendField?.GetValue(sb), BlendState.NonPremultiplied);
        }
        catch {
            return false;
        }
    }

    private static Texture2D CircleFor(SpriteBatch sb) {
        if (StraightAlpha)
            return _circleStraight!;
        try {
            if (ReferenceEquals(_blendField?.GetValue(sb), BlendState.NonPremultiplied))
                return _circleStraight!;
        }
        catch { }
        return _circle!;
    }

    private static void EnsureTextures(GraphicsDevice gd) {
        if (_circle is { IsDisposed: false } && _pixel is { IsDisposed: false } && _circleStraight is { IsDisposed: false })
            return;
        _circleStraight = new Texture2D(gd, TexSize, TexSize, true, SurfaceFormat.Color);

        // a circle with every mip level drawn exactly (so small circles stay round and smooth instead of jaggy)
        _circle = new Texture2D(gd, TexSize, TexSize, true, SurfaceFormat.Color);
        for (int level = 0, size = TexSize; level < _circle.LevelCount && size >= 1; level++, size /= 2) {
            var data = new Color[size * size];
            float rad = size / 2f;
            const int SS = 4; // 4x4 samples per texel
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    int hit = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++) {
                            float dx = x + (sx + 0.5f) / SS - rad, dy = y + (sy + 0.5f) / SS - rad;
                            if (dx * dx + dy * dy <= rad * rad) hit++;
                        }
                    // premultiplied: fully see-through outside the circle whichever blend mode the batch uses
                    // (straight-alpha white there brightened the corners in premultiplied batches)
                    int a = hit * 255 / (SS * SS);
                    data[y * size + x] = new Color(a, a, a, a);
                }
            _circle.SetData(level, null, data, 0, data.Length);
            for (int i = 0; i < data.Length; i++)
                data[i] = new Color(255, 255, 255, (int)data[i].A);
            _circleStraight.SetData(level, null, data, 0, data.Length);
        }

        _pixel = new Texture2D(gd, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }
}
