Shader "Default/TAA"

Properties
{
}

Pass "TAA"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull None
    ZTest Off
    ZWrite Off

	GLSLPROGRAM

	Vertex
	{
		layout (location = 0) in vec3 vertexPosition;
		layout (location = 1) in vec2 vertexTexCoord;
		
		out vec2 TexCoords;
		
		void main()
		{
			TexCoords = vertexTexCoord;
		    gl_Position = vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout(location = 0) out vec4 OutputColor;
		
		in vec2 TexCoords;

		uniform sampler2D gColor;
		uniform sampler2D gHistory;
		uniform sampler2D _CameraMotionVectorsTexture;
		uniform sampler2D _CameraDepthTexture;
		
		uniform mat4 matProjection;
		uniform mat4 matProjectionInverse;
		uniform mat4 matOldProjection;
		uniform mat4 matViewInverse;
		uniform mat4 matOldView;

		#include "Utilities"

		// ----------------------------------------------------------------------------

		// https://www.elopezr.com/temporal-aa-and-the-quest-for-the-holy-trail/
		vec3 AdjustHDRColor(vec3 color) {
			return vec3(color.x > 0.0 ? log(color.x) : -10.0, 
						color.y > 0.0 ? log(color.y) : -10.0, 
						color.z > 0.0 ? log(color.z) : -10.0); // Guard against nan
		}

		vec2 GetVelocity(ivec2 pixelPos) {
			float closestDepth = 100.0;
			ivec2 closestUVOffset;
			for(int j = -1; j <= 1; ++j) {
			    for(int i = -1; i <= 1; ++i) {
			         float neighborDepth = texelFetch(_CameraDepthTexture, pixelPos + ivec2(i, j), 0).x;
			         if(neighborDepth < closestDepth)
			         {
			             closestUVOffset = ivec2(i, j);
			             closestDepth = neighborDepth;
			         }
			    }
			}
			return texelFetch(_CameraMotionVectorsTexture, pixelPos + closestUVOffset, 0).xy;
		}

		void main()
		{
			ivec2 pixelPos = ivec2(gl_FragCoord.xy);
			
			vec3 neighbourhood[9];
			
			neighbourhood[0] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(-1, -1), 0).xyz);
			neighbourhood[1] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(+0, -1), 0).xyz);
			neighbourhood[2] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(+1, -1), 0).xyz);
			neighbourhood[3] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(-1, +0), 0).xyz);
			neighbourhood[4] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(+0, +0), 0).xyz);
			neighbourhood[5] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(+1, +0), 0).xyz);
			neighbourhood[6] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(-1, +1), 0).xyz);
			neighbourhood[7] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(+0, +1), 0).xyz);
			neighbourhood[8] = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(+1, +1), 0).xyz);

			vec3 nmin = neighbourhood[0];
			vec3 nmax = neighbourhood[0];   
			for(int i = 1; i < 9; ++i) {
			    nmin = min(nmin, neighbourhood[i]);
			    nmax = max(nmax, neighbourhood[i]);
			}
			
			//vec2 velocity = texelFetch(_CameraMotionVectorsTexture, pixelPos, 0).xy;
			// Inflate Velocity Edge
			vec2 velocity = GetVelocity(pixelPos);


			vec2 histUv = TexCoords + velocity;
			
			// sample from history buffer, with neighbourhood clamping.  
			vec3 histSample = texture2D(gHistory, histUv).xyz;
			//vec3 histSample = clamp(AdjustHDRColor(texture2D(gHistory, histUv).xyz), nmin, nmax);
			//vec3 histSample = SampleTextureCatmullRom(gHistory, histUv, Resolution).xyz;
			histSample = clamp(AdjustHDRColor(histSample), nmin, nmax);
			
			// blend factor
			float blend = 1.0 / 16.0; // 16 frames of jitter so match that for accumulation
			
			bvec2 a = greaterThan(histUv, vec2(1.0, 1.0));
			bvec2 b = lessThan(histUv, vec2(0.0, 0.0));
			// if history sample is outside screen, switch to aliased image as a fallback.
			blend = any(bvec2(any(a), any(b))) ? 1.0 : blend;
			
			vec3 curSample = neighbourhood[4];
			// finally, blend current and clamped history sample.
			OutputColor = vec4(mix(histSample, curSample, vec3(blend)), 1.0);

			OutputColor.rgb = exp(OutputColor.rgb); // Undo log transformation
		}

	}

	ENDGLSL
}