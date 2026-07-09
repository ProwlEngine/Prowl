// Baseline graphics shader: a three-attribute vertex input feeding a vertex + fragment stage.
// Kept deliberately tiny so the known-good GLSL/HLSL/SPIR-V stay readable. Carries no resources,
// so it exercises only the stage and vertex-input reflection paths.
module Graphics;

struct VertexInput
{
    float3 position : POSITION;
    float2 uv       : UV0;
    float4 color    : COLOR;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float2 uv : UV;
    float4 color : COLOR;
}

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = float4(input.position, 1);
    output.uv = input.uv;
    output.color = input.color;
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return input.color;
}
