Shader "Default/ScenePicker"

Pass
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull Back

    // Stencil state
    DepthStencil
    {
        // Comparison kind
        DepthTest LessEqual
    }

	HLSLPROGRAM
		#pragma vertex Vertex
        #pragma fragment Fragment

		struct Varyings
		{
			float4 position : SV_POSITION;
		};

        cbuffer _PerDraw
        {
            float4x4 _Matrix_MVP;
            int _ObjectID;
        }


        float4 Vertex(float3 position : POSITION) : SV_POSITION
        {
            return mul(_Matrix_MVP, float4(position.xyz, 1.0));
        }


        float4 Fragment(Varyings input) : SV_TARGET
        {
            float4 packed = (float4)0;

            packed.x = (_ObjectID & 0xFF) / 255.0f;            // Lowest byte
            packed.y = ((_ObjectID >> 8) & 0xFF) / 255.0f;     // Second byte
            packed.z = ((_ObjectID >> 16) & 0xFF) / 255.0f;    // Third byte
            packed.w = ((_ObjectID >> 24) & 0xFF) / 255.0f;    // Highest byte

            return packed;
        }
	ENDHLSL
}
