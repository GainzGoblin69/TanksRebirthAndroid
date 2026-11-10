using System;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace TanksRebirth.Droid;

/// <summary>
/// The Larry Slop Software start-up splash, from the "Larry Slop splash (animated, loops)" design: a white room,
/// seven green ribbons of light sweeping in to the middle, a flash, sparks, the glossy green orb with Larry popping
/// in (a sheen sweeps across it), then the name. It loops every 7 seconds, like the design, until the game is ready.
/// All positions, sizes, colors and timings are the design's own (a 1920x1080 artboard, fitted to the screen's height).
/// It has its own surface and draws from its own thread, so it plays smoothly while the game loads.
/// </summary>
internal sealed class SplashView : SurfaceView, ISurfaceHolderCallback {
    // ---- the design's timeline: one 7 second loop, everything keyed in percent of it
    const float Cycle = 7f;
    static readonly float[] RibbonDelays = [0f, 0.12f, 0.28f, 0.05f, 0.33f, 0.2f, 0.16f];
    static readonly int[][] RibbonPoints = [
        [2385, 592, 2311, 702, 2227, 801, 2133, 889, 2031, 965, 1925, 1028, 1814, 1080, 1702, 1120, 1589, 1148, 1478, 1165, 1370, 1172, 1266, 1168, 1168, 1156, 1076, 1136, 992, 1108, 916, 1074, 848, 1035, 789, 993, 739, 947, 698, 899, 665, 851, 642, 802, 626, 754, 618, 708, 617, 664, 621, 623, 631, 585, 646, 550, 664, 520, 685, 493, 708, 471, 732, 452, 756, 437, 781, 425, 804, 416, 826, 410, 846, 406, 864, 404, 880, 402, 894, 401, 906, 399],
        [1319, 1612, 1409, 1544, 1490, 1473, 1559, 1399, 1619, 1323, 1669, 1246, 1708, 1168, 1738, 1091, 1758, 1014, 1770, 940, 1773, 868, 1768, 798, 1757, 732, 1738, 670, 1714, 612, 1685, 558, 1651, 510, 1614, 466, 1574, 426, 1531, 392, 1487, 363, 1442, 339, 1397, 319, 1353, 304, 1309, 293, 1266, 286, 1226, 282, 1187, 282, 1151, 285, 1118, 290, 1088, 297, 1061, 306, 1037, 316, 1015, 326, 997, 338, 982, 349, 969, 359, 958, 369, 949, 378, 941, 385, 933, 389],
        [572, 1605, 428, 1518, 303, 1421, 198, 1315, 114, 1203, 50, 1088, 7, 971, -16, 856, -21, 744, -9, 637, 19, 536, 61, 444, 115, 360, 179, 287, 251, 224, 328, 172, 409, 131, 492, 100, 574, 81, 655, 70, 732, 69, 804, 77, 870, 91, 930, 111, 983, 136, 1027, 165, 1064, 197, 1093, 230, 1115, 264, 1130, 297, 1138, 329, 1141, 359, 1139, 387, 1133, 412, 1124, 434, 1113, 453, 1101, 469, 1089, 482, 1077, 493, 1066, 503, 1058, 512],
        [-462, 614, -394, 692, -320, 763, -242, 827, -161, 883, -78, 931, 8, 972, 94, 1005, 179, 1032, 264, 1051, 348, 1064, 429, 1070, 508, 1071, 583, 1066, 654, 1057, 721, 1042, 783, 1024, 841, 1002, 893, 978, 940, 950, 982, 921, 1018, 891, 1050, 859, 1076, 827, 1097, 795, 1114, 763, 1126, 732, 1135, 702, 1139, 673, 1140, 646, 1138, 621, 1134, 598, 1128, 577, 1120, 558, 1112, 541, 1103, 526, 1093, 513, 1085, 502, 1077, 493, 1071, 485, 1068, 478],
        [-172, -244, -31, -311, 113, -363, 258, -399, 402, -421, 543, -428, 678, -421, 807, -403, 927, -373, 1037, -333, 1136, -285, 1224, -230, 1299, -170, 1363, -105, 1413, -38, 1452, 31, 1479, 99, 1495, 166, 1500, 231, 1496, 293, 1483, 350, 1463, 403, 1436, 450, 1404, 492, 1369, 528, 1330, 558, 1290, 582, 1249, 600, 1209, 613, 1170, 622, 1133, 626, 1098, 626, 1066, 622, 1038, 617, 1013, 609, 992, 600, 973, 590, 958, 581, 945, 572, 934, 564, 924, 558],
        [1308, -664, 1173, -642, 1046, -611, 928, -572, 818, -524, 719, -470, 630, -410, 552, -346, 485, -279, 429, -209, 383, -137, 348, -66, 323, 5, 308, 75, 302, 143, 305, 208, 315, 269, 331, 326, 355, 379, 383, 427, 415, 471, 451, 509, 490, 541, 530, 569, 572, 592, 613, 609, 654, 623, 694, 631, 732, 636, 768, 638, 802, 636, 833, 632, 860, 626, 885, 618, 907, 609, 925, 599, 941, 590, 954, 581, 965, 572, 975, 566, 984, 561],
        [2010, -323, 2081, -184, 2127, -43, 2150, 96, 2150, 231, 2129, 360, 2089, 481, 2032, 592, 1960, 691, 1877, 778, 1784, 852, 1684, 912, 1580, 959, 1475, 992, 1370, 1012, 1267, 1020, 1170, 1016, 1078, 1003, 995, 980, 920, 950, 855, 913, 799, 872, 754, 828, 719, 782, 694, 735, 679, 689, 672, 645, 672, 603, 680, 564, 692, 530, 710, 500, 730, 474, 753, 452, 777, 435, 801, 422, 824, 413, 846, 406, 867, 402, 885, 399, 901, 397, 915, 395],
    ];
    static readonly (float X, float Y, float R, bool White)[] Sparks = [
        (1200, 475, 7, false), (1190, 608, 5, true), (1080, 683, 8, false), (960, 740, 5, false),
        (840, 683, 7, true), (730, 608, 6, false), (720, 475, 8, false), (730, 342, 5, true),
        (840, 267, 7, false), (960, 210, 6, false), (1080, 267, 5, true), (1190, 342, 8, false),
    ];

