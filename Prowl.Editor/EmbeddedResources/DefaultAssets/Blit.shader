Shader "Default/Blit"

Pass "Blit"
{
    // Rasterizer culling mode
    Cull None

	SHADERPROGRAM
		#pragma vertex Vertex
        #pragma fragment Fragment

		struct Attributes
		{
			float3 position : POSITION;
			float2 uv : TEXCOORD0;
		};

		struct Varyings
		{
			float3 position : SV_POSITION;
			float2 uv : TEXCOORD0;
		};
		
		Texture2D<float4> _MainTexture;
		SamplerState sampler_MainTexture;

        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

			output.position = input.position;
			output.uv = input.uv;

            return output;
        }

        float4 Fragment(Varyings input) : SV_TARGET
        {
			float3 baseColor = _MainTexture.Sample(sampler_MainTexture, input.uv).rgb;

            return float4(baseColor, 1.0);
        }
	ENDPROGRAM
}