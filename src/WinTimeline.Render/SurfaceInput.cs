namespace WinTimeline.Render;

/// <summary>Where a press is in its life.</summary>
public enum PointerPhase
{
    /// <summary>Pressed.</summary>
    Down,

    /// <summary>Moved, pressed or not.</summary>
    Move,

    /// <summary>Released.</summary>
    Up,
}

/// <summary>What is pointing.</summary>
public enum PointerKind
{
    /// <summary>A mouse, a touchpad, or touch Windows turned into mouse messages.</summary>
    Mouse,

    /// <summary>A pen, with pressure.</summary>
    Pen,
}

/// <summary>A press, drag or release over a surface, in device pixels.</summary>
/// <param name="Phase">Down, Move or Up.</param>
/// <param name="Kind">Mouse or pen.</param>
/// <param name="X">Across.</param>
/// <param name="Y">Down.</param>
/// <param name="Pressed">Whether the primary button or the pen tip is down.</param>
/// <param name="Pressure">0 to 1 for a pen; 1 for a pressed mouse, 0 otherwise.</param>
/// <param name="Shift">Whether Shift was down.</param>
/// <param name="Control">Whether Control was down.</param>
public sealed record SurfacePointer(PointerPhase Phase, PointerKind Kind, int X, int Y, bool Pressed, float Pressure, bool Shift, bool Control);

/// <summary>Files dropped on a surface, and where.</summary>
/// <param name="Paths">The files, in the order the drop carried them.</param>
/// <param name="X">Across, in device pixels.</param>
/// <param name="Y">Down, in device pixels.</param>
public sealed record SurfaceDrop(IReadOnlyList<string> Paths, int X, int Y);

/// <summary>
/// WT152: the window messages a surface's child window receives, decoded into the surface's
/// events. Pure, so what a message means is tested without a window.
/// </summary>
public static class SurfaceInput
{
    /// <summary>WM_MOUSEMOVE.</summary>
    public const uint MouseMove = 0x0200;

    /// <summary>WM_LBUTTONDOWN.</summary>
    public const uint LeftDown = 0x0201;

    /// <summary>WM_LBUTTONUP.</summary>
    public const uint LeftUp = 0x0202;

    /// <summary>WM_MOUSEWHEEL.</summary>
    public const uint MouseWheel = 0x020A;

    /// <summary>WM_MOUSEHWHEEL.</summary>
    public const uint MouseHWheel = 0x020E;

    /// <summary>WM_POINTERUPDATE.</summary>
    public const uint PointerUpdate = 0x0245;

    /// <summary>WM_POINTERDOWN.</summary>
    public const uint PointerDown = 0x0246;

    /// <summary>WM_POINTERUP.</summary>
    public const uint PointerUp = 0x0247;

    /// <summary>WM_DROPFILES.</summary>
    public const uint DropFiles = 0x0233;

    private const int MkLeft = 0x0001, MkShift = 0x0004, MkControl = 0x0008;

    /// <summary>A wheel or tilt message, its screen point mapped into the surface.</summary>
    /// <param name="msg">The message.</param>
    /// <param name="wParam">Its wParam.</param>
    /// <param name="lParam">Its lParam.</param>
    /// <param name="toClient">Maps a screen point into the surface's pixels.</param>
    /// <returns>The wheel event, or null for another message.</returns>
    public static SurfaceWheel? Wheel(uint msg, nint wParam, nint lParam, Func<(int X, int Y), (int X, int Y)> toClient)
    {
        ArgumentNullException.ThrowIfNull(toClient);
        if (msg is not (MouseWheel or MouseHWheel))
        {
            return null;
        }

        (int x, int y) = toClient(Point(lParam));
        int keys = (int)(wParam & 0xFFFF);
        return new SurfaceWheel(x, y, (short)((wParam >> 16) & 0xFFFF), msg == MouseHWheel, (keys & MkShift) != 0, (keys & MkControl) != 0);
    }

    /// <summary>A mouse button or move message; its point is already in the surface's pixels.</summary>
    /// <param name="msg">The message.</param>
    /// <param name="wParam">Its wParam: the keys and buttons down.</param>
    /// <param name="lParam">Its lParam: the point.</param>
    /// <returns>The pointer event, or null for another message.</returns>
    public static SurfacePointer? Mouse(uint msg, nint wParam, nint lParam)
    {
        PointerPhase? phase = msg switch
        {
            LeftDown => PointerPhase.Down,
            MouseMove => PointerPhase.Move,
            LeftUp => PointerPhase.Up,
            _ => null,
        };
        if (phase is not { } p)
        {
            return null;
        }

        int keys = (int)(wParam & 0xFFFF);
        bool pressed = p == PointerPhase.Down || (p == PointerPhase.Move && (keys & MkLeft) != 0);
        (int x, int y) = Point(lParam);
        return new SurfacePointer(p, PointerKind.Mouse, x, y, pressed, pressed ? 1 : 0, (keys & MkShift) != 0, (keys & MkControl) != 0);
    }

    /// <summary>A pen pointer message, with the pressure the pen reported (0 to 1024).</summary>
    /// <param name="msg">The message.</param>
    /// <param name="lParam">Its lParam: the screen point.</param>
    /// <param name="rawPressure">The pen's pressure, 0 to 1024.</param>
    /// <param name="toClient">Maps a screen point into the surface's pixels.</param>
    /// <returns>The pointer event, or null for another message.</returns>
    public static SurfacePointer? Pen(uint msg, nint lParam, uint rawPressure, Func<(int X, int Y), (int X, int Y)> toClient)
    {
        ArgumentNullException.ThrowIfNull(toClient);
        PointerPhase? phase = msg switch
        {
            PointerDown => PointerPhase.Down,
            PointerUpdate => PointerPhase.Move,
            PointerUp => PointerPhase.Up,
            _ => null,
        };
        if (phase is not { } p)
        {
            return null;
        }

        (int x, int y) = toClient(Point(lParam));
        float pressure = Math.Clamp(rawPressure / 1024f, 0, 1);
        return new SurfacePointer(p, PointerKind.Pen, x, y, p != PointerPhase.Up && pressure > 0, p == PointerPhase.Up ? 0 : pressure, false, false);
    }

    private static (int X, int Y) Point(nint lParam) => ((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
}
