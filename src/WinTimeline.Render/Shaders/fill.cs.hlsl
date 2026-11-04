// WT150: fills a texture with one colour; the least compute shader that proves the build-time
// path (compiled by tools/WinTimeline.ShaderCompiler, embedded, loaded by ShaderLibrary).

cbuffer Fill : register(b0)
{
    float4 Colour;
};

RWTexture2D<unorm float4> Target : register(u0);

[numthreads(8, 8, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    Target[id.xy] = Colour;
}
