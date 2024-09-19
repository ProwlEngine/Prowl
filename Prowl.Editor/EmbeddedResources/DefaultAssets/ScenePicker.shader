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

		struct Attributes
		{
			float3 position : POSITION;
			float2 uv : TEXCOORD0;
		};

		struct Varyings
		{
			float4 position : SV_POSITION;
            float3 worldPosition : INTERPOLATOR0;
		};

        struct Output
        {
            float worldPosition : SV_TARGET0;
            int objectID : SV_TARGET1;
        };

        float3 _CameraPosition;

        cbuffer _PerDraw
        {
            float4x4 _Matrix_M;
            float4x4 _Matrix_MVP;
            int _ObjectID;
        }

        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

            output.worldPosition = mul(_Matrix_M, float4(input.position.xyz, 1.0)).xyz;
			output.position = mul(_Matrix_MVP, float4(input.position.xyz, 1.0));

            return output;
        }


        Output Fragment(Varyings input)
        {
            Output output = (Output)0;

            output.worldPosition = length(_CameraPosition - input.worldPosition);
            output.objectID = _ObjectID;

            return output;
        }
	ENDHLSL
}
