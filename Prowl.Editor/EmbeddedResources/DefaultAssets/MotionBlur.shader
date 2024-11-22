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

		bool IsUVValid(float2 uv)
		{
			// Check if UV is within valid range
			return all(uv >= 0.0 && uv <= 1.0);
		}

        float4 Fragment(Varyings input) : SV_TARGET
        {
            float2 motion = _CameraMotionVectorsTexture.Sample(sampler_CameraMotionVectorsTexture, input.uv).xy;
            motion *= _Intensity;

			float4 color = 0.0;
			float totalWeight = 0.0;
			
			// Sample center pixel first
			color += _MainTex.Sample(sampler_MainTex, input.uv);
			totalWeight += 1.0;
			
            [loop]
			for(int i = 0; i < _SampleCount; i++)
			{
				float t = (float)i / (_SampleCount - 1);
				float2 offset = motion * t;
				float2 sampleUV = input.uv + offset;
				
				if(IsUVValid(sampleUV))
				{
					// Sample with validated UV
					float sampleWeight = 1.0;
					
					// Reduce weight of samples near edges
					float2 edgeDistance = min(sampleUV, 1.0 - sampleUV);
					float edgeFade = saturate(min(edgeDistance.x, edgeDistance.y) * 10.0); // Adjust multiplier for fade strength
					sampleWeight *= edgeFade;
					
					color += _MainTex.Sample(sampler_MainTex, sampleUV) * sampleWeight;
					totalWeight += sampleWeight;
				}
				
				// Also sample in opposite direction for more balanced blur
				sampleUV = input.uv - offset;
				if(IsUVValid(sampleUV))
				{
					float sampleWeight = 1.0;
					float2 edgeDistance = min(sampleUV, 1.0 - sampleUV);
					float edgeFade = saturate(min(edgeDistance.x, edgeDistance.y) * 10.0);
					sampleWeight *= edgeFade;
					
					color += _MainTex.Sample(sampler_MainTex, sampleUV) * sampleWeight;
					totalWeight += sampleWeight;
				}
			}
			
			// Ensure we don't divide by zero
			totalWeight = max(totalWeight, 1e-5);
			color /= totalWeight;
			
			return color;
        }
    ENDHLSL
}
