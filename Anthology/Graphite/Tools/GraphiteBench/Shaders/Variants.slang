import VariantAttributes;

module Variants;

[VariantAxis]
extern static const bool A;
[VariantAxis]
extern static const bool B;
[VariantAxis]
extern static const bool C;
[VariantAxis]
extern static const bool D;
[VariantAxis]
extern static const bool E;
[VariantAxis]
extern static const bool F;

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
    float4 c = Model.MainTexture.Sample(input.uv) * input.color;
    if (A) c.r = 1.0 - c.r;
    if (B) c.g *= 0.5;
    if (C) c.b = c.r * c.g;
    if (D) c = c.bgra;
    if (E) c.rgb = sqrt(c.rgb);
    if (F) c.a = 0.5;
    return c;
}
