// WT150: wintimeline shader compiler.
//   WinTimeline.ShaderCompiler <output folder> <shader.hlsl>...
// Compiles each file's "main" with the profile its name carries (fill.cs.hlsl is cs_5_0;
// .vs, .ps likewise) and writes <name>.cso. Errors print as file(line,col): error ..., which
// MSBuild shows as build errors; any error exits 1.
using System.Text;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: WinTimeline.ShaderCompiler <output folder> <shader.hlsl>...");
    return 2;
}

string output = args[0];
Directory.CreateDirectory(output);
int failed = 0;
foreach (string source in args.Skip(1))
{
    string name = Path.GetFileNameWithoutExtension(source);
    string? stage = Path.GetExtension(name).TrimStart('.') switch
    {
        "cs" => "cs_5_0",
        "vs" => "vs_5_0",
        "ps" => "ps_5_0",
        _ => null,
    };
    if (stage is null)
    {
        Console.WriteLine($"{Path.GetFullPath(source)}(1,1): error WTS001: name the stage in the file name, e.g. fill.cs.hlsl for a compute shader");
        failed++;
        continue;
    }

    SharpGen.Runtime.Result result = Compiler.Compile(File.ReadAllText(source), [], null!, "main", Path.GetFullPath(source), stage,
        ShaderFlags.OptimizationLevel3 | ShaderFlags.WarningsAreErrors, out Blob code, out Blob? errors);
    string messages = errors is null ? string.Empty : Encoding.UTF8.GetString(errors.AsSpan()).Trim('\0', ' ', '\r', '\n');
    if (result.Failure || code is null)
    {
        Console.WriteLine(messages.Length > 0 ? messages : $"{Path.GetFullPath(source)}(1,1): error WTS002: did not compile ({result.Code:X8})");
        failed++;
    }
    else
    {
        File.WriteAllBytes(Path.Combine(output, name + ".cso"), code.AsSpan().ToArray());
    }

    code?.Dispose();
    errors?.Dispose();
}

return failed == 0 ? 0 : 1;
