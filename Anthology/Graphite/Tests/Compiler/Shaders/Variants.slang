// Criterion: shaders with variants. A single boolean variant space (DoubleColor) consumed by the
// vertex stage, so the two specialized permutations differ in their generated code.
import VariantAttributes;

module Variants;

[VariantAxis]
extern static const bool DoubleColor;

struct VertexInput
{
    float3 position : POSITION;
    float4 color    : COLOR;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float4 color : COLOR;
}

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = float4(input.position, 1);
    output.color = DoubleColor ? input.color * 2.0 : input.color;
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return input.color;
}
