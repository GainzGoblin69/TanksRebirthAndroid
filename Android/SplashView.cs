using System;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace TanksRebirth.Droid;

/// <summary>
/// The Larry Slop Software start-up animation, in the spirit of an old console boot screen: a bright white room,
/// green ribbons of light spiralling in to the middle, a flash, then a glossy green orb with Larry's face pops in,
/// and the name fades in under it. Drawn with plain Android graphics, so it plays while the game is still loading.
/// </summary>
internal sealed class SplashView : View {
    readonly Bitmap? _face;
    readonly long _start = SystemClock.UptimeMillis();
    readonly Paint _p = new(PaintFlags.AntiAlias);
    readonly Path _path = new();
    readonly Random _rng = new(7);
    readonly (float Angle, float Dir, float Delay, float Width, float Turns)[] _ribbons;
    readonly (float Angle, float Speed, float Size, bool White)[] _sparks;
    readonly Typeface _light = Typeface.Create("sans-serif-light", TypefaceStyle.Normal)!;
    readonly Typeface _bold = Typeface.Create("sans-serif-medium", TypefaceStyle.Normal)!;

    static readonly Color Green = Color.Rgb(16, 124, 16);
    static readonly Color Lime = Color.Rgb(122, 205, 60);

    /// <summary>How long the animation runs (seconds); it holds its last frame after that.</summary>
    public const float Length = 2.8f;

    public SplashView(Context context, Bitmap? face) : base(context) {
        _face = face;
        _ribbons = new (float, float, float, float, float)[7];
        for (int i = 0; i < _ribbons.Length; i++)
            _ribbons[i] = (i * MathF.Tau / _ribbons.Length + (float)_rng.NextDouble() * 0.5f, i % 2 == 0 ? 1f : -1f,
                (float)_rng.NextDouble() * 0.35f, 0.6f + (float)_rng.NextDouble() * 0.6f, 0.9f + (float)_rng.NextDouble() * 0.7f);
        _sparks = new (float, float, float, bool)[36];
        for (int i = 0; i < _sparks.Length; i++)
            _sparks[i] = ((float)_rng.NextDouble() * MathF.Tau, 0.35f + (float)_rng.NextDouble() * 0.9f, 0.4f + (float)_rng.NextDouble(), _rng.Next(3) == 0);
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
    static Color WithAlpha(Color c, float a) => Color.Argb((int)(255 * Clamp01(a)), c.R, c.G, c.B);

    protected override void OnDraw(Canvas canvas) {
        base.OnDraw(canvas);
        float t = (SystemClock.UptimeMillis() - _start) / 1000f;
        float w = Width, h = Height;
        float cx = w / 2f, cy = h * 0.44f;
        float unit = MathF.Min(w, h);
        float orbR = unit * 0.2f;

        // a bright white room, a touch greyer at the edges
        _p.Reset();
        _p.AntiAlias = true;
        _p.SetShader(new RadialGradient(cx, cy, MathF.Max(w, h) * 0.75f, Color.White, Color.Rgb(222, 228, 222), Shader.TileMode.Clamp!));
        canvas.DrawRect(0, 0, w, h, _p);
        _p.SetShader(null);

        // green ribbons of light spiralling in to the middle
        for (int i = 0; i < _ribbons.Length; i++) {
            var rb = _ribbons[i];
            float head = EaseInOut((t - rb.Delay) / 1.15f);
            if (head <= 0f)
                continue;
            float fadeOut = 1f - Clamp01((t - 1.25f) / 0.3f);
            if (fadeOut <= 0f)
                continue;
            float tail = MathF.Max(0f, head - 0.45f);
            const int segs = 28;
            for (int s = 0; s < segs; s++) {
                float u0 = tail + (head - tail) * s / segs, u1 = tail + (head - tail) * (s + 1) / segs;
                var a = Spiral(rb, u0, cx, cy, w, h, orbR);
                var b = Spiral(rb, u1, cx, cy, w, h, orbR);
                float along = (s + 1f) / segs; // 0 at the tail, 1 at the head
                float thick = unit * 0.05f * rb.Width * (0.25f + 0.75f * along) * (1f - 0.5f * head);
                _p.SetStyle(Paint.Style.Stroke);
                _p.StrokeCap = Paint.Cap.Round;
                // a soft wide glow, then the bright core
                _p.StrokeWidth = thick * 2.4f;
                _p.Color = WithAlpha(Lime, 0.16f * along * fadeOut);
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _p);
                _p.StrokeWidth = thick;
                _p.Color = WithAlpha(Green, 0.75f * along * fadeOut);
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _p);
                _p.StrokeWidth = thick * 0.35f;
                _p.Color = WithAlpha(Color.White, 0.6f * along * fadeOut);
                canvas.DrawLine(a.X, a.Y, b.X, b.Y, _p);
            }
        }
        _p.SetStyle(Paint.Style.Fill);

