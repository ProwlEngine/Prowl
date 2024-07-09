Shader "Default/ToneMapper"

Pass "ToneMapper"
{
    Tags { "RenderOrder" = "Composite" }
	
	DepthStencil
	{
		DepthTest Off
		DepthWrite Off
	}
	
    Blend
    {    
		Src Alpha SourceAlpha
		Dest Alpha One
		Mode Alpha Add
    }
	
    // Rasterizer culling mode
    Cull None

	Inputs
	{
		VertexInput 
        {
            Position // Input location 0
            UV0 // Input location 1
        }
        
        // Set 0
        Set
        {
            Buffer TonemappingUniforms
            {
				_Contrast Float
				_Saturation Float
			}

            Buffer DefaultUniforms
            {
                Mat_V Matrix4x4
                Mat_P Matrix4x4
                Mat_ObjectToWorld Matrix4x4
                Mat_WorldToObject Matrix4x4
                Mat_MVP Matrix4x4
				Time Float
            }

			SampledTexture _Texture
        }
	}

	PROGRAM VERTEX
		layout(location = 0) in vec3 vertexPosition;
		layout(location = 1) in vec2 vertexTexCoord;
		
		layout(location = 0) out vec2 TexCoords;
		
		void main() 
		{
			gl_Position = vec4(vertexPosition, 1.0);
			
			TexCoords = vertexTexCoord;
		}
	ENDPROGRAM

	PROGRAM FRAGMENT	
		layout(location = 0) in vec2 TexCoords;

		layout(location = 0) out vec4 result;

		layout(set = 0, binding = 0, std140) uniform TonemappingUniforms
		{
			float _Contrast;
			float _Saturation;
		};
		
		layout(set = 0, binding = 1, std140) uniform DefaultUniforms
		{
			mat4 Mat_V;
			mat4 Mat_P;
			mat4 Mat_ObjectToWorld;
			mat4 Mat_WorldToObject;
			mat4 Mat_MVP;
			float Time;
		};

		layout(set = 0, binding = 2) uniform texture2D _Texture;
		layout(set = 0, binding = 3) uniform sampler _TextureSampler;
		
		// TODO: A Keyword/Feature to switch between different tonemapping operators
		#define MELON

#ifdef MELON
		// Shifts red to Yellow, green to Yellow, blue to Cyan,
		vec3 HueShift(vec3 In)
		{
			float A = max(In.x, In.y);
			return vec3(A, max(A, In.z), In.z); 
		}
		
		float Cmax(vec3 In)
		{
			return max(max(In.r, In.g), In.b);
		}

		// Made by TripleMelon
		vec3 MelonTonemap(vec3 color)
		{
			// remaps the colors to [0-1] range
			// tested to be as close ti ACES contrast levels as possible
			color = pow(color, vec3(1.56, 1.56, 1.56)); 
			color = color/(color + 0.84);
			
			// governs the transition to white for high color intensities
			float factor = Cmax(color) * 0.15; // multiply by 0.15 to get a similar look to ACES
			factor = factor / (factor + 1); // remaps the factor to [0-1] range
			factor *= factor; // smooths the transition to white
			
			// shift the hue for high intensities (for a more pleasing look).
			color = mix(color, HueShift(color), factor); // can be removed for more neutral colors
			color = mix(color, vec3(1.0, 1.0, 1.0), factor); // shift to white for high intensities
			
		    return color;
		}
#endif

#ifdef ACES
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
		
		    return color;
		}
#endif

#ifdef REINHARD
		vec3 lumaBasedReinhardToneMapping(vec3 color)
		{
			float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
			float toneMappedLuma = luma / (1. + luma);
			color *= toneMappedLuma / luma;
			return color;
		}
#endif

#ifdef FILMIC
		vec3 filmicToneMapping(vec3 color)
		{
			color = max(vec3(0.), color - vec3(0.004));
			color = (color * (6.2 * color + .5)) / (color * (6.2 * color + 1.7) + 0.06);
			return color;
		}
#endif

#ifdef UNCHARTED
		vec3 Uncharted2ToneMapping(vec3 color)
		{
			float A = 0.15;
			float B = 0.50;
			float C = 0.10;
			float D = 0.20;
			float E = 0.02;
			float F = 0.30;
			float W = 11.2;
			float exposure = 2.;
			color *= exposure;
			color = ((color * (A * color + C * B) + D * E) / (color * (A * color + B) + D * F)) - E / F;
			float white = ((W * (A * W + C * B) + D * E) / (W * (A * W + B) + D * F)) - E / F;
			color /= white;
			return color;
		}
#endif
		

		
		vec3 Tonemap(vec3 color) 
		{
#ifdef MELON
			color = MelonTonemap(color);
#endif
#ifdef ACES
			color = ACESFitted(color);
#endif
#ifdef REINHARD
			color = lumaBasedReinhardToneMapping(color);
#endif
#ifdef UNCHARTED
			color = Uncharted2ToneMapping(color);
#endif
#ifdef FILMIC
			color = filmicToneMapping(color);
#endif
		
		    // Clamp to [0, 1]
		  	color = clamp(color, 0.0, 1.0);

			return color;
		}


		mat4 contrastMatrix()
		{
			float t = (1.0 - _Contrast) / 2.0;
		    return mat4(_Contrast, 0, 0, 0,
		                0, _Contrast, 0, 0,
		                0, 0, _Contrast, 0,
		                t, t, t, 1);
		}
		
		mat4 saturationMatrix()
		{
		    vec3 luminance = vec3(0.3086, 0.6094, 0.0820);
		    
		    float oneMinusSat = 1.0 - _Saturation;
		    
		    vec3 red = vec3(luminance.x * oneMinusSat);
		    red+= vec3(_Saturation, 0, 0);
		    
		    vec3 green = vec3(luminance.y * oneMinusSat);
		    green += vec3(0, _Saturation, 0);
		    
		    vec3 blue = vec3(luminance.z * oneMinusSat);
		    blue += vec3(0, 0, _Saturation);
		    
		    return mat4(red,     0,
		                green,   0,
		                blue,    0,
		                0, 0, 0, 1);
		}

		void main()
		{
			vec3 color = texture(sampler2D(_Texture, _TextureSampler), TexCoords).rgb;
		
			color = Tonemap(color);

			result = contrastMatrix() * saturationMatrix() * vec4(color, 1.0);
			
			// Gamma Correction
			result.rgb = pow(result.rgb, vec3(1.0 / 2.2));
		}
	ENDPROGRAM
}