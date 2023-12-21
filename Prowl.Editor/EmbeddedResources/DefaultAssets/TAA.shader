Shader "Default/TAA"

Pass 0
{
	Vertex
	{
		in vec3 vertexPosition;
		
		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
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

		uniform int ClampRadius;
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

		// https://www.elopezr.com/temporal-aa-and-the-quest-for-the-holy-trail/
		vec3 AdjustHDRColor(vec3 color) {
			return vec3(color.x > 0.0 ? log(color.x) : -10.0, 
						color.y > 0.0 ? log(color.y) : -10.0, 
						color.z > 0.0 ? log(color.z) : -10.0); // Guard against nan
		}

		float BSpline( float x )
		{
			float f = abs(x);
		
			if(f <= 1.0 )
				return (( 2.0 / 3.0 ) + ( 0.5 ) * ( f * f * f ) - (f * f));
			else
			{
				f = 2.0 - f;
				return (1.0 / 6.0 * f * f * f);
			}
		}
		const vec4 offset = vec4(-1.0, 1.0, 1.0 ,-1.0);
		
		vec4 bicubicFilter(sampler2D tex, vec2 texcoord)
		{
			float fx = fract(texcoord.x);
			float fy = fract(texcoord.y);
			texcoord.x -= fx;
			texcoord.y -= fy;
		
			vec4 xcubic = vec4(BSpline(- 1 - fx), BSpline(-fx), BSpline(1 - fx), BSpline(2 - fx));
			vec4 ycubic = vec4(BSpline(- 1 - fy), BSpline(-fy), BSpline(1 - fy), BSpline(2 - fy));
		
			vec4 c = vec4(texcoord.x - 0.5, texcoord.x + 0.5, texcoord.y - 0.5, texcoord.y + 0.5);
			vec4 s = vec4(xcubic.x + xcubic.y, xcubic.z + xcubic.w, ycubic.x + ycubic.y, ycubic.z + ycubic.w);
			vec4 offset = c + vec4(xcubic.y, xcubic.w, ycubic.y, ycubic.w) / s;
		
			vec4 sample0 = texture2D(tex, vec2(offset.x, offset.z) / Resolution);
			vec4 sample1 = texture2D(tex, vec2(offset.y, offset.z) / Resolution);
			vec4 sample2 = texture2D(tex, vec2(offset.x, offset.w) / Resolution);
			vec4 sample3 = texture2D(tex, vec2(offset.y, offset.w) / Resolution);
		
			float sx = s.x / (s.x + s.y);
			float sy = s.z / (s.z + s.w);
		
			return mix(mix(sample3, sample2, sx), mix(sample1, sample0, sx), sy);
		}

		vec2 GetVelocity(ivec2 pixelPos) {
			float closestDepth = 100.0;
			ivec2 closestUVOffset;
			for(int j = -1; j <= 1; ++j) {
			    for(int i = -1; i <= 1; ++i) {
			         float neighborDepth = texelFetch(gDepth, pixelPos + ivec2(i, j), 0).x;
			         if(neighborDepth < closestDepth)
			         {
			             closestUVOffset = ivec2(i, j);
			             closestDepth = neighborDepth;
			         }
			    }
			}
			return texelFetch(gVelocity, pixelPos + closestUVOffset, 0).xy;
		}

		void main()
		{
			vec2 TexCoords = gl_FragCoord.xy / Resolution.xy;

			vec2 pixel_size = vec2(1.0) / Resolution;
			ivec2 pixelPos = ivec2(gl_FragCoord.xy);
			
			vec3 neighbourhood[9];
			
			vec3 curr = AdjustHDRColor(texelFetch(gColor, pixelPos, 0).xyz);
			vec3 nmin = curr;
			vec3 nmax = curr;   
			for (int x=-ClampRadius; x<= ClampRadius; x++) {
				for (int y=-ClampRadius; y<= ClampRadius; y++) {
					if(x == 0 && y == 0) continue;
					vec3 neighbor = AdjustHDRColor(texelFetch(gColor, pixelPos + ivec2(x, y), 0).xyz);
					nmin = min(nmin, neighbor);
					nmax = max(nmax, neighbor);
				}
			}
			
			//vec2 velocity = texelFetch(gVelocity, pixelPos, 0).xy;
			// Inflate Velocity Edge
			vec2 velocity = GetVelocity(pixelPos);


			vec2 histUv = TexCoords + velocity;
			
			// sample from history buffer, with neighbourhood clamping.  
			//vec3 histSample = AdjustHDRColor(SampleTextureCatmullRom(gHistory, histUv, Resolution).xyz);
			vec3 histSample = AdjustHDRColor(bicubicFilter(gHistory, histUv * Resolution).xyz);
			//vec3 histSample = AdjustHDRColor(texture(gHistory, histUv).xyz);
			histSample = clamp(histSample, nmin, nmax);
			
			// blend factor
			// 16 frames of jitter so match that for accumulation
			// Strangely it seems 16 doesnt work as well as 32
			// it takes longer but the resulting AA is far better
			float blend = 1.0 / 32.0; 
			
			bvec2 a = greaterThan(histUv, vec2(1.0, 1.0));
			bvec2 b = lessThan(histUv, vec2(0.0, 0.0));
			// if history sample is outside screen, switch to aliased image as a fallback.
			blend = any(bvec2(any(a), any(b))) ? 1.0 : blend;
			
			vec3 curSample = curr;
			// finally, blend current and clamped history sample.
			OutputColor = vec4(mix(histSample, curSample, vec3(blend)), 1.0);

			OutputColor.rgb = exp(OutputColor.rgb); // Undo log transformation
		}

	}
}