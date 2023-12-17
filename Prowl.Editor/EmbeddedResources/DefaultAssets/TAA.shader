Shader "Default/TAA"

Pass 0
{
	Vertex
	{
		in vec3 vertexPosition;

		void main() 
		{
			gl_Position = vec4(vertexPosition, 1.0);
		}
	}

	Fragment
	{
		layout(location = 0) out vec4 OutputColor;
		
		uniform vec2 Resolution;
		
		uniform sampler2D gColor;
		uniform sampler2D gHistory;
		uniform sampler2D gPositionRoughness;
		uniform sampler2D gVelocity;
		uniform sampler2D gDepth;
		
		uniform mat4 matProjection;
		uniform mat4 matProjectionInverse;
		uniform mat4 matOldProjection;
		uniform mat4 matViewInverse;
		uniform mat4 matOldView;

		uniform vec2 Jitter;
		uniform vec2 PreviousJitter;

		#include "Utilities"

		// ----------------------------------------------------------------------------

		// from http://www.java-gaming.org/index.php?topic=35123.0
		vec4 cubic(float v){
		    vec4 n = vec4(1.0, 2.0, 3.0, 4.0) - v;
		    vec4 s = n * n * n;
		    float x = s.x;
		    float y = s.y - 4.0 * s.x;
		    float z = s.z - 4.0 * s.y + 6.0 * s.x;
		    float w = 6.0 - x - y - z;
		    return vec4(x, y, z, w) * (1.0/6.0);
		}
		
		vec4 textureBicubic(sampler2D sampler, vec2 texCoords){
		
		   vec2 texSize = textureSize(sampler, 0);
		   vec2 invTexSize = 1.0 / texSize;
		   
		   texCoords = texCoords * texSize - 0.5;
		
		   
		    vec2 fxy = fract(texCoords);
		    texCoords -= fxy;
		
		    vec4 xcubic = cubic(fxy.x);
		    vec4 ycubic = cubic(fxy.y);
		
		    vec4 c = texCoords.xxyy + vec2 (-0.5, +1.5).xyxy;
		    
		    vec4 s = vec4(xcubic.xz + xcubic.yw, ycubic.xz + ycubic.yw);
		    vec4 offset = c + vec4 (xcubic.yw, ycubic.yw) / s;
		    
		    offset *= invTexSize.xxyy;
		    
		    vec4 sample0 = texture(sampler, offset.xz);
		    vec4 sample1 = texture(sampler, offset.yz);
		    vec4 sample2 = texture(sampler, offset.xw);
		    vec4 sample3 = texture(sampler, offset.yw);
		
		    float sx = s.x / (s.x + s.y);
		    float sy = s.z / (s.z + s.w);
		
		    return mix(
		       mix(sample3, sample2, sx), mix(sample1, sample0, sx)
		    , sy);
		}

		void main()
		{
			vec2 texCoords = gl_FragCoord.xy / Resolution;
			vec2 pixel_size = vec2(1.0) / Resolution;
			
			vec3 neighbourhood[9];
			
			neighbourhood[0] = texture2D(gColor, texCoords + vec2(-1, -1) * pixel_size).xyz;
			neighbourhood[1] = texture2D(gColor, texCoords + vec2(+0, -1) * pixel_size).xyz;
			neighbourhood[2] = texture2D(gColor, texCoords + vec2(+1, -1) * pixel_size).xyz;
			neighbourhood[3] = texture2D(gColor, texCoords + vec2(-1, +0) * pixel_size).xyz;
			neighbourhood[4] = texture2D(gColor, texCoords + vec2(+0, +0) * pixel_size).xyz;
			neighbourhood[5] = texture2D(gColor, texCoords + vec2(+1, +0) * pixel_size).xyz;
			neighbourhood[6] = texture2D(gColor, texCoords + vec2(-1, +1) * pixel_size).xyz;
			neighbourhood[7] = texture2D(gColor, texCoords + vec2(+0, +1) * pixel_size).xyz;
			neighbourhood[8] = texture2D(gColor, texCoords + vec2(+1, +1) * pixel_size).xyz;

			vec3 nmin = neighbourhood[0];
			vec3 nmax = neighbourhood[0];   
			for(int i = 1; i < 9; ++i) {
			    nmin = min(nmin, neighbourhood[i]);
			    nmax = max(nmax, neighbourhood[i]);
			}
			
			vec2 velocity = texture2D(gVelocity, texCoords).xy;
			vec2 histUv = texCoords + velocity;
			
			// sample from history buffer, with neighbourhood clamping.  
			vec3 histSample = clamp(texture2D(gHistory, histUv).xyz, nmin, nmax);
			//vec3 histSample = texture2D(gHistory, histUv).xyz;
			
			// blend factor
			float blend = 1.0 / 16.0; // 16 frames of jitter so match that for accumulation
			
			bvec2 a = greaterThan(histUv, vec2(1.0, 1.0));
			bvec2 b = lessThan(histUv, vec2(0.0, 0.0));
			// if history sample is outside screen, switch to aliased image as a fallback.
			blend = any(bvec2(any(a), any(b))) ? 1.0 : blend;
			
			vec3 curSample = neighbourhood[4];
			// finally, blend current and clamped history sample.
			OutputColor = vec4(mix(histSample, curSample, vec3(blend)), 1.0);
		}

	}
}