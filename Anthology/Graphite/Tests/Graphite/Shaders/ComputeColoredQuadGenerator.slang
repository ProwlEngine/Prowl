module ComputeColoredQuadGenerator;

// Fills a read-write structured buffer with the 4 vertices of a full-screen triangle-strip quad,
// all colored red. A following graphics pass reads the buffer back as vertices (see
// ColoredQuadRenderer) so the whole target ends up red. The quad covers the screen regardless of
// clip-space Y orientation, so no orientation uniform is needed.

struct ColoredVertex
{
    float4 Color;
    float2 Position;
    float2 _padding0;
}

struct Globals
{
    RWStructuredBuffer<ColoredVertex> OutputVertices;
};
ParameterBlock<Globals> Resources;

[shader("compute")]
[numthreads(1, 1, 1)]
void compute(uint3 id : SV_DispatchThreadID)
{
    float4 red = float4(1, 0, 0, 1);

    ColoredVertex v0; v0.Position = float2(-1, 1); v0.Color = red; v0._padding0 = float2(0, 0);
    ColoredVertex v1; v1.Position = float2(1, 1); v1.Color = red; v1._padding0 = float2(0, 0);
    ColoredVertex v2; v2.Position = float2(-1, -1); v2.Color = red; v2._padding0 = float2(0, 0);
    ColoredVertex v3; v3.Position = float2(1, -1); v3.Color = red; v3._padding0 = float2(0, 0);

    Resources.OutputVertices[0] = v0;
    Resources.OutputVertices[1] = v1;
    Resources.OutputVertices[2] = v2;
    Resources.OutputVertices[3] = v3;
}
