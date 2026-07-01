module FloatColorVertexAttribs;

// Renders points whose color comes from floating-point vertex attributes, normalized by a
// uniform factor and transformed by an orthographic matrix. Used for both Half4 attributes
// (factor != 1) and UShort4_Norm attributes (factor == 1, which leaves the already-normalized
// color untouched).

struct VertexInput
{
    float2 Position : POSITION;
    float4 Color    : COLOR;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float4 Color        : COLOR;
}

struct Globals
{
    float4x4 Ortho;
    uint ColorNormalizationFactor;
};
ParameterBlock<Globals> Model;

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = mul(Model.Ortho, float4(input.Position, 0, 1));
    output.Color = float4(input.Color.xyz, 1.0) / Model.ColorNormalizationFactor;
    output.Color.w = 1.0;
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return input.Color;
}
