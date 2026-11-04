using System.Diagnostics;
using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WinTimeline.Render.Tests;

/// <summary>WT47: Direct2D draws on the app's device, into any B8G8R8A8 surface, and keeps up with a full timeline.</summary>
public sealed class Canvas2DTests
{
    private const int Width = 1920;
    private const int Height = 720;

    [Fact]
    public void RectanglesLandWhereTheyAreDrawnInTheirColour()
    {
        using GpuContext gpu = GpuContext.CreateWarp();
        using Canvas2D canvas = new(gpu);
        using ID3D11Texture2D target = Target(gpu);

        Draw(canvas, target, c =>
        {
            c.Fill(0, 0, Width, Height, new Color4(0, 0, 0, 1));
            c.Fill(100, 50, 200, 30, new Color4(1, 0, 0, 1));
            c.Line(new Vector2(500, 0), new Vector2(500, Height), new Color4(0, 1, 0, 1), 2);
        });

        byte[] pixels = Read(gpu, target);
        Assert.Equal((0, 0, 255), Pixel(pixels, 150, 60));
        Assert.Equal((0, 255, 0), Pixel(pixels, 500, 300));
        Assert.Equal((0, 0, 0), Pixel(pixels, 10, 10));
    }

    [Fact]
    public void AFrameOfThreeThousandClipsAndNamesStaysWithinItsBudgetEvenOnWarp()
    {
        using GpuContext gpu = GpuContext.CreateWarp();
        using Canvas2D canvas = new(gpu);
        using ID3D11Texture2D target = Target(gpu);
        Random random = new(47);

        // What a zoomed-out 24-lane view of a feature shows: 3,000 rectangles, edges and names.
        (float X, float Y, float W, string Name)[] clips = [.. Enumerable.Range(0, 3000).Select(i =>
            ((float)random.Next(Width), (float)((i % 24) * 30), (float)random.Next(4, 120), $"V{(i % 24) + 1} shot {i / 24:000}"))];
        void Frame(Canvas2D c)
        {
            foreach ((float x, float y, float w, string name) in clips)
            {
                c.Fill(x, y + 1, w, 28, new Color4(0.25f, 0.4f, 0.72f, 1));
                c.Outline(x + 0.5f, y + 1.5f, w - 1, 27, new Color4(0, 0, 0, 0.5f));
                c.Text(name, x + 4, y + 8, w - 8, 11, new Color4(0.9f, 0.9f, 0.9f, 1));
            }
        }

        Draw(canvas, target, Frame);
        List<double> times = [];
        for (int frame = 0; frame < 20; frame++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Draw(canvas, target, Frame);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        // On WARP the CPU rasterises too, so this bounds the submission cost from above.
        double median = times.Order().ElementAt(times.Count / 2);
        Assert.True(median < 100, $"a frame took {median:F1} ms median on WARP");
        Assert.InRange(canvas.CachedLayouts, 1, 8192);
    }

    private static void Draw(Canvas2D canvas, ID3D11Texture2D target, Action<Canvas2D> draw)
    {
        using IDXGISurface surface = target.QueryInterface<IDXGISurface>();
        canvas.Begin(surface);
        draw(canvas);
        canvas.End();
    }

    private static ID3D11Texture2D Target(GpuContext gpu) => gpu.Device.CreateTexture2D(new Texture2DDescription
    {
        Width = Width,
        Height = Height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
    });

    private static byte[] Read(GpuContext gpu, ID3D11Texture2D target)
    {
        using ID3D11Texture2D staging = gpu.Device.CreateTexture2D(target.Description with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        gpu.ImmediateContext.CopyResource(staging, target);
        MappedSubresource map = gpu.ImmediateContext.Map(staging, 0, MapMode.Read);
        try
        {
            byte[] pixels = new byte[Width * Height * 4];
            for (int row = 0; row < Height; row++)
            {
                System.Runtime.InteropServices.Marshal.Copy(map.DataPointer + (nint)(row * map.RowPitch), pixels, row * Width * 4, Width * 4);
            }

            return pixels;
        }
        finally
        {
            gpu.ImmediateContext.Unmap(staging, 0);
        }
    }

    private static (int B, int G, int R) Pixel(byte[] pixels, int x, int y)
    {
        int at = ((y * Width) + x) * 4;
        return (pixels[at], pixels[at + 1], pixels[at + 2]);
    }
}