    readonly Bitmap? _face;
    readonly Paint _p = new(PaintFlags.AntiAlias);
    readonly Path _seg = new(), _clip = new();
    readonly PathMeasure[] _ribbons;
    readonly float[] _ribbonLength;
    readonly Typeface _medium, _light;
    long _start = SystemClock.UptimeMillis();
    bool _started;

    // the drawing thread
    System.Threading.Thread? _thread;
    volatile bool _running;
    long _fadeStart = -1;
    Action? _onGone;
    const float FadeSeconds = 0.45f;

    /// <summary>Seconds since the splash started playing (0 until its surface exists).</summary>
    public float Elapsed => _started ? (SystemClock.UptimeMillis() - _start) / 1000f : 0f;

    /// <summary>The logo and name are fully in and not about to loop (a good moment to leave).</summary>
    public bool AtRest {
        get {
            float e = Elapsed;
            if (e < Cycle * 0.4f)
                return false; // the first time round, let the whole intro play
            float pct = e % Cycle / Cycle * 100f;
            return pct is >= 38f and <= 86f;
        }
    }

    public SplashView(Context context, Bitmap? face) : base(context) {
        _face = face;
        _ribbons = new PathMeasure[RibbonPoints.Length];
        _ribbonLength = new float[RibbonPoints.Length];
        for (int i = 0; i < RibbonPoints.Length; i++) {
            var pts = RibbonPoints[i];
            var path = new Path();
            path.MoveTo(pts[0], pts[1]);
            for (int k = 2; k < pts.Length; k += 2)
                path.LineTo(pts[k], pts[k + 1]);
            _ribbons[i] = new PathMeasure(path, false);
            _ribbonLength[i] = _ribbons[i].Length;
        }
        _medium = LoadFont(context, "Splash/Inter-Medium.ttf") ?? Typeface.Create("sans-serif-medium", TypefaceStyle.Normal)!;
        _light = LoadFont(context, "Splash/Inter-Light.ttf") ?? Typeface.Create("sans-serif-light", TypefaceStyle.Normal)!;
        // above the game's own surface, and see-through (so it can fade out to reveal the game)
        SetZOrderOnTop(true);
        Holder!.SetFormat(Format.Translucent);
        Holder.AddCallback(this);
    }

    static Typeface? LoadFont(Context context, string asset) {
        try { return Typeface.CreateFromAsset(context.Assets, asset); }
        catch { return null; }
    }

    /// <summary>Fades the splash out, then calls <paramref name="gone"/> (on the drawing thread) once nothing is left.</summary>
    public void FadeOut(Action gone) {
        _onGone = gone;
        _fadeStart = SystemClock.UptimeMillis();
        if (!_running)
            gone(); // (never got a surface)
    }

    public void SurfaceCreated(ISurfaceHolder holder) {
        if (_running)
            return;
        if (!_started) {
            _started = true;
            _start = SystemClock.UptimeMillis();
        }
        _running = true;
        _thread = new System.Threading.Thread(Loop) { IsBackground = true, Name = "Splash" };
        _thread.Start();
    }

