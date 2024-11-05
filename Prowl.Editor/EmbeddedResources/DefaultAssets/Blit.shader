Shader "Default/Blit"

Pass "Blit"
{
    // Rasterizer culling mode
    Cull None

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
			float2 uv : TEXCOORD0;
		};


		Texture2D<float4> _MainTex;
		SamplerState sampler_MainTex;


        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

			output.position = float4(input.position.xyz, 1.0);
			output.uv = input.uv;

            return output;
        }

        float4 Fragment(Varyings input) : SV_TARGET
        {
			float3 baseColor = _MainTex.Sample(sampler_MainTex, input.uv).rgb;

            return float4(baseColor, 1.0);
        }
	ENDHLSL
}
