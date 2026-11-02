using System;
using System.Runtime.InteropServices;
using WebOverlay;

namespace HideoutRadio;

internal sealed class OverlayDrag
{
    private const int LeftButton = 1;
    private Point _start;
    private Rect _window, _work;
    private bool _active, _resize;
    private int _lastX, _lastY;
    private double _barHeight, _scale;

    internal void Begin(bool resize, double barHeight, double scale)
    {
        _active = false;
        if (!LeftDown || !GetCursorPos(out _start)) return;
        IntPtr window = GetAncestor(WindowFromPoint(_start), 2);
        GetWindowThreadProcessId(window, out uint owner);
        if (owner != GetCurrentProcessId() || !GetWindowRect(window, out _window)) return;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor)) return;

        _work = monitor.Work;
        _resize = resize;
        _barHeight = Math.Max(90, Math.Min(barHeight, 220));
        _scale = Math.Max(.5, Math.Min(scale, 4));
        _lastX = _lastY = 0;
        _active = true;
    }

    internal void Update(IWebOverlay? overlay)
    {
        if (!_active) return;
        if (overlay?.IsVisible != true || !LeftDown) { _active = false; return; }
        if (!GetCursorPos(out var cursor)) return;
        int dx = cursor.X - _start.X;
        int dy = cursor.Y - _start.Y;
        if (dx == _lastX && dy == _lastY) return;
        _lastX = dx;
        _lastY = dy;

        if (_resize)
        {
            int width = _window.Right - _window.Left;
            int minimum = Math.Min(width, (int)Math.Round(360 * _scale));
            int maximum = Math.Max(minimum, _work.Right - _window.Left);
            int minDelta = (int)Math.Ceiling((90 - _barHeight) * _scale);
            int maxDelta = Math.Max(0, Math.Min(_work.Bottom - _window.Bottom,
                (int)Math.Floor((220 - _barHeight) * _scale)));
            int heightDelta = Clamp(dy, minDelta, maxDelta);
            int height = _window.Bottom - _window.Top + heightDelta;
            overlay.SetBounds(null, null, Clamp(width + dx, minimum, maximum), height);
            overlay.Post(Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                type = "bar-height",
                height = _barHeight + heightDelta / _scale
            }));
        }
        else
        {
            // Keep the drag grip reachable, even when the library is taller than the screen.
            int width = _window.Right - _window.Left;
            int height = _window.Bottom - _window.Top;
            int x = Clamp(_window.Left + dx, _work.Left, Math.Max(_work.Left, _work.Right - width));
            int bottom = Clamp(_window.Bottom + dy, Math.Min(_work.Bottom, _work.Top + (int)Math.Ceiling(_barHeight * _scale) + 2), _work.Bottom);
            overlay.SetBounds(x, bottom - height, null, null);
        }
    }

    private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(value, maximum));
    private static bool LeftDown => (GetAsyncKeyState(LeftButton) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Bounds, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}


