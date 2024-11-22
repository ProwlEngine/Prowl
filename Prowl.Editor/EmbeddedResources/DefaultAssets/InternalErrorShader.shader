Shader "Hidden/InternalError"

Pass "InternalError"
{
	Blend Override

    // Stencil state
    DepthStencil
    {
        // Depth write
        DepthWrite On

        // Comparison kind
        DepthTest LessEqual
    }

    // Rasterizer culling mode
    Cull None

    HLSLPROGRAM
        #pragma vertex Vertex
        #pragma fragment Fragment
        
        #include "Prowl.hlsl"

        float4 Vertex(float3 position : POSITION) : SV_POSITION
        {
            return mul(PROWL_MATRIX_MVP, float4(position, 1.0));
        }

        float4 Fragment() : SV_TARGET
        {
            return float4(1.0, 0.0, 1.0, 1.0);
        }
	ENDHLSL
}
