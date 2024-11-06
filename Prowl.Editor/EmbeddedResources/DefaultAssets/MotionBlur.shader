Shader "Default/MotionBlur"

Pass "MotionBlur"
{
    Tags { "RenderOrder" = "Composite" }

	DepthStencil
	{
		DepthTest Off
		DepthWrite Off
	}
    
    Blend Override

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
        Texture2D<float2> _CameraMotionVectorsTexture;
        SamplerState sampler_CameraMotionVectorsTexture;
        
        float _Intensity;
        int _SampleCount;

        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

			output.position = float4(input.position.xyz, 1.0);
			output.uv = input.uv;

            return output;
        }

        float4 Fragment(Varyings input) : SV_TARGET
        {
            float2 motion = _CameraMotionVectorsTexture.Sample(sampler_CameraMotionVectorsTexture, input.uv).xy;
            motion *= _Intensity;

            float4 color = 0.0;
            
            [unroll]
            for(int i = 0; i < _SampleCount; i++)
            {
                float t = (float)i / (_SampleCount - 1);
                float2 offset = motion * t;
                color += _MainTex.Sample(sampler_MainTex, input.uv + offset);
            }
            
            color /= _SampleCount;
            return color;
        }
    ENDHLSL
}
