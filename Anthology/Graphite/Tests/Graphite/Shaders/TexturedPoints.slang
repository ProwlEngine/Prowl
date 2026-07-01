module TexturedPoints;

// Renders points (one per vertex) positioned by an orthographic matrix and colored by sampling
// a 2D texture at its center. Exercises a ParameterBlock that mixes a uniform block with a
// texture + sampler, bound through a PropertySet.

struct VertexInput
{
    float2 Position : POSITION;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
}

struct Globals
{
    float4x4 Ortho;

    Texture2D Tex;
    SamplerState Smp;
};
ParameterBlock<Globals> Resources;

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = mul(Resources.Ortho, float4(input.Position, 0, 1));
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return Resources.Tex.Sample(Resources.Smp, float2(0.5, 0.5));
}
