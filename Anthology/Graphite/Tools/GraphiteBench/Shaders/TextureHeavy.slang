module TextureHeavy;

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
    Sampler2D<float4> Noise;
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
    float2 uv = input.uv;
    float4 acc = 0.0;
    for (int i = 0; i < Pass.Iterations; i++)
    {
        float4 texel = Pass.Noise.Sample(uv);
        acc += texel;
        uv = frac(uv + texel.xy * 0.173 + float2(0.013, 0.029) * i);
    }
    return acc / max(Pass.Iterations, 1) * Pass.Tint;
}
