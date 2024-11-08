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
            float3 viewRay : TEXCOORD1;
		};


		Texture2D<float4> _MainTex;
        SamplerState sampler_MainTex;
        Texture2D<float> _CameraDepthTexture;
        SamplerState sampler_CameraDepthTexture;
        
        float4x4 _ProjectionMatrix;
        float4 _ScreenParams;
        float _Radius;
        float _Bias;
        int _SampleCount;
        float _Intensity;
        float _MaxDistance;
        
		#include "Prowl.hlsl"

        Varyings Vertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            
            output.position = float4(input.position.xyz, 1.0);
            output.uv = input.uv;

	    	// Generate view ray from vertex position
	    	float fov = GetFovFromProjectionMatrix(_ProjectionMatrix); // Your camera's FOV, could be passed in
	    	float aspect = _ScreenParams.x / _ScreenParams.y;
	    	
	    	// Create view ray from vertex position (which is in [-1,1] range)
	    	output.viewRay = float3(input.position.x * tan(fov/2) * aspect, 
	    						input.position.y * tan(fov/2),
	    						-1.0);
            
            return output;
        }

	    // Hash function for random numbers
	    float Hash(float2 p)
	    {
	    	float3 p3 = frac(float3(p.xyx) * float3(.1031, .1030, .0973));
	    	p3 += dot(p3, p3.yxz + 33.33);
	    	return frac((p3.x + p3.y) * p3.z);
	    }
	    
	    // Generate a random direction in hemisphere
	    float3 GenerateSamplePoint(float3 normal, float index, float2 screenPos)
	    {
	    	// Generate random angles
	    	float hash = Hash(screenPos + index * 127.1);
	    	float hash2 = Hash(screenPos * 113.7 + index * 311.9);
	    	
	    	float cosTheta = hash * 0.5 + 0.5; // Bias toward up direction
	    	float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
	    	float phi = hash2 * 2.0 * 3.14159;
	    	
	    	// Create sample vector
	    	float3 sample = float3(
	    		sinTheta * cos(phi),
	    		sinTheta * sin(phi),
	    		cosTheta
	    	);
	    	
	    	// Create TBN matrix to orient sample towards normal
	    	float3 tangent = normalize(cross(normal, float3(0, 1, 0)));
	    	float3 bitangent = normalize(cross(normal, tangent));
	    	float3x3 TBN = float3x3(tangent, bitangent, normal);
	    	
	    	// Transform sample to correct orientation
	    	sample = mul(sample, TBN);
	    	
	    	// Scale sample based on index (closer samples matter more)
	    	float scale = index / 16.0;
	    	scale = lerp(0.1, 1.0, scale * scale);
	    	
	    	return sample * scale;
	    }

        float4 Fragment(Varyings input) : SV_TARGET
        {
            float depth = _CameraDepthTexture.Sample(sampler_CameraDepthTexture, input.uv);
            if (depth == 0.0) return _MainTex.Sample(sampler_MainTex, input.uv); // Skip skybox
            //return float4(depth, depth, depth, 1.0);
            
            // Reconstruct view-space position
            float3 viewPos = GetViewPos(input.uv, depth, _ProjectionMatrix);
            //return float4(viewPos.xyz, 1.0);
            
            // Reconstruct view-space normal from depth
            float2 texelSize = 1.0 / _ScreenParams.xy;
            float3 ddx = GetViewPos(input.uv + float2(texelSize.x, 0), 
                _CameraDepthTexture.Sample(sampler_CameraDepthTexture, input.uv + float2(texelSize.x, 0)), _ProjectionMatrix) - viewPos;
            float3 ddy = GetViewPos(input.uv + float2(0, texelSize.y),
                _CameraDepthTexture.Sample(sampler_CameraDepthTexture, input.uv + float2(0, texelSize.y)), _ProjectionMatrix) - viewPos;
            float3 viewNormal = normalize(cross(ddy, ddx));
            //return float4(viewNormal.xyz, 1.0);
	    	
            // Calculate occlusion
            float occlusion = 0.0;

            [loop]
            for (int i = 0; i < _SampleCount; i++)
            {
                // Get sample position
	    	 	float3 sampleVec = GenerateSamplePoint(viewNormal, i, input.position.xy);
	    	 	
	    	 	// Add a bit of normal to reduce self shadowing
	    	 	sampleVec += viewNormal * 0.05;
	    	 
	    	 	float3 samplePos = viewPos + sampleVec * _Radius;
                
                // Project sample position to screen space
                float4 offset = float4(samplePos, 1.0);
                offset = mul(_ProjectionMatrix, offset);
                offset.xy /= offset.w;
                offset.xy = offset.xy * 0.5 + 0.5;
                
                // Get sample depth
                float sampleDepth = _CameraDepthTexture.Sample(sampler_CameraDepthTexture, offset.xy);
                float3 sampleViewPos = GetViewPos(offset.xy, sampleDepth, _ProjectionMatrix);
                
                // Calculate occlusion factor
	    	    if(sampleViewPos.z < samplePos.z && abs(sampleViewPos.z - samplePos.z) < _Radius)
                    occlusion += 1.0;
            }
            
            occlusion = 1.0 - (occlusion / _SampleCount);
            
            // Apply intensity and clamp
            occlusion = pow(occlusion, _Intensity);
	        
	        //return float4(occlusion, occlusion, occlusion, 1.0);
            
            // Combine with original color
            float4 color = _MainTex.Sample(sampler_MainTex, input.uv);
            return lerp(color, color * occlusion, _Intensity);
        }
    ENDHLSL
}
