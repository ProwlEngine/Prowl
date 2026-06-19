module FullScreenTriSampleTexture2D;

// Draws a full-screen triangle (3 vertices, no vertex buffer) and samples a 2D texture across
// it. Used to verify texture/sampler binding through a render pass and rendering into specific
// framebuffer array layers / cubemap faces.

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float2 UV           : TEXCOORD0;
}

struct Globals
{
    Texture2D Tex;
    SamplerState Smp;
};
ParameterBlock<Globals> Resources;

[shader("vertex")]
VertexOutput vertex(uint vertexId : SV_VertexID)
{
    float x = -1.0 + float((vertexId & 1) << 2);
    float y = -1.0 + float((vertexId & 2) << 1);

    VertexOutput output;
    output.clipPosition = float4(x, y, 0, 1);
    output.UV = float2((x + 1.0) * 0.5, (y + 1.0) * 0.5);
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return Resources.Tex.Sample(Resources.Smp, input.UV);
}
