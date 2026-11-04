using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using AlphaMode = Vortice.DCommon.AlphaMode;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace WinTimeline.Render;

/// <summary>
/// WT47: Direct2D and DirectWrite on the app's one device, for what is drawn as shapes and words
/// rather than video: the timeline's lanes, clips, names and playhead. Coordinates are device
/// pixels. Text layouts are cached by text, size and width bucket, so a frame of thousands of
/// clip names lays out only the names it has not seen.
/// </summary>
public sealed class Canvas2D : IDisposable
{
    private const int LayoutCacheLimit = 8192;
    private const float WidthBucket = 16;

    private readonly ID2D1Factory1 _factory;
    private readonly ID2D1Device _device;
    private readonly ID2D1DeviceContext _context;
    private readonly IDWriteFactory _write;
    private readonly ID2D1SolidColorBrush _brush;
    private readonly Dictionary<float, IDWriteTextFormat> _formats = [];
    private readonly Dictionary<(string Text, float Size, float Width), IDWriteTextLayout> _layouts = [];
    private ID2D1Bitmap1? _target;

    /// <summary>Creates the canvas on a device.</summary>
    /// <param name="gpu">The app's one device, created with BGRA support.</param>
    public Canvas2D(GpuContext gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        using IDXGIDevice dxgi = gpu.Device.QueryInterface<IDXGIDevice>();
        _device = _factory.CreateDevice(dxgi);
        _context = _device.CreateDeviceContext(DeviceContextOptions.None);
        _context.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        _write = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _brush = _context.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
    }

    /// <summary>How many text layouts are cached, for tests and the frame-time overlay.</summary>
    public int CachedLayouts => _layouts.Count;

    /// <summary>Starts drawing into a surface, such as a swap chain's back buffer or an offscreen texture.</summary>
    /// <param name="surface">The surface, B8G8R8A8.</param>
    public void Begin(IDXGISurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _target = _context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96, BitmapOptions.Target | BitmapOptions.CannotDraw));
        _context.Target = _target;
        _context.BeginDraw();
        _context.Transform = Matrix3x2.Identity;
    }

    /// <summary>Finishes drawing and lets the surface go.</summary>
    public void End()
    {
        _context.EndDraw().CheckError();
        _context.Target = null;
        _target?.Dispose();
        _target = null;
    }

    /// <summary>Fills a rectangle.</summary>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">Colour.</param>
    public void Fill(float x, float y, float width, float height, Color4 color)
    {
        _brush.Color = color;
        _context.FillRectangle(new Rect(x, y, width, height), _brush);
    }

    /// <summary>Outlines a rectangle.</summary>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">Colour.</param>
    /// <param name="stroke">Line width.</param>
    public void Outline(float x, float y, float width, float height, Color4 color, float stroke = 1)
    {
        _brush.Color = color;
        _context.DrawRectangle(new Rect(x, y, width, height), _brush, stroke);
    }

    /// <summary>Draws a line.</summary>
    /// <param name="from">One end.</param>
    /// <param name="to">The other.</param>
    /// <param name="color">Colour.</param>
    /// <param name="stroke">Line width.</param>
    public void Line(Vector2 from, Vector2 to, Color4 color, float stroke = 1)
    {
        _brush.Color = color;
        _context.DrawLine(from, to, _brush, stroke);
    }

    /// <summary>Draws one line of text, clipped to a width and trimmed with an ellipsis.</summary>
    /// <param name="text">What.</param>
    /// <param name="x">Left.</param>
    /// <param name="y">Top.</param>
    /// <param name="maxWidth">How wide it may be; nothing is drawn below a few pixels.</param>
    /// <param name="size">Font size in pixels.</param>
    /// <param name="color">Colour.</param>
    public void Text(string text, float x, float y, float maxWidth, float size, Color4 color)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxWidth < 8 || text.Length == 0)
        {
            return;
        }

        float bucket = MathF.Floor(maxWidth / WidthBucket) * WidthBucket;
        if (!_layouts.TryGetValue((text, size, bucket), out IDWriteTextLayout? layout))
        {
            if (_layouts.Count >= LayoutCacheLimit)
            {
                ClearLayouts();
            }

            layout = _write.CreateTextLayout(text, Format(size), Math.Max(bucket, WidthBucket), size * 1.5f);
            _layouts[(text, size, bucket)] = layout;
        }

        _brush.Color = color;
        _context.PushAxisAlignedClip(new Rect(x, y, maxWidth, size * 1.5f), AntialiasMode.Aliased);
        _context.DrawTextLayout(new Vector2(x, y), layout, _brush, DrawTextOptions.None);
        _context.PopAxisAlignedClip();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ClearLayouts();
        foreach (IDWriteTextFormat format in _formats.Values)
        {
            format.Dispose();
        }

        _brush.Dispose();
        _write.Dispose();
        _context.Dispose();
        _device.Dispose();
        _factory.Dispose();
    }

    private IDWriteTextFormat Format(float size)
    {
        if (!_formats.TryGetValue(size, out IDWriteTextFormat? format))
        {
            format = _write.CreateTextFormat("Segoe UI", FontWeight.Normal, FontStyle.Normal, FontStretch.Normal, size);
            format.WordWrapping = WordWrapping.NoWrap;
            using IDWriteInlineObject ellipsis = _write.CreateEllipsisTrimmingSign(format);
            format.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ellipsis);
            _formats[size] = format;
        }

        return format;
    }

    private void ClearLayouts()
    {
        foreach (IDWriteTextLayout layout in _layouts.Values)
        {
            layout.Dispose();
        }

        _layouts.Clear();
    }
}