        // the flash where they meet
        float flashT = (t - 1.2f) / 0.6f;
        if (flashT is > 0f and < 1f) {
            float fr = unit * (0.1f + 0.9f * EaseOut(flashT));
            float fa = 1f - flashT;
            _p.SetShader(new RadialGradient(cx, cy, fr, new int[] { WithAlpha(Color.White, fa), WithAlpha(Lime, 0.45f * fa), WithAlpha(Lime, 0f) },
                new float[] { 0f, 0.45f, 1f }, Shader.TileMode.Clamp!));
            canvas.DrawCircle(cx, cy, fr, _p);
            _p.SetShader(null);
        }

        // sparkles drifting out after the flash
        float sparkT = t - 1.3f;
        if (sparkT > 0f && sparkT < 1.6f) {
            foreach (var sp in _sparks) {
                float d = orbR * (1.05f + sp.Speed * 2.2f * EaseOut(sparkT / 1.6f));
                float a = (1f - sparkT / 1.6f) * 0.9f;
                _p.Color = WithAlpha(sp.White ? Color.White : Lime, a);
                canvas.DrawCircle(cx + MathF.Cos(sp.Angle) * d, cy + MathF.Sin(sp.Angle) * d, unit * 0.006f * sp.Size, _p);
            }
        }

