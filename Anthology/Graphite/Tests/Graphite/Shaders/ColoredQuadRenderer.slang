module ColoredQuadRenderer;

// Pulls per-vertex position/color from a read-only structured buffer indexed by vertex id,
// then passes the color through. Pairs with a compute pass that fills the vertex buffer.

struct ColoredVertex
{
    float4 Color;
    float2 Position;
    float2 _padding0;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float4 Color        : COLOR;
}

struct Globals
{
    StructuredBuffer<ColoredVertex> InputVertices;
};
ParameterBlock<Globals> Resources;

[shader("vertex")]
VertexOutput vertex(uint vertexId : SV_VertexID)
{
    ColoredVertex v = Resources.InputVertices[vertexId];

    VertexOutput output;
    output.clipPosition = float4(v.Position, 0, 1);
    output.Color = v.Color;
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return input.Color;
}
