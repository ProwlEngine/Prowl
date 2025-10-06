Shader "Default/SSR"

Pass "SSR"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    ZTest Off
    ZWrite Off
    Cull Off

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

        uniform sampler2D _MainTex;
        uniform sampler2D _CameraDepthTexture;
        uniform sampler2D _CameraNormalsTexture;
        uniform sampler2D _CameraSurfaceTexture;
        
        uniform int _RayStepCount;
        uniform float _ScreenEdgeFade;
        
        #include "Fragment"
        #include "Utilities"
		#include "PBR"
		#include "Random"
        
		vec3 calculateSSR(vec3 viewPos, vec3 screenPos, vec3 gBMVNorm, float dither) {
			vec3 reflectedScreenPos = rayTrace(screenPos, viewPos, reflect(normalize(viewPos), gBMVNorm), dither, _RayStepCount, 4, _CameraDepthTexture);
			if(reflectedScreenPos.z < 0.5) return vec3(0);
			return vec3(reflectedScreenPos.xy, 1);
		}

        void main()
        {
            // Start with the original color
			vec3 color = texture(_MainTex, TexCoords).xyz;
			OutputColor = vec4(color, 1.0);
            
            // Get surface data
            vec4 surfaceData = texture(_CameraSurfaceTexture, TexCoords); // R: Roughness, G: Metallicness
            float smoothness = 1.0 - surfaceData.r;
            float metallic = surfaceData.g;

			smoothness = smoothness * smoothness;

			if(smoothness > 0.01)
			{
                vec3 normal = normalize(texture(_CameraNormalsTexture, TexCoords).xyz);
				
				vec3 screenPos = getScreenPos(TexCoords, _CameraDepthTexture);
				vec3 viewPos = getViewFromScreenPos(screenPos);

				bool isMetal = metallic > 0.9;

				// Get fresnel
				vec3 F0 = vec3(0.04); 
				F0 = mix(F0, color, metallic);
				vec3 fresnel = FresnelSchlick(max(dot(normal, normalize(-viewPos)), 0.0), F0);
				
				float dither = fract(sin(dot(TexCoords + vec2(_Time.y, _Time.y), vec2(12.9898,78.233))) * 43758.5453123);

				vec3 SSRCoord = calculateSSR(viewPos, screenPos, normalize(normal), dither);
				if(SSRCoord.z > 0.5)
				{
					OutputColor.rgb *= isMetal ? vec3(1.0 - smoothness) : 1.0 - fresnel;
					OutputColor.rgb += texture(_MainTex, SSRCoord.xy).xyz * fresnel;
				}
			}
        }
    }

    ENDGLSL
}