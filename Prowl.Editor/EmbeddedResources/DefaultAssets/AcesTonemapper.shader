Shader "Default/GBuffer"

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
		uniform vec2 Resolution;
		uniform float Contrast;
		uniform float Saturation;

		uniform sampler2D gAlbedo;
		
		layout(location = 0) out vec4 OutputColor;
		

		const mat3x3 ACESInputMat = mat3x3
		(
		    0.59719, 0.35458, 0.04823,
		    0.07600, 0.90834, 0.01566,
		    0.02840, 0.13383, 0.83777
		);
		
		// ODT_SAT => XYZ => D60_2_D65 => sRGB
		const mat3x3 ACESOutputMat = mat3x3
		(
		     1.60475, -0.53108, -0.07367,
		    -0.10208,  1.10813, -0.00605,
		    -0.00327, -0.07276,  1.07602
		);
		
		vec3 RRTAndODTFit(vec3 v)
		{
		    vec3 a = v * (v + 0.0245786f) - 0.000090537f;
		    vec3 b = v * (0.983729f * v + 0.4329510f) + 0.238081f;
		    return a / b;
		}
		
		vec3 ACESFitted(vec3 color)
		{
		    color = color * ACESInputMat;
		
		    // Apply RRT and ODT
		    color = RRTAndODTFit(color);
		
		    color = color * ACESOutputMat;
		
		    // Clamp to [0, 1]
		  	color = clamp(color, 0.0, 1.0);
		
		    return color;
		}
		
		mat4 contrastMatrix()
		{
			float t = (1.0 - Contrast) / 2.0;
		    
		    return mat4(Contrast, 0, 0, 0,
		                0, Contrast, 0, 0,
		                0, 0, Contrast, 0,
		                t, t, t, 1);
		
		}
		
		
		mat4 saturationMatrix()
		{
		    vec3 luminance = vec3(0.3086, 0.6094, 0.0820);
		    
		    float oneMinusSat = 1.0 - Saturation;
		    
		    vec3 red = vec3(luminance.x * oneMinusSat);
		    red+= vec3(Saturation, 0, 0);
		    
		    vec3 green = vec3(luminance.y * oneMinusSat);
		    green += vec3(0, Saturation, 0);
		    
		    vec3 blue = vec3(luminance.z * oneMinusSat);
		    blue += vec3(0, 0, Saturation);
		    
		    return mat4(red,     0,
		                green,   0,
		                blue,    0,
		                0, 0, 0, 1);
		}

		void main()
		{
			vec2 texCoords = gl_FragCoord.xy / Resolution;
			vec3 color = texture(gAlbedo, texCoords).rgb;
		
			#ifdef ACESTONEMAP
			color = ACESFitted(color);
			#endif

			#ifdef GAMMACORRECTION
			color = pow(color, vec3(1.0/2.2));
			#endif 

			OutputColor = contrastMatrix() * saturationMatrix() * vec4(color, 1.0);
		}
	}
}