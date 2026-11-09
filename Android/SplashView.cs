using System;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace TanksRebirth.Droid;

/// <summary>
/// The Larry Slop Software start-up animation, in the spirit of a mid-2000s console boot: a bright white room,
/// green and silver ribbons of light sweeping in to the middle, a flash and a shockwave ring, then a glossy green
/// orb with Larry's face pops in and the name fades in under it. Drawn with plain Android graphics, so it plays
/// while the game is still loading.
/// </summary>
internal sealed class SplashView : View {
    readonly Bitmap? _face;
    readonly long _start = SystemClock.UptimeMillis();
    readonly Paint _p = new(PaintFlags.AntiAlias);
    readonly Path _path = new();
    readonly (float Angle, float Dir, float Delay, float Width, float Turns, int Kind)[] _ribbons;
    readonly (float Angle, float Speed, float Size, bool White)[] _sparks;
    readonly Typeface _light = Typeface.Create("sans-serif-light", TypefaceStyle.Normal)!;
    readonly Typeface _bold = Typeface.Create("sans-serif", TypefaceStyle.Bold)!;

    // ribbon colors: deep green, lime, silver
    static readonly (int R, int G, int B)[] Kinds = [(16, 124, 16), (122, 205, 60), (190, 198, 190)];
    const int N = 36; // pieces per ribbon
    readonly PointF[] _mid = new PointF[N + 1];
    readonly float[] _half = new float[N + 1];
    readonly float[] _nx = new float[N + 1], _ny = new float[N + 1];

    /// <summary>How long the animation runs (seconds); it holds its last frame after that.</summary>
    public const float Length = 2.6f;

    public SplashView(Context context, Bitmap? face) : base(context) {
        _face = face;
        var rng = new Random(11);
        _ribbons = new (float, float, float, float, float, int)[9];
        for (int i = 0; i < _ribbons.Length; i++)
            _ribbons[i] = (i * MathF.Tau / 9f + (float)rng.NextDouble() * 0.4f, i % 2 == 0 ? 1f : -1f, (float)rng.NextDouble() * 0.3f,
                0.55f + (float)rng.NextDouble() * 0.7f, 0.8f + (float)rng.NextDouble() * 0.6f, i % 3);
        _sparks = new (float, float, float, bool)[40];
        for (int i = 0; i < _sparks.Length; i++)
            _sparks[i] = ((float)rng.NextDouble() * MathF.Tau, 0.3f + (float)rng.NextDouble(), 0.4f + (float)rng.NextDouble(), rng.Next(3) == 0);
    }

