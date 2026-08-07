module BenchShader;

// Two parameter blocks on purpose: set 0 is per-view and shared across passes (the state U1 wants
// memoized for a whole execution), set 1 is per-draw (the state U2/U4 have to re-resolve). The
// texture and sampler live in set 1 so the descriptor write path is checked too.

struct VertexInput
{
    float3 position : POSITION;
    float2 uv       : UV0;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float2 uv           : UV;
    float4 color        : COLOR;
}

struct ViewData
{
    float4x4 MatrixViewProjection;
    float4 ViewTint;
};
ParameterBlock<ViewData> View;

struct ModelData
{
    float4x4 MatrixModel;
    float4 Color;
    Sampler2D<float4> MainTexture;
};
ParameterBlock<ModelData> Model;

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    float4 world = mul(Model.MatrixModel, float4(input.position, 1.0));

    VertexOutput output;
    output.clipPosition = mul(View.MatrixViewProjection, world);
    output.uv = input.uv;
    output.color = Model.Color * View.ViewTint;

    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return Model.MainTexture.Sample(input.uv) + input.color;
}
