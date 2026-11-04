using System.Diagnostics;
using System.IO;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WinTimeline.Render.Tests;

/// <summary>WT150: shaders are compiled by the build, embedded, and run; a broken one is a build error.</summary>
public sealed class ShaderTests
{
    [Fact]
    public void TheBuiltInFillShaderRunsOnWarpAndPaintsItsColour()
    {
        using GpuContext gpu = GpuContext.CreateWarp();
        using ShaderLibrary shaders = new();
        Assert.Contains("fill.cs", ShaderLibrary.Names);

        using ID3D11Texture2D target = gpu.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = 64, Height = 32, MipLevels = 1, ArraySize = 1, Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.UnorderedAccess,
        });
        using ID3D11UnorderedAccessView view = gpu.Device.CreateUnorderedAccessView(target);
        using ID3D11Buffer colour = gpu.Device.CreateBuffer([new Color4(1f, 0.5f, 0f, 1f)], BindFlags.ConstantBuffer);
        using ID3D11ComputeShader fill = shaders.Compute(gpu, "fill.cs");

        ID3D11DeviceContext1 context = gpu.ImmediateContext;
        context.CSSetShader(fill);
        context.CSSetConstantBuffer(0, colour);
        context.CSSetUnorderedAccessView(0, view);
        context.Dispatch(8, 4, 1);
        context.CSSetUnorderedAccessView(0, null);

        using ID3D11Texture2D staging = gpu.Device.CreateTexture2D(target.Description with { Usage = ResourceUsage.Staging, BindFlags = BindFlags.None, CPUAccessFlags = CpuAccessFlags.Read });
        context.CopyResource(staging, target);
        MappedSubresource map = context.Map(staging, 0, MapMode.Read);
        byte[] pixel = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(map.DataPointer + (nint)((31 * map.RowPitch) + (63 * 4)), pixel, 0, 4);
        context.Unmap(staging, 0);

        Assert.Equal(new byte[] { 255, 128, 0, 255 }, pixel);
    }

    [Fact]
    public void ABrokenShaderFailsWithItsFileLineAndMessage()
    {
        string folder = Directory.CreateTempSubdirectory("wt-shader-").FullName;
        string broken = Path.Combine(folder, "broken.cs.hlsl");
        File.WriteAllText(broken, "[numthreads(1,1,1)] void main() { undefined_thing = 1; }");

        (int exit, string output) = Compile(folder, broken);

        Assert.Equal(1, exit);
        Assert.Contains("broken.cs.hlsl(1,35-49): error X3004: undeclared identifier 'undefined_thing'", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(folder, "broken.cs.cso")));
    }

    [Fact]
    public void InDebugAChangedSourceIsRecompiledAndServedInsteadOfTheBuiltIn()
    {
        using ShaderLibrary shaders = new();
        string source = Path.Combine(Repository.Root, "src", "WinTimeline.Render", "Shaders", "fill.cs.hlsl");
        string copy = Path.Combine(Directory.CreateTempSubdirectory("wt-shader-").FullName, "fill.cs.hlsl");
        File.WriteAllText(copy, File.ReadAllText(source).Replace("Target[id.xy] = Colour;", "Target[id.xy] = Colour.bgra;", StringComparison.Ordinal));
        byte[] builtIn = shaders.Bytecode("fill.cs").ToArray();
        string? changed = null;
        shaders.Changed += (_, name) => changed = name;

        Assert.Null(shaders.Recompile(copy));

        Assert.Equal("fill.cs", changed);
        Assert.NotEqual(builtIn, shaders.Bytecode("fill.cs").ToArray());
        File.WriteAllText(copy, "void main( {");
        Assert.Contains("error", shaders.Recompile(copy), StringComparison.Ordinal);
    }

    private static (int Exit, string Output) Compile(string output, string source)
    {
        string tool = Path.Combine(Repository.Root, "tools", "WinTimeline.ShaderCompiler", "bin", Configuration, "net10.0-windows10.0.19041.0", "WinTimeline.ShaderCompiler.dll");
        using Process process = Process.Start(new ProcessStartInfo("dotnet", $"\"{tool}\" \"{output}\" \"{source}\"") { RedirectStandardOutput = true, UseShellExecute = false })!;
        string text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, text);
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
}