    static float Clamp01(float v) => MathF.Max(0f, MathF.Min(1f, v));
    static float EaseOut(float t) => 1f - MathF.Pow(1f - Clamp01(t), 3f);
    static float EaseInOut(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
    /// <summary>Overshoots a little past 1 and settles (a "pop").</summary>
    static float Pop(float t) {
        t = Clamp01(t);
        const float c1 = 1.9f, c3 = c1 + 1f;
        return 1f + c3 * MathF.Pow(t - 1f, 3f) + c1 * MathF.Pow(t - 1f, 2f);
    }
    static int Argb(float a, int r, int g, int b) => Color.Argb((int)(255 * Clamp01(a)), r, g, b).ToArgb();
    static readonly Shader.TileMode Clamp = Shader.TileMode.Clamp!;

    void Fill() {
        _p.Reset();
        _p.AntiAlias = true;
        _p.SetStyle(Paint.Style.Fill);
    }

    protected override void OnDraw(Canvas canvas) {
        base.OnDraw(canvas);
        float t = (SystemClock.UptimeMillis() - _start) / 1000f;
        float w = Width, h = Height;
        float cx = w / 2f, cy = h * 0.43f;
        float unit = MathF.Min(w, h);
        float orbR = unit * 0.2f;

        // a bright white room, a touch greyer at the edges
        Fill();
        _p.SetShader(new RadialGradient(cx, cy, MathF.Max(w, h) * 0.75f, Argb(1, 255, 255, 255), Argb(1, 218, 226, 218), Clamp));
        canvas.DrawRect(0, 0, w, h, _p);

        // ribbons of light sweeping in to the middle: tapered bands, each with a soft glow and a bright core
        float ribbonFade = 1f - Clamp01((t - 1.22f) / 0.25f);
        if (ribbonFade > 0f) {
            foreach (var rb in _ribbons) {
                float head = EaseInOut((t - rb.Delay) / 1.1f);
                if (head <= 0f)
                    continue;
                float tail = MathF.Max(0f, head - 0.5f);
                for (int s = 0; s <= N; s++)
                    _mid[s] = Spiral(rb, tail + (head - tail) * s / N, cx, cy, w, h, orbR);
                for (int s = 0; s <= N; s++) {
                    var a = _mid[Math.Max(0, s - 1)];
                    var b = _mid[Math.Min(N, s + 1)];
                    float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Max(0.001f, MathF.Sqrt(dx * dx + dy * dy));
                    _nx[s] = -dy / len;
                    _ny[s] = dx / len;
                    _half[s] = unit * 0.034f * rb.Width * MathF.Pow(MathF.Sin((float)s / N * MathF.PI), 0.7f) * (1f - 0.55f * head);
                }
                var (r, g, b2) = Kinds[rb.Kind];
                for (int pass = 0; pass < 2; pass++) {
                    float scale = pass == 0 ? 2.2f : 1f, alpha = pass == 0 ? 0.10f : 0.62f;
                    _path.Reset();
                    _path.MoveTo(_mid[0].X + _nx[0] * _half[0] * scale, _mid[0].Y + _ny[0] * _half[0] * scale);
                    for (int s = 1; s <= N; s++)
                        _path.LineTo(_mid[s].X + _nx[s] * _half[s] * scale, _mid[s].Y + _ny[s] * _half[s] * scale);
                    for (int s = N; s >= 0; s--)
                        _path.LineTo(_mid[s].X - _nx[s] * _half[s] * scale, _mid[s].Y - _ny[s] * _half[s] * scale);
                    _path.Close();
                    Fill();
                    _p.SetShader(new LinearGradient(_mid[0].X, _mid[0].Y, _mid[N].X, _mid[N].Y, Argb(0, r, g, b2), Argb(alpha * ribbonFade, r, g, b2), Clamp));
                    canvas.DrawPath(_path, _p);
                }
                Fill();
                _p.SetStyle(Paint.Style.Stroke);
                _p.StrokeCap = Paint.Cap.Round;
                for (int s = N / 2; s < N; s++) {
                    float along = (float)s / N;
                    _p.StrokeWidth = unit * 0.006f * rb.Width * MathF.Pow(MathF.Sin(along * MathF.PI), 0.7f);
                    _p.Color = new Color(Argb(0.7f * (along - 0.5f) * 2f * ribbonFade, 255, 255, 255));
                    canvas.DrawLine(_mid[s].X, _mid[s].Y, _mid[s + 1].X, _mid[s + 1].Y, _p);
                }
            }
        }

        // light gathering in the middle while the ribbons arrive
        float gather = Clamp01((t - 0.5f) / 0.7f) * (1f - Clamp01((t - 1.3f) / 0.3f));
        if (gather > 0f) {
            float rr = orbR * (0.6f + 0.8f * gather);
            Fill();
            _p.SetShader(new RadialGradient(cx, cy, rr, new[] { Argb(0.85f * gather, 255, 255, 255), Argb(0.35f * gather, 150, 220, 90), Argb(0, 150, 220, 90) },
                new[] { 0f, 0.4f, 1f }, Clamp));
            canvas.DrawCircle(cx, cy, rr, _p);
        }

        // the flash where they meet, and a ring racing outward
        float flash = (t - 1.22f) / 0.6f;
        if (flash is > 0f and < 1f) {
            float fr = unit * (0.12f + 0.95f * EaseOut(flash)), fa = 1f - flash;
            Fill();
            _p.SetShader(new RadialGradient(cx, cy, fr, new[] { Argb(fa, 255, 255, 255), Argb(0.45f * fa, 150, 220, 90), Argb(0, 150, 220, 90) },
                new[] { 0f, 0.45f, 1f }, Clamp));
            canvas.DrawCircle(cx, cy, fr, _p);
            Fill();
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = MathF.Max(1f, unit * 0.012f * (1f - flash));
            _p.Color = new Color(Argb(0.6f * (1f - flash), 16, 124, 16));
            canvas.DrawCircle(cx, cy, orbR * (1f + 2.6f * EaseOut(flash)), _p);
        }

        // sparkles drifting out after the flash
        float sparkT = t - 1.3f;
        if (sparkT is > 0f and < 1.6f) {
            Fill();
            foreach (var sp in _sparks) {
                float d = orbR * (1.05f + sp.Speed * 2.3f * EaseOut(sparkT / 1.6f));
                float a = (1f - sparkT / 1.6f) * 0.9f;
                _p.Color = new Color(sp.White ? Argb(a, 255, 255, 255) : Argb(a, 122, 205, 60));
                canvas.DrawCircle(cx + MathF.Cos(sp.Angle) * d, cy + MathF.Sin(sp.Angle) * d, unit * 0.006f * sp.Size, _p);
            }
        }

        // the orb pops in: a glossy green ball with Larry in a round window
        float orbT = (t - 1.3f) / 0.5f;
        if (orbT > 0f) {
            float scale = Pop(orbT);
            float r = orbR * scale * (1f + 0.012f * MathF.Sin(MathF.Max(0f, t - 1.8f) * 3f)); // (it breathes a little)
            // its shadow on the floor
            float sy = cy + orbR * 1.22f, sr = orbR * 1.15f * scale;
            canvas.Save();
            canvas.Scale(1f, 0.2f, cx, sy);
            Fill();
            _p.SetShader(new RadialGradient(cx, sy, sr, Argb(0.26f, 0, 40, 0), Argb(0, 0, 40, 0), Clamp));
            canvas.DrawCircle(cx, sy, sr, _p);
            canvas.Restore();
            // a green halo around it
            Fill();
            _p.SetShader(new RadialGradient(cx, cy, r * 1.35f, new[] { Argb(0, 122, 205, 60), Argb(0.35f, 122, 205, 60), Argb(0, 122, 205, 60) },
                new[] { 0.7f, 0.76f, 1f }, Clamp));
            canvas.DrawCircle(cx, cy, r * 1.35f, _p);
            // the ball: lit from the upper left
            Fill();
            _p.SetShader(new RadialGradient(cx - r * 0.3f, cy - r * 0.38f, r * 1.45f,
                new[] { Argb(1, 178, 240, 120), Argb(1, 76, 176, 44), Argb(1, 14, 96, 14), Argb(1, 5, 46, 8) }, new[] { 0f, 0.32f, 0.72f, 1f }, Clamp));
            canvas.DrawCircle(cx, cy, r, _p);
            // Larry
            float fr = r * 0.78f;
            if (_face is not null) {
                canvas.Save();
                _path.Reset();
                _path.AddCircle(cx, cy, fr, Path.Direction.Cw!);
                canvas.ClipPath(_path);
                Fill();
                _p.FilterBitmap = true;
                canvas.DrawBitmap(_face, null, new RectF(cx - fr, cy - fr, cx + fr, cy + fr), _p);
                // a green shade at the rim, so he sits inside the ball
                Fill();
                _p.SetShader(new RadialGradient(cx, cy, fr, new[] { Argb(0, 10, 80, 10), Argb(0, 10, 80, 10), Argb(0.62f, 8, 60, 8) }, new[] { 0f, 0.7f, 1f }, Clamp));
                canvas.DrawCircle(cx, cy, fr, _p);
                canvas.Restore();
            }
            // the window's rim, with a thin bright edge
            Fill();
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = r * 0.045f;
            _p.Color = Color.Rgb(8, 58, 8);
            canvas.DrawCircle(cx, cy, fr, _p);
            _p.StrokeWidth = MathF.Max(1f, r * 0.012f);
            _p.Color = new Color(Argb(0.5f, 190, 245, 150));
            canvas.DrawCircle(cx, cy, fr + r * 0.03f, _p);
            // glossy shine across the top
            Fill();
            _p.SetShader(new LinearGradient(0, cy - r, 0, cy - r * 0.1f, Argb(0.62f, 255, 255, 255), Argb(0, 255, 255, 255), Clamp));
            canvas.DrawOval(new RectF(cx - r * 0.66f, cy - r * 0.96f, cx + r * 0.66f, cy - r * 0.12f), _p);
            // one sweep of light across the ball, just after it lands
            float sweep = (t - 1.9f) / 0.6f;
            if (sweep is > 0f and < 1f) {
                canvas.Save();
                _path.Reset();
                _path.AddCircle(cx, cy, r, Path.Direction.Cw!);
                canvas.ClipPath(_path);
                canvas.Rotate(20f, cx, cy);
                float sx = cx - r * 1.6f + r * 3.2f * sweep;
                Fill();
                _p.SetShader(new LinearGradient(sx - r * 0.3f, 0, sx + r * 0.3f, 0,
                    new[] { Argb(0, 255, 255, 255), Argb(0.5f, 255, 255, 255), Argb(0, 255, 255, 255) }, null, Clamp));
                canvas.DrawRect(cx - r * 2f, cy - r * 2f, cx + r * 2f, cy + r * 2f, _p);
                canvas.Restore();
            }
        }

        // the name slides up and fades in under the orb, with a thin line growing between the two words
        float textT = EaseOut((t - 1.85f) / 0.6f);
        if (textT > 0f) {
            float ty = cy + orbR * 1.78f + (1f - textT) * unit * 0.03f;
            Fill();
            _p.TextAlign = Paint.Align.Center;
            _p.SetTypeface(_bold);
            _p.TextSize = unit * 0.085f;
            _p.Color = new Color(Argb(textT, 70, 76, 70));
            canvas.DrawText("Larry Slop", cx, ty, _p);
            float lw = unit * 0.26f * textT, ly = ty + unit * 0.03f;
            Fill();
            _p.Color = new Color(Argb(0.5f * textT, 16, 124, 16));
            canvas.DrawRect(cx - lw, ly, cx + lw, ly + MathF.Max(1f, unit * 0.003f), _p);
            Fill();
            _p.TextAlign = Paint.Align.Center;
            _p.SetTypeface(_light);
            _p.TextSize = unit * 0.04f;
            _p.LetterSpacing = 0.5f;
            _p.Color = new Color(Argb(textT, 16, 124, 16));
            canvas.DrawText("SOFTWARE", cx + _p.TextSize * 0.25f, ty + unit * 0.083f, _p);
        }

        PostInvalidateOnAnimation(); // (keeps breathing until the game takes over)
    }

    /// <summary>A point along a ribbon: from off-screen (u = 0), spiralling in to the middle (u = 1).</summary>
    static PointF Spiral((float Angle, float Dir, float Delay, float Width, float Turns, int Kind) rb, float u, float cx, float cy, float w, float h, float orbR) {
        float far = MathF.Sqrt(w * w + h * h) * 0.62f;
        float radius = orbR * 0.55f + (far - orbR * 0.55f) * MathF.Pow(1f - u, 1.7f);
        float ang = rb.Angle + rb.Dir * u * rb.Turns * MathF.PI;
        return new PointF(cx + MathF.Cos(ang) * radius, cy + MathF.Sin(ang) * radius * 0.8f);
    }
}
