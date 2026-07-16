// Criterion: shaders with variants that coexist with real resource bindings. Every one of
// DoubleColor's two permutations must reflect the exact same resource layout, since the axis only
// changes which branch is taken, not what is declared.
import VariantAttributes;

module VariantsWithResources;

[VariantAxis]
extern static const bool DoubleColor;

struct MaterialData
{
    float4 tint;
    Texture2D<float4> albedo;
    SamplerState samp;
};
ParameterBlock<MaterialData> Material;

struct VertexInput
{
    float3 position : POSITION;
    float2 uv       : UV0;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float2 uv : UV;
}

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = float4(input.position, 1);
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    float4 color = Material.albedo.Sample(Material.samp, input.uv) * Material.tint;
    return DoubleColor ? color * 2.0 : color;
}
