// Criterion: shaders with modules + block variables that are not in the base module.
// Imports Common, which owns the `lighting` constant buffer; the base module owns `globals`.
// Both blocks must appear in the program reflection even though one comes from an imported module.
module Modules;

import Common;

struct Globals
{
    float4x4 viewProj;
    float4 tint;
}

ConstantBuffer<Globals> globals;

struct VertexInput
{
    float3 position : POSITION;
    float3 normal   : NORMAL;
    float4 color    : COLOR;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float3 normal : NORMAL;
    float4 color : COLOR;
}

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = mul(globals.viewProj, float4(input.position, 1));
    output.normal = input.normal;
    output.color = input.color * globals.tint;
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return float4(shade(input.color.rgb, input.normal), input.color.a);
}
