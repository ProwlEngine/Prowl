module Alu;

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
    int Iterations;
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
    float2 z = input.uv * 2.0 - 1.0;
    float3 acc = 0.0;
    for (int i = 0; i < Pass.Iterations; i++)
    {
        z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + input.uv * 0.37;
        z = float2(sin(z.x * 3.1 + i), cos(z.y * 2.7 - i));
        acc += float3(exp(-abs(z.x)), sqrt(abs(z.y)), atan2(z.y, z.x + 1.5)) * 0.01;
    }
    return float4(acc, 1.0) * Pass.Tint;
}
