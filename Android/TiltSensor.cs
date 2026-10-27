using System;
using Android.Content;
using Android.Hardware;
using Android.Runtime;
using Android.Views;
using TanksRebirth.Internals.Common.Framework.Input;

namespace TanksRebirth.Droid;

/// <summary>
/// Reads the phone's tilt (gravity sensor, or the accelerometer if there's none) and hands it to the game as a
/// -1..1 offset from how the phone has been held lately, so the menu logo can lean with the phone.
/// </summary>
public sealed class TiltSensor : Java.Lang.Object, ISensorEventListener {
    private readonly SensorManager? _manager;
    private readonly Sensor? _sensor;
    private readonly IWindowManager? _windows;
    private float _baseX, _baseY;
    private bool _hasBase;
    private long _lastNs;

    public TiltSensor(Context context) {
        _manager = (SensorManager?)context.GetSystemService(Context.SensorService);
        _sensor = _manager?.GetDefaultSensor(SensorType.Gravity) ?? _manager?.GetDefaultSensor(SensorType.Accelerometer);
        _windows = context.GetSystemService(Context.WindowService).JavaCast<IWindowManager>();
    }

    public void Start() {
        if (_sensor is not null)
            _manager?.RegisterListener(this, _sensor, SensorDelay.Game);
    }

    public void Stop() {
        _manager?.UnregisterListener(this);
        _hasBase = false;
        TouchControls.Tilt = Microsoft.Xna.Framework.Vector2.Zero;
    }

    public void OnAccuracyChanged(Sensor? sensor, [GeneratedEnum] SensorStatus accuracy) { }

    public void OnSensorChanged(SensorEvent? e) {
        if (e?.Values is null || e.Values.Count < 3)
            return;
        float dx = e.Values[0], dy = e.Values[1];
        // device axes -> screen axes for the current rotation (the game runs in landscape)
#pragma warning disable CA1422
        var rotation = _windows?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation90;
#pragma warning restore CA1422
        (float x, float y) = rotation switch {
            SurfaceOrientation.Rotation90 => (-dy, dx),
            SurfaceOrientation.Rotation180 => (-dx, -dy),
            SurfaceOrientation.Rotation270 => (dy, -dx),
            _ => (dx, dy),
        };
        // drift slowly toward how the phone is being held, so it's the tilting that moves the logo
        float dt = _lastNs == 0 ? 0.02f : Math.Clamp((e.Timestamp - _lastNs) / 1e9f, 0f, 0.1f);
        _lastNs = e.Timestamp;
        if (!_hasBase) {
            _baseX = x; _baseY = y;
            _hasBase = true;
        }
        float k = dt / 2.5f;
        _baseX += (x - _baseX) * k;
        _baseY += (y - _baseY) * k;
        float tx = Math.Clamp((x - _baseX) / 4f, -1f, 1f);
        float ty = Math.Clamp((y - _baseY) / 4f, -1f, 1f);
        TouchControls.Tilt = new Microsoft.Xna.Framework.Vector2(tx, ty);
    }
}