        // the orb pops in: a glossy green ball with Larry in the middle
        float orbT = (t - 1.3f) / 0.5f;
        if (orbT > 0f) {
            float scale = Pop(orbT);
            float r = orbR * scale;
            // shadow on the floor
            _p.SetShader(new RadialGradient(cx, cy + orbR * 1.25f, orbR * 1.1f, Color.Argb(60, 0, 40, 0), Color.Argb(0, 0, 40, 0), Shader.TileMode.Clamp!));
            canvas.Save();
            canvas.Scale(1f, 0.22f, cx, cy + orbR * 1.25f);
            canvas.DrawCircle(cx, cy + orbR * 1.25f, orbR * 1.1f * scale, _p);
            canvas.Restore();
            // the green ball
            _p.SetShader(new RadialGradient(cx - r * 0.3f, cy - r * 0.35f, r * 1.5f,
                new int[] { Color.Rgb(170, 235, 110), Color.Rgb(70, 170, 40), Color.Rgb(14, 92, 14), Color.Rgb(6, 50, 8) },
                new float[] { 0f, 0.35f, 0.75f, 1f }, Shader.TileMode.Clamp!));
            canvas.DrawCircle(cx, cy, r, _p);
            _p.SetShader(null);
            // Larry, in a round window
            float fr = r * 0.8f;
            if (_face is not null) {
                canvas.Save();
                _path.Reset();
                _path.AddCircle(cx, cy, fr, Path.Direction.Cw!);
                canvas.ClipPath(_path);
                _p.FilterBitmap = true;
                canvas.DrawBitmap(_face, null, new RectF(cx - fr, cy - fr, cx + fr, cy + fr), _p);
                // a little green tint at the rim, so he sits inside the ball
                _p.SetShader(new RadialGradient(cx, cy, fr, new int[] { Color.Argb(0, 10, 80, 10), Color.Argb(0, 10, 80, 10), Color.Argb(150, 10, 80, 10) },
                    new float[] { 0f, 0.72f, 1f }, Shader.TileMode.Clamp!));
                canvas.DrawCircle(cx, cy, fr, _p);
                _p.SetShader(null);
                canvas.Restore();
            }
            // a dark rim around the window
            _p.SetStyle(Paint.Style.Stroke);
            _p.StrokeWidth = r * 0.05f;
            _p.Color = Color.Rgb(8, 60, 8);
            canvas.DrawCircle(cx, cy, fr, _p);
            _p.SetStyle(Paint.Style.Fill);
            // glossy shine across the top
            _p.SetShader(new LinearGradient(0, cy - r, 0, cy, Color.Argb(170, 255, 255, 255), Color.Argb(0, 255, 255, 255), Shader.TileMode.Clamp!));
            canvas.DrawOval(new RectF(cx - r * 0.72f, cy - r * 0.95f, cx + r * 0.72f, cy - r * 0.05f), _p);
            _p.SetShader(null);
            // a sweep of light across the ball, once, just after it lands
            float sweep = (t - 1.9f) / 0.6f;
            if (sweep is > 0f and < 1f) {
                canvas.Save();
                _path.Reset();
                _path.AddCircle(cx, cy, r, Path.Direction.Cw!);
                canvas.ClipPath(_path);
                float sx = cx - r * 1.6f + r * 3.2f * sweep;
                _p.SetShader(new LinearGradient(sx - r * 0.3f, 0, sx + r * 0.3f, 0,
                    new int[] { Color.Argb(0, 255, 255, 255), Color.Argb(120, 255, 255, 255), Color.Argb(0, 255, 255, 255) }, null, Shader.TileMode.Clamp!));
                canvas.DrawRect(cx - r, cy - r, cx + r, cy + r, _p);
                _p.SetShader(null);
                canvas.Restore();
            }
        }

        // the name fades and slides in under the orb
        float textT = EaseOut((t - 1.85f) / 0.6f);
        if (textT > 0f) {
            _p.TextAlign = Paint.Align.Center;
            float ty = cy + orbR * 1.75f + (1f - textT) * unit * 0.03f;
            _p.SetTypeface(_bold);
            _p.TextSize = unit * 0.085f;
            _p.LetterSpacing = 0.02f;
            _p.Color = WithAlpha(Color.Rgb(72, 76, 72), textT);
            canvas.DrawText("Larry Slop", cx, ty, _p);
            _p.SetTypeface(_light);
            _p.TextSize = unit * 0.042f;
            _p.LetterSpacing = 0.45f;
            _p.Color = WithAlpha(Green, textT);
            canvas.DrawText("SOFTWARE", cx, ty + unit * 0.07f, _p);
            _p.LetterSpacing = 0f;
        }

        if (t < Length + 0.2f)
            PostInvalidateOnAnimation();
    }

    /// <summary>A point along a ribbon: from off-screen (u = 0), spiralling in to the orb's edge (u = 1).</summary>
    static PointF Spiral((float Angle, float Dir, float Delay, float Width, float Turns) rb, float u, float cx, float cy, float w, float h, float orbR) {
        float far = MathF.Sqrt(w * w + h * h) * 0.65f;
        float radius = orbR * 0.9f + (far - orbR * 0.9f) * MathF.Pow(1f - u, 1.6f);
        float ang = rb.Angle + rb.Dir * u * rb.Turns * MathF.PI;
        return new PointF(cx + MathF.Cos(ang) * radius, cy + MathF.Sin(ang) * radius * 0.82f);
    }
}
