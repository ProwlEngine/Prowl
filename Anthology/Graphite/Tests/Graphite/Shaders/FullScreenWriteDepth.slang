module FullScreenWriteDepth;

// Draws a full-screen triangle (3 vertices, no vertex buffer) and writes a per-pixel ramp into
// the depth target. The ramp is val = (y * width + x) / (width * height), letting a test read
// the depth attachment back and verify gl_FragDepth / SV_Depth writes.

struct VertexOutput
{
    float4 clipPosition : SV_Position;
}

struct Globals
{
    float4 OutputSize;
};
ParameterBlock<Globals> Frame;

[shader("vertex")]
VertexOutput vertex(uint vertexId : SV_VertexID)
{
    float x = -1.0 + float((vertexId & 1) << 2);
    float y = -1.0 + float((vertexId & 2) << 1);

    VertexOutput output;
    output.clipPosition = float4(x, y, 0, 1);
    return output;
}

[shader("fragment")]
float fragment(VertexOutput input) : SV_Depth
{
    float2 pixel = input.clipPosition.xy - float2(0.5, 0.5);
    float xComp = pixel.x;
    float yComp = pixel.y * Frame.OutputSize.x;
    return (yComp + xComp) / (Frame.OutputSize.x * Frame.OutputSize.y);
}
