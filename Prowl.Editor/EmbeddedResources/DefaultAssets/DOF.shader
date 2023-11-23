Shader "Default/DOF"

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
		
		uniform sampler2D gCombined; // Depth
		uniform sampler2D gDepth; // Depth

		//uniform float focusDistance;
		uniform float focusStrength; // [10.0 15.0 25.0 30.0 35.0 40.0 45.0 50.0 65.0 100.0 200.0 300.0 400.0 500.0 600.0 700.0 800.0 900.0 1000.0 1250.0 1500.0 1750.0 2000.0 2500.0 3000.0]
		
		// ----------------------------------------------------------------------------
		
		const float DOF_QUALITY = 0.5; // [0.1 0.15 0.2 0.25 0.3 0.35 0.4 0.45 0.5 0.55 0.6 0.65 0.7 0.75 0.8 0.85 0.9]
		const float DOF_BLUR_SIZE = 20; // [5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40]
		
		float getBlurSize(float depth, float focusPoint, float focusScale)
		{
			float coc = clamp((1.0 / focusPoint - 1.0 / depth)*focusScale, -1.0, 1.0);
			return abs(coc) * DOF_BLUR_SIZE;
		}
		
		vec3 depthOfField(vec2 texCoord, float focusPoint, float focusScale)
		{
			vec3 color = texture(gCombined, texCoord).rgb;
			float centerDepth = texture2D(gDepth, texCoord).x;
			float centerSize = getBlurSize(centerDepth, focusPoint, focusScale);
			float tot = 1.0;
			
			vec2 texelSize = 1.0 / Resolution * 1.5;
			
			const float quality = 1.0 - DOF_QUALITY;
			float radius = quality;
			for (float ang = 0.0; radius < DOF_BLUR_SIZE; ang += 2.39996323)
			{
				vec2 tc = texCoord + vec2(cos(ang), sin(ang)) * texelSize * radius;
				
				float sampleDepth = texture2D(gDepth, tc).x;
				float sampleSize = getBlurSize(sampleDepth, focusPoint, focusScale);
				
				vec3 sampleColor = texture(gCombined, tc).rgb;
				
				if (sampleDepth > centerDepth)
				{
					sampleSize = clamp(sampleSize, 0.0, centerSize*2.0);
				}
				
				float m = smoothstep(radius-0.5, radius+0.5, sampleSize);
				color += mix(color/tot, sampleColor, m);
				tot += 1.0;
				radius += quality/radius;
			}
			
			return color / tot;
		}


		void main()
		{
			vec2 texCoords = gl_FragCoord.xy / Resolution;
			float centerDepth = texture2D(gDepth, vec2(0.5,0.5)).x;
			//OutputColor = vec4(depthOfField(texCoords, focusDistance, focusStrength), 1.0);
			OutputColor = vec4(depthOfField(texCoords, centerDepth, focusStrength), 1.0);
		}

	}
}