    public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height) { }

    public void SurfaceDestroyed(ISurfaceHolder holder) {
        _running = false;
        try { _thread?.Join(500); } catch { }
    }

    void Loop() {
        var holder = Holder!;
        bool hardware = Build.VERSION.SdkInt >= BuildVersionCodes.O;
        bool broken = false;
        while (_running) {
            float fade = _fadeStart < 0 ? 1f : 1f - (SystemClock.UptimeMillis() - _fadeStart) / 1000f / FadeSeconds;
            Canvas? canvas = null;
            try {
                try { canvas = hardware ? holder.LockHardwareCanvas() : holder.LockCanvas(); }
                catch { hardware = false; }
                if (canvas is null) {
                    System.Threading.Thread.Sleep(16);
                    continue;
                }
                canvas.DrawColor(Color.Transparent, PorterDuff.Mode.Clear!);
                if (fade > 0f) {
                    int w = canvas.Width, h = canvas.Height;
                    bool layer = fade < 1f;
                    if (layer)
                        canvas.SaveLayerAlpha(0, 0, w, h, (int)(255 * Clamp01(fade)));
                    if (broken)
                        canvas.DrawColor(Color.White); // (a plain white screen rather than ever stopping the game from starting)
                    else {
                        try { Draw(canvas, Elapsed, w, h); }
                        catch (Exception e) {
                            broken = true;
                            Android.Util.Log.Warn("TanksRebirth", "Splash animation failed: " + e);
                        }
                    }
                    if (layer)
                        canvas.Restore();
                }
            }
            catch (Exception e) {
                Android.Util.Log.Warn("TanksRebirth", "Splash surface failed: " + e.Message);
                fade = 0f;
            }
            finally {
                if (canvas is not null) {
                    try { holder.UnlockCanvasAndPost(canvas); } catch { }
                }
            }
            if (fade <= 0f) {
                _running = false;
                try { _onGone?.Invoke(); } catch { }
                return;
            }
            System.Threading.Thread.Sleep(hardware ? 6 : 12);
        }
    }

    // ---- CSS timing: cubic-bezier easing, applied between each pair of keyframes
    static float Clamp01(float v) => MathF.Max(0f, MathF.Min(1f, v));

    static Func<float, float> Bezier(float x1, float y1, float x2, float y2) => x => {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        float lo = 0f, hi = 1f, t = 0.5f;
        for (int i = 0; i < 24; i++) {
            t = (lo + hi) * 0.5f;
            float bx = 3f * (1 - t) * (1 - t) * t * x1 + 3f * (1 - t) * t * t * x2 + t * t * t;
            if (bx < x) lo = t; else hi = t;
        }
        t = (lo + hi) * 0.5f;
        return 3f * (1 - t) * (1 - t) * t * y1 + 3f * (1 - t) * t * t * y2 + t * t * t;
    };
    static readonly Func<float, float> EaseInOut = Bezier(0.42f, 0f, 0.58f, 1f);
    static readonly Func<float, float> EaseOut = Bezier(0f, 0f, 0.58f, 1f);
    static readonly Func<float, float> Linear = x => Clamp01(x);
    static readonly Func<float, float> OrbEase = Bezier(0.3f, 0.9f, 0.4f, 1f);

    /// <summary>A value along keyframes (percent, value), eased between each pair.</summary>
    static float Key(float pct, Func<float, float> ease, params float[] frames) {
        if (pct <= frames[0])
            return frames[1];
        for (int i = 0; i + 3 < frames.Length; i += 2) {
            float p0 = frames[i], v0 = frames[i + 1], p1 = frames[i + 2], v1 = frames[i + 3];
            if (pct <= p1) {
                float u = p1 == p0 ? 1f : (pct - p0) / (p1 - p0);
                return v0 + (v1 - v0) * ease(u);
            }
        }
        return frames[^1];
    }

    /// <summary>Where in its loop an animation is, in percent (a delayed one waits on its first frame).</summary>
    static float Pct(float t, float delay = 0f) {
        t -= delay;
        return t < 0f ? 0f : t % Cycle / Cycle * 100f;
    }

    static int Argb(float a, int rgb) => ((int)MathF.Round(255f * Clamp01(a)) << 24) | (rgb & 0xFFFFFF);
    static readonly Shader.TileMode Clamp = Shader.TileMode.Clamp!;

    void Plain() {
        _p.Reset();
        _p.AntiAlias = true;
        _p.FilterBitmap = true;
    }

    void Draw(Canvas c, float t, int w, int h) {
        // the room: white in the middle, a soft grey-green at the edges (fills the whole screen)
        float bx = w * 0.5f, by = h * 0.44f;
        float far = MathF.Sqrt(MathF.Max(bx, w - bx) * MathF.Max(bx, w - bx) + MathF.Max(by, h - by) * MathF.Max(by, h - by));
        Plain();
        _p.SetShader(new RadialGradient(bx, by, far, new[] { Argb(1, 0xFFFFFF), Argb(1, 0xFFFFFF), Argb(1, 0xDEE4DE) }, new[] { 0f, 0.3f, 1f }, Clamp));
        c.DrawRect(0, 0, w, h, _p);

        // the 1920x1080 artboard, fitted to the screen's height and centred
        float s = h / 1080f;
        c.Save();
        c.Translate((w - 1920f * s) / 2f, 0f);
        c.Scale(s, s);

        // ribbons: a 45% dash running along each path into the middle, then fading
        for (int i = 0; i < _ribbons.Length; i++) {
            float pr = Pct(t, RibbonDelays[i]);
            float off = Key(pr, EaseInOut, 0, 45, 17, -55, 100, -55);
            float op = Key(pr, EaseInOut, 0, 1, 17, 1, 22, 0, 100, 0);
            float a = MathF.Max(0f, -off), b = MathF.Min(100f, -off + 45f);
            if (op <= 0f || b <= a)
                continue;
            _seg.Reset();
            _ribbons[i].GetSegment(a / 100f * _ribbonLength[i], b / 100f * _ribbonLength[i], _seg, true);
            Stroke(c, 64f, Argb(0.18f * op, 0x7ACD3C));
            Stroke(c, 26f, Argb(0.8f * op, 0x107C10));
            Stroke(c, 8f, Argb(0.65f * op, 0xFFFFFF));
        }

        float pct = Pct(t);

        // sparks, flung out from the middle
        float sparkOp = Key(pct, EaseOut, 0, 0, 18.6f, 0, 19.5f, 0.9f, 41, 0, 100, 0);
        if (sparkOp > 0f) {
            float sc = Key(pct, EaseOut, 0, 1, 18.6f, 1, 19.5f, 1.05f, 41, 2.4f, 100, 2.4f);
            c.Save();
            c.Scale(sc, sc, 960f, 475f);
            Plain();
            foreach (var sp in Sparks) {
                _p.Color = new Color(Argb(sparkOp, sp.White ? 0xFFFFFF : 0x7ACD3C));
                c.DrawCircle(sp.X, sp.Y, sp.R, _p);
            }
            c.Restore();
        }

        // the flash
        float flashOp = Key(pct, EaseOut, 0, 0, 17, 0, 17.3f, 1, 26, 0, 100, 0);
        if (flashOp > 0f) {
            float sc = Key(pct, EaseOut, 0, 0.1f, 17, 0.1f, 17.3f, 0.1f, 26, 1, 100, 1);
            float r = 550f * MathF.Sqrt(2f) * 0.7f;
            c.Save();
            c.Translate(960f, 475f);
            c.Scale(sc, sc);
            Plain();
            _p.SetShader(new RadialGradient(0, 0, r, new[] { Argb(flashOp, 0xFFFFFF), Argb(0.45f * flashOp, 0x7ACD3C), Argb(0, 0x7ACD3C) }, new[] { 0f, 0.45f / 0.7f, 1f }, Clamp));
            c.DrawCircle(0, 0, r, _p);
            c.Restore();
        }

        // the orb's shadow on the floor
        float shadowOp = Key(pct, EaseOut, 0, 0, 19, 0, 26, 1, 90, 1, 96, 0, 100, 0);
        if (shadowOp > 0f) {
            float r = 216f * MathF.Sqrt(2f) * 0.7f;
            c.Save();
            c.Translate(960f, 730f);
            c.Scale(1f, 30f / 216f);
            Plain();
            _p.SetShader(new RadialGradient(0, 0, r, new[] { Argb(0.24f * shadowOp, 0x002800), Argb(0, 0x002800) }, null, Clamp));
            c.DrawCircle(0, 0, r, _p);
            c.Restore();
        }

        // the orb pops in: a glossy green ball with Larry in a dark green ring
        float orbOp = Key(pct, OrbEase, 0, 0, 18.5f, 1, 23, 1, 26, 1, 90, 1, 96, 0, 100, 0);
        float orbScale = Key(pct, OrbEase, 0, 0, 18.5f, 0, 23, 1.08f, 26, 1, 90, 1, 96, 1, 100, 1);
        if (orbOp > 0f && orbScale > 0f) {
            c.Save();
            c.Translate(960f, 475f);
            c.Scale(orbScale, orbScale);
            c.Translate(-216f, -216f);
            c.SaveLayerAlpha(0, 0, 432, 432, (int)(255 * Clamp01(orbOp)));
            _clip.Reset();
            _clip.AddCircle(216, 216, 216, Path.Direction.Cw!);
            c.ClipPath(_clip);
            // the ball, lit from the upper left
            float gx = 0.35f * 432f, gy = 0.32f * 432f, gr = MathF.Sqrt((432 - gx) * (432 - gx) + (432 - gy) * (432 - gy));
            Plain();
            _p.SetShader(new RadialGradient(gx, gy, gr, new[] { Argb(1, 0xAAEB6E), Argb(1, 0x46AA28), Argb(1, 0x0E5C0E), Argb(1, 0x063208) }, new[] { 0f, 0.35f, 0.75f, 1f }, Clamp));
            c.DrawRect(0, 0, 432, 432, _p);
            // Larry's ring, then Larry
            Plain();
            _p.Color = new Color(Argb(1, 0x083C08));
            c.DrawCircle(216, 216, 173, _p);
            if (_face is not null) {
                c.Save();
                _clip.Reset();
                _clip.AddCircle(216, 216, 163, Path.Direction.Cw!);
                c.ClipPath(_clip);
                Plain();
                c.DrawBitmap(_face, null, new RectF(53, 53, 379, 379), _p);
                c.Restore();
            }
            // gloss across the top
            Plain();
            _p.SetShader(new LinearGradient(0, 10, 0, 204, new[] { Argb(0.62f, 0xFFFFFF), Argb(0, 0xFFFFFF) }, null, Clamp));
            c.DrawOval(new RectF(60, 10, 372, 204), _p);
            // the sheen sweeping across, once a loop
            float sheenOp = Key(pct, Linear, 0, 0, 27, 0, 27.2f, 1, 36, 1, 36.2f, 0, 100, 0);
            if (sheenOp > 0f) {
                float x0 = 151f + Key(pct, Linear, 0, -420, 27, -420, 27.2f, -420, 36, 420, 36.2f, 420, 100, 420);
                Plain();
                _p.SetShader(new LinearGradient(x0, 0, x0 + 130, 0, new[] { Argb(0, 0xFFFFFF), Argb(0.5f * sheenOp, 0xFFFFFF), Argb(0, 0xFFFFFF) }, null, Clamp));
                c.DrawRect(x0, 0, x0 + 130, 432, _p);
            }
            c.Restore(); // (the layer)
            c.Restore();
        }

        // the name rises in under the orb
        float nameOp = Key(pct, EaseOut, 0, 0, 26.4f, 0, 35, 1, 90, 1, 96, 0, 100, 0);
        if (nameOp > 0f) {
            float rise = Key(pct, EaseOut, 0, 32, 26.4f, 32, 35, 0, 100, 0);
            // (Inter: the baseline sits 0.8638 of the size below the top of a line-height 1 box)
            Spaced(c, "Larry Slop", _medium, 92f, 0.02f, 770f + 0.8638f * 92f + rise, Argb(nameOp, 0x484C48));
            Spaced(c, "SOFTWARE", _light, 44f, 0.45f, 876f + 0.8638f * 44f + rise, Argb(nameOp, 0x107C10));
        }

        c.Restore();
    }

    void Stroke(Canvas c, float width, int color) {
        Plain();
        _p.SetStyle(Paint.Style.Stroke);
        _p.StrokeWidth = width;
        _p.StrokeCap = Paint.Cap.Round;
        _p.StrokeJoin = Paint.Join.Round;
        _p.Color = new Color(color);
        c.DrawPath(_seg, _p);
    }

    /// <summary>Text centred on the artboard, with CSS-style letter spacing (in em) between the letters.</summary>
    void Spaced(Canvas c, string text, Typeface face, float size, float spacingEm, float baseline, int color) {
        Plain();
        _p.SetTypeface(face);
        _p.TextSize = size;
        _p.Color = new Color(color);
        float gap = spacingEm * size, total = 0f;
        var widths = new float[text.Length];
        for (int i = 0; i < text.Length; i++) {
            widths[i] = _p.MeasureText(text, i, i + 1);
            total += widths[i] + (i < text.Length - 1 ? gap : 0f);
        }
        float x = 960f - total / 2f;
        for (int i = 0; i < text.Length; i++) {
            c.DrawText(text, i, i + 1, x, baseline, _p);
            x += widths[i] + gap;
        }
    }
}
