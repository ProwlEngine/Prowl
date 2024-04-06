Shader "Default/DOF"

Pass 0
{
	DepthTest Off
	DepthWrite Off
	// DepthMode Less
	Blend On
	BlendSrc SrcAlpha
	BlendDst One
	BlendEquation FuncAdd
	Cull Off
	// Winding CW

	Vertex
	{
		in vec3 vertexPosition;
		in vec2 vertexTexCoord;
		
		out vec2 TexCoords;

		void main() 
		{
			gl_Position =vec4(vertexPosition, 1.0);
			TexCoords = vertexTexCoord;
		}
	}

	Fragment
	{
		layout(location = 0) out vec4 OutputColor;
		
		in vec2 TexCoords;
		uniform vec2 Resolution;
		
		uniform sampler2D gCombined; // Depth
		uniform sampler2D gDepth; // Depth

		uniform float u_Quality; // [0.1 0.15 0.2 0.25 0.3 0.35 0.4 0.45 0.5 0.55 0.6 0.65 0.7 0.75 0.8 0.85 0.9]
		uniform float u_BlurRadius; // [5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38 39 40]
		uniform float u_FocusStrength; // [10.0 15.0 25.0 30.0 35.0 40.0 45.0 50.0 65.0 100.0 200.0 300.0 400.0 500.0 600.0 700.0 800.0 900.0 1000.0 1250.0 1500.0 1750.0 2000.0 2500.0 3000.0]
		
		// ----------------------------------------------------------------------------
		
		float getBlurSize(float depth, float focusPoint, float focusScale)
		{
			float coc = clamp((1.0 / focusPoint - 1.0 / depth)*focusScale, -1.0, 1.0);
			return abs(coc) * u_BlurRadius;
		}
		
		vec3 depthOfField(vec2 texCoord, float focusPoint, float focusScale)
		{
			vec3 color = texture(gCombined, texCoord).rgb;
			float centerDepth = texture(gDepth, texCoord).x;
			float centerSize = getBlurSize(centerDepth, focusPoint, focusScale);
			float tot = 1.0;
			
			vec2 texelSize = 1.0 / Resolution * 1.5;
			
			float quality = 1.0 - u_Quality;
			float radius = quality;
			for (float ang = 0.0; radius < u_BlurRadius; ang += 2.39996323)
			{
				vec2 tc = texCoord + vec2(cos(ang), sin(ang)) * texelSize * radius;
				
				float sampleDepth = texture(gDepth, tc).x;
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
			float centerDepth = texture(gDepth, vec2(0.5,0.5)).x;
			//OutputColor = vec4(depthOfField(TexCoords, focusDistance, u_FocusStrength), 1.0);
			OutputColor = vec4(depthOfField(TexCoords, centerDepth, u_FocusStrength), 1.0);
		}

	}
}