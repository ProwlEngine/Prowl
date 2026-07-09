// Criterion: shaders that use ParameterBlock<T>. Top-level resources sit alongside two parameter
// blocks: perObject carries ordinary uniform data plus resources, onlyTex carries resources only.
// Each block opens its own descriptor set / register space; a block with uniform data also emits an
// implicit constant buffer. A resource-only block emits no constant buffer.
module ParameterBlocks;

struct Globals { float4x4 viewProj; float4 tint; }

ConstantBuffer<Globals> globals;
Texture2D albedo;
SamplerState samp;

struct PerObject
{
    float4 color;
    float2 uvOffset;
    Texture2D detail;
    SamplerState detailSamp;
}
ParameterBlock<PerObject> perObject;

struct OnlyTex { Texture2D tex; SamplerState s; }
ParameterBlock<OnlyTex> onlyTex;

[shader("vertex")]
float4 vertex(float3 position : POSITION) : SV_Position
{ return mul(globals.viewProj, float4(position, 1)) * globals.tint; }

[shader("fragment")]
float4 fragment(float2 uv : UV0) : SV_Target
{
    return albedo.Sample(samp, uv) * perObject.color
        + perObject.detail.Sample(perObject.detailSamp, uv + perObject.uvOffset)
        + onlyTex.tex.Sample(onlyTex.s, uv);
}
