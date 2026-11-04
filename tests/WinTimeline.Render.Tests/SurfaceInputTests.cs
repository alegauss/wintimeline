namespace WinTimeline.Render.Tests;

/// <summary>WT152: what the surface's child window receives becomes the surface's events.</summary>
public sealed class SurfaceInputTests
{
    private static nint Point(int x, int y) => (nint)((ushort)(short)x | ((ushort)(short)y << 16));

    [Fact]
    public void AWheelTurnIsMappedFromTheScreenIntoTheSurfaceWithItsKeys()
    {
        // One notch away from the user, Ctrl held, at screen (1100, 640); the surface's origin is (1000, 600).
        nint wParam = (nint)((120 << 16) | 0x0008);

        SurfaceWheel wheel = Assert.IsType<SurfaceWheel>(SurfaceInput.Wheel(SurfaceInput.MouseWheel, wParam, Point(1100, 640), p => (p.X - 1000, p.Y - 600)));

        Assert.Equal(new SurfaceWheel(100, 40, 120, Horizontal: false, Shift: false, Control: true), wheel);
        Assert.True(SurfaceInput.Wheel(SurfaceInput.MouseHWheel, (nint)(-120 << 16), Point(0, 0), p => p)!.Horizontal);
        Assert.Null(SurfaceInput.Wheel(SurfaceInput.LeftDown, 0, 0, p => p));
    }

    [Fact]
    public void APressADragAndAReleaseReachTheHandlerInOrder()
    {
        SurfacePointer down = SurfaceInput.Mouse(SurfaceInput.LeftDown, 0x0001, Point(10, 20))!;
        SurfacePointer drag = SurfaceInput.Mouse(SurfaceInput.MouseMove, 0x0001 | 0x0004, Point(-5, 25))!;
        SurfacePointer hover = SurfaceInput.Mouse(SurfaceInput.MouseMove, 0, Point(30, 30))!;
        SurfacePointer up = SurfaceInput.Mouse(SurfaceInput.LeftUp, 0, Point(40, 20))!;

        Assert.Equal((PointerPhase.Down, true, 10, 20), (down.Phase, down.Pressed, down.X, down.Y));
        Assert.Equal((PointerPhase.Move, true, -5, true), (drag.Phase, drag.Pressed, drag.X, drag.Shift));
        Assert.False(hover.Pressed);
        Assert.Equal((PointerPhase.Up, false, 0f), (up.Phase, up.Pressed, up.Pressure));
        Assert.Null(SurfaceInput.Mouse(SurfaceInput.MouseWheel, 0, 0));
    }

    [Fact]
    public void APenCarriesItsPressure()
    {
        SurfacePointer press = SurfaceInput.Pen(SurfaceInput.PointerDown, Point(500, 300), 512, p => (p.X - 400, p.Y - 200))!;
        SurfacePointer lift = SurfaceInput.Pen(SurfaceInput.PointerUp, Point(500, 300), 0, p => p)!;

        Assert.Equal((PointerKind.Pen, 100, 100, 0.5f, true), (press.Kind, press.X, press.Y, press.Pressure, press.Pressed));
        Assert.Equal((PointerPhase.Up, false), (lift.Phase, lift.Pressed));
    }
}
