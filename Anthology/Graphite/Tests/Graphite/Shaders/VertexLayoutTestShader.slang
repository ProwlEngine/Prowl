module VertexLayoutTestShader;

// Consumes four vertex attributes of differing sizes to exercise vertex-layout plumbing
// (strides / offsets / locations). The attributes are referenced so they are not stripped.

struct VertexInput
{
    float3 A_V3 : POSITION;
    float4 B_V4 : COLOR0;
    float2 C_V2 : TEXCOORD0;
    float4 D_V4 : COLOR1;
}

[shader("vertex")]
float4 vertex(VertexInput input) : SV_Position
{
    float4 sink = float4(input.A_V3, 0) + input.B_V4 + float4(input.C_V2, 0, 0) + input.D_V4;
    return float4(0, 0, 0, 1) + sink * 0.0;
}

[shader("fragment")]
float4 fragment() : SV_Target
{
    return float4(0, 0, 0, 1);
}
