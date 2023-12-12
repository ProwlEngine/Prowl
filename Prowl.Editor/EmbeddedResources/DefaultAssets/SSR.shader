Shader "Default/SSR"

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
		
		uniform sampler2D gColor; // Depth
		uniform sampler2D gNormalMetallic; // Depth
		uniform sampler2D gPositionRoughness; // Depth
		uniform sampler2D gDepth; // Depth
		
		uniform mat4 matProjection;
		uniform mat4 matProjectionInverse;
		uniform mat4 matViewInverse;

		uniform float Time;

		uniform int SSR_STEPS; // [16 20 24 28 32]
		uniform int SSR_BISTEPS; // [0 4 8 16]
		
		#include "PBR"

		// ----------------------------------------------------------------------------
		
		vec3 toScreen(vec3 pos) {
			//vec3 data = vec3(matProjection[0].x, matProjection[1].y, matProjection[2].z) * pos + matProjection[3].xyz;
			vec4 data = (matProjection * vec4(pos, 1.0));
			return (data.xyz / data.w) * 0.5 + 0.5;
			//return (matProjection * vec4(pos, 1.0)).xyz;
		}

		vec3 binaryRefine(vec3 screenPosRayDir, vec3 startPos){
			for(int i = 0; i < SSR_BISTEPS; i++){
				screenPosRayDir *= 0.5;
				startPos += texture2D(gDepth, startPos.xy).x < startPos.z ? -screenPosRayDir : screenPosRayDir;
			}
		
			return startPos;
		}

		vec3 rayTrace(vec3 screenPos, vec3 viewPos, vec3 rayDir, float dither) {
			vec3 screenPosRayDir = normalize(toScreen(viewPos + rayDir) - screenPos) / SSR_STEPS;
			screenPos += screenPosRayDir * dither;
		
			for(int i = 0; i < SSR_STEPS; i++){
				screenPos += screenPosRayDir;
				if(screenPos.x <= 0 || screenPos.y <= 0 || screenPos.x >= 1 || screenPos.y >= 1) return vec3(0);
				float currDepth = texture2D(gDepth, screenPos.xy).x;

				if(screenPos.z > currDepth) {
					if(SSR_BISTEPS == 0) return vec3(screenPos.xy, currDepth != 1);
					return vec3(binaryRefine(screenPosRayDir, screenPos).xy, currDepth != 1);
				}
			}
			
			return vec3(0);
		}

		vec3 calculateSSR(vec3 viewPos, vec3 screenPos, vec3 gBMVNorm, float dither) {
			vec3 reflectedScreenPos = rayTrace(screenPos, viewPos, reflect(normalize(viewPos), gBMVNorm), dither);
			if(reflectedScreenPos.z < 0.5) return vec3(0);
			return vec3(reflectedScreenPos.xy, 1);
		}

		vec3 projectAndDivide(mat4 matrix, vec3 pos) {
		    vec4 homoPos = matrix * vec4(pos, 1.0);
		    return homoPos.xyz / homoPos.w;
		}

		void main()
		{
			vec2 texCoords = gl_FragCoord.xy / Resolution;

			vec3 color = texture2D(gColor, texCoords).xyz;
			OutputColor = vec4(color, 1.0);

			vec4 viewPosAndRough = texture2D(gPositionRoughness, texCoords);
			vec3 viewPos = viewPosAndRough.xyz;
			float smoothness = 1.0 - viewPosAndRough.w;

			if(smoothness > 0.05)
			{
				vec4 normalAndMetallic = texture2D(gNormalMetallic, texCoords);
				vec3 normal = normalAndMetallic.xyz;
				float metallic = normalAndMetallic.w;

				bool isMetal = metallic > 0.9;

				// Get fresnel
				vec3 fresnel = FresnelSchlick(max(dot(normal, -normalize(viewPos)), 0.0),
					isMetal ? color : vec3(metallic)) * smoothness;
				
				vec3 screenPos = vec3(texCoords, texture2D(gDepth, texCoords).x);
				//vec3 ndcPos = screenPos * 2.0 - 1.0;
				//vec3 viewPos = projectAndDivide(matProjectionInverse, ndcPos);

				float dither = fract(sin(dot(texCoords + vec2(Time, Time), vec2(12.9898,78.233))) * 43758.5453123);

				vec3 SSRCoord = calculateSSR(viewPos, screenPos, normalize(normal), dither);
				vec3 ssrColor = SSRCoord.z > 0.5 ? texture2D(gColor, SSRCoord.xy).xyz : color;
				//vec3 ssrColor = texture2D(gColor, SSRCoord.xy).xyz;
				
				OutputColor.rgb *= isMetal ? vec3(1.0 - smoothness) : 1.0 - fresnel;
				OutputColor.rgb += ssrColor * fresnel;
			}
		}

	}
}