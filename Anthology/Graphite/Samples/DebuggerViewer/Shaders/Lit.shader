Shader "Sample/Lit"
{
    Pass
    {
        Name "Forward"

        Cull Off
        ZTest LessEqual
        ZWrite On

        SLANGPROGRAM
        uniform float4x4 MatrixMVP;
        uniform float4x4 MatrixModel;
        uniform float4 Color;
        uniform float4 LightDirection;
        uniform float4 LightColor;
        uniform float4 Ambient;

        struct VertexInput
        {
            float3 position : POSITION0;
            float3 normal : NORMAL0;
            float2 uv : TEXCOORD0;
        };

        struct VertexOutput
        {
            float4 position : SV_Position;
            float3 normal : NORMAL0;
            float2 uv : TEXCOORD0;
        };

        [shader("vertex")]
        VertexOutput Vertex(VertexInput input)
        {
            VertexOutput output;
            output.position = mul(MatrixMVP, float4(input.position, 1.0));
            output.normal = mul(MatrixModel, float4(input.normal, 0.0)).xyz;
            output.uv = input.uv;
            return output;
        }

        [shader("fragment")]
        float4 Fragment(VertexOutput input) : SV_Target
        {
            float3 n = normalize(input.normal);
            float diffuse = saturate(dot(n, -LightDirection.xyz));
            float2 edge = abs(input.uv - 0.5) * 2.0;
            float rim = smoothstep(0.85, 1.0, max(edge.x, edge.y));
            float3 albedo = Color.rgb * (1.0 - rim * 0.35);
            float3 lit = albedo * (Ambient.rgb + LightColor.rgb * diffuse);
            return float4(lit, 1.0);
        }
        ENDSLANG
    }
}
