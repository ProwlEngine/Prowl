module Fullscreen;

struct VertexInput
{
    float3 position : POSITION;
    float2 uv       : UV0;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float2 uv           : UV;
}

struct PassData
{
    float4 Tint;
    Sampler2D<float4> Input;
};
ParameterBlock<PassData> Pass;

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = float4(input.position.xy, 0.0, 1.0);
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return Pass.Input.Sample(input.uv) * 0.5 + Pass.Tint;
}
