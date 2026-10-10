module Flat;

struct VertexInput
{
    float3 position : POSITION;
    float2 uv       : UV0;
}

struct PassData
{
    float4 Tint;
};
ParameterBlock<PassData> Pass;

[shader("vertex")]
float4 vertex(VertexInput input) : SV_Position
{
    return float4(input.position.xy, 0.0, 1.0);
}

[shader("fragment")]
float4 fragment() : SV_Target
{
    return Pass.Tint;
}
