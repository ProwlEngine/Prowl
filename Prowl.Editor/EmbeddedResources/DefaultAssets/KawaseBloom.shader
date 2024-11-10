Shader "Default/KawaseBloom"

Pass "KawaseBloomThreshold"
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

        float _Threshold;
        float _SoftKnee;

        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

			output.position = float4(input.position.xyz, 1.0);
			output.uv = input.uv;

            return output;
        }
        
		float3 SafeColor(float3 color)
		{
			// Check for NaN or Inf
			if (any(isnan(color)) || any(isinf(color)))
			{
				return float3(1, 0, 1); // Bright magenta to make it obvious
			}
			return color;
		}
		
        // Quadratic threshold function
        float3 Threshold(float3 color) {
            float brightness = max(max(color.r, color.g), color.b);
            float soft = brightness - _Threshold + _SoftKnee;
            soft = clamp(soft, 0, 2 * _SoftKnee);
            soft = soft * soft / (4 * _SoftKnee + 0.00001);
            float contribution = max(soft, brightness - _Threshold);
            contribution /= max(brightness, 0.00001);
            return SafeColor(color * contribution);
        }

        float4 Fragment(Varyings input) : SV_TARGET
        {
            return float4(Threshold(_MainTex.Sample(sampler_MainTex, input.uv).rgb), 1.0);
        }
    ENDHLSL
}


Pass "KawaseBloom"
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

        float2 _Resolution;
        
        float _Radius;
        float _Offset;
        float _Intensity;

        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

			output.position = float4(input.position.xyz, 1.0);
			output.uv = input.uv;

            return output;
        }

		float2 hash22(float2 p)
		{
			float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
			p3 += dot(p3, p3.yzx+33.33);
			return frac((p3.xx+p3.yz)*p3.zy);
		}

        float4 Fragment(Varyings input) : SV_TARGET
        {
            float2 offset = _Offset * (1.0 / _Resolution);
			float2 pixelCoord = input.uv * _Resolution;
			
			offset *= hash22(pixelCoord) * 5.0 * _Radius;
			
			const float2 samples[4] = {
				float2(offset.x, offset.y),
				float2(-offset.x, offset.y),
				float2(-offset.x, -offset.y),
				float2(offset.x, -offset.y)
			};
			
			// Sample center pixel
            float3 centerColor = _MainTex.Sample(sampler_MainTex, input.uv).rgb;
            float3 color = centerColor; // Initialize with center color
            int sampleCount = 1;    // Start count at 1
            
            // Check and accumulate valid samples
            [unroll]
            for(int i = 0; i < 4; i++)
            {
                float2 sampleUV = input.uv + samples[i];
                if(all(sampleUV >= 0) && all(sampleUV <= 1)) // Use all() for better vector comparison
                {
                    float3 sampleColor = _MainTex.Sample(sampler_MainTex, sampleUV).rgb;
                    color += sampleColor;
                    sampleCount++;
                }
            }
            
            // Safer averaging
            color = (color / max(sampleCount, 1.0)) * _Intensity;
			
            return float4(color, 1.0);
        }
    ENDHLSL
}

Pass "BilateralBlurH"
{
    Tags { "RenderOrder" = "Composite" }

    DepthStencil
    {
        DepthTest Off
        DepthWrite Off
    }
    
    Blend Override
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

        float2 _Resolution;

        static const int KERNEL_SIZE = 16;

        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            output.position = float4(input.position.xyz, 1.0);
            output.uv = input.uv;
            return output;
        }

        float4 Fragment(Varyings input) : SV_TARGET
        {
            float2 texelSize = 1.0 / _Resolution;
            float3 centerColor = _MainTex.Sample(sampler_MainTex, input.uv).rgb;
            
            float3 color = 0;
            
            // Horizontal blur
            for(int i = 0; i < KERNEL_SIZE; i++)
            {
                int offset = i - KERNEL_SIZE/2;
                float2 uv = input.uv + float2(texelSize.x * offset, 0);
                float3 sampleColor = _MainTex.Sample(sampler_MainTex, uv).rgb;
                
                color += sampleColor;
            }
            
            return float4(color / KERNEL_SIZE, 1.0);
        }
    ENDHLSL
}

Pass "BilateralBlurV"
{
    Tags { "RenderOrder" = "Composite" }

    DepthStencil
    {
        DepthTest Off
        DepthWrite Off
    }
    
    Blend Override
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

        float2 _Resolution;

        static const int KERNEL_SIZE = 16;

        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            output.position = float4(input.position.xyz, 1.0);
            output.uv = input.uv;
            return output;
        }

        float4 Fragment(Varyings input) : SV_TARGET
        {
            float2 texelSize = 1.0 / _Resolution;
            float3 centerColor = _MainTex.Sample(sampler_MainTex, input.uv).rgb;
            
            float3 color = 0;
            
            // Vertical blur
            for(int i = 0; i < KERNEL_SIZE; i++)
            {
                int offset = i - KERNEL_SIZE/2;
                float2 uv = input.uv + float2(0, texelSize.y * offset);
                float3 sampleColor = _MainTex.Sample(sampler_MainTex, uv).rgb;
                
                color += sampleColor;
            }
            
            return float4(color / KERNEL_SIZE, 1.0);
        }
    ENDHLSL
}

Pass "KawaseBloomComposite"
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
		
		Texture2D<float4> _BloomTex;
		SamplerState sampler_BloomTex;

        Varyings Vertex(Attributes input)
        {
			Varyings output = (Varyings)0;

			output.position = float4(input.position.xyz, 1.0);
			output.uv = input.uv;

            return output;
        }
        
        float4 Fragment(Varyings input) : SV_TARGET
        {
			float3 base = _MainTex.Sample(sampler_MainTex, input.uv).rgb;
			float3 bloom = _BloomTex.Sample(sampler_BloomTex, input.uv).rgb;
            return float4(base + bloom, 1.0);
        }
    ENDHLSL
}
