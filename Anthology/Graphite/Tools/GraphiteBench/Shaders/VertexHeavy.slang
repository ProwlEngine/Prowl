module VertexHeavy;

struct VertexInput
{
    float3 position : POSITION;
    float2 uv       : UV0;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float4 color        : COLOR;
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
    float3 p = input.position;
    float3 n = float3(input.uv, 1.0);
    for (int i = 0; i < Pass.Iterations; i++)
    {
        n = float3(sin(n.y * 1.7 + i), cos(n.z * 2.3 - i), sin(n.x * 0.9 + n.y));
        p.z += n.x * 0.001;
    }

    VertexOutput output;
    output.clipPosition = float4(p.xy, saturate(p.z * 0.5 + 0.5), 1.0);
    output.color = float4(n * 0.5 + 0.5, 1.0);
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return input.color * Pass.Tint;
}
