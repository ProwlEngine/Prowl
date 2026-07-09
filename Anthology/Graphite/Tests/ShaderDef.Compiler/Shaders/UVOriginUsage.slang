// Criterion: a shader that reads the linker-provided IsUVOriginTopLeft constant. The constant is an
// extern resolved per backend, so the bottom-left backends (OpenGL) fold the flip branch while the
// top-left backends (Vulkan, D3D11) fold the passthrough branch.
import UVOrigin;

module UVOriginUsage;

struct VertexInput
{
    float3 position : POSITION;
    float2 uv : TEXCOORD0;
}

struct VertexOutput
{
    float4 clipPosition : SV_Position;
    float2 uv : TEXCOORD0;
}

[shader("vertex")]
VertexOutput vertex(VertexInput input)
{
    VertexOutput output;
    output.clipPosition = float4(input.position, 1);
    output.uv = IsUVOriginTopLeft ? input.uv : float2(input.uv.x, 1.0 - input.uv.y);
    return output;
}

[shader("fragment")]
float4 fragment(VertexOutput input) : SV_Target
{
    return float4(input.uv, 0, 1);
}
