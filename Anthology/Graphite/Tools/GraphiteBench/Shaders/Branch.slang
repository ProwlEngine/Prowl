module Branch;

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

uint hash(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    uint2 pixel = uint2(input.clipPosition.xy);
    uint h = hash(pixel.x * 7919u + pixel.y);
    float v = float(h & 0xFFu) / 255.0;
    float3 acc = 0.0;

    for (int i = 0; i < Pass.Iterations; i++)
    {
        h = hash(h + uint(i));
        switch (h & 3u)
        {
        case 0u:
            acc.x += sin(v * i);
            break;
        case 1u:
            for (int j = 0; j < 4; j++)
                acc.y += cos(v + j * 0.5);
            break;
        case 2u:
            if ((h & 16u) != 0u)
                acc.z += exp(-v * i * 0.01);
            else
                acc.z -= sqrt(v + i);
            break;
        default:
            acc += float3(v, v * v, v * v * v);
            break;
        }
    }
    return float4(frac(acc * 0.01), 1.0) * Pass.Tint;
}
