Shader "Default/Standard"

Properties
{
    _MainTex ("Albedo", Texture2D) = "grid"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)

    _NormalTex ("Normal", Texture2D) = "normal"

    _SurfaceTex ("Surface (AO, Roughness, Metallicness)", Texture2D) = "surface"

    _EmissionTex ("Emission", Texture2D) = "emission"
    _EmissionIntensity ("Emission Intensity", Float) = 1.0

}

Pass "Standard"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull Back

	GLSLPROGRAM

		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec3 worldPos;
			out vec4 currentPos;
			out vec4 previousPos;
			out float fogCoord;

			out vec4 vColor;
			out vec3 vNormal;
			out vec3 vTangent;
			out vec3 vBitangent;

			void main()
			{
#ifdef SKINNED
				// Apply skinning transformations
				vec4 skinnedPos = GetSkinnedPosition(vertexPosition);
				vec3 skinnedNormal = GetSkinnedNormal(vertexNormal);

				gl_Position = PROWL_MATRIX_MVP * skinnedPos;
				fogCoord = gl_Position.z;
				currentPos = gl_Position; // Clip space
				texCoord0 = vertexTexCoord0;

				// Previous position with current projection (using skinned position)
				vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * skinnedPos;
				previousPos = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;

				worldPos = (PROWL_MATRIX_M * skinnedPos).xyz;

				vColor = vertexColor;

				vNormal = normalize(mat3(PROWL_MATRIX_M) * skinnedNormal);
#ifdef HAS_TANGENTS
				// For skinned meshes, also transform tangents
				vec3 skinnedTangent = GetSkinnedNormal(vertexTangent.xyz);
				vTangent = normalize(mat3(PROWL_MATRIX_M) * skinnedTangent);
				vBitangent = cross(vNormal, vTangent);
#endif
#else
				// Non-skinned rendering (original code)
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
				fogCoord = gl_Position.z;
				currentPos = gl_Position; // Clip space
				texCoord0 = vertexTexCoord0;

				// Previous position with current projection
				vec4 prevWorldPos = PROWL_MATRIX_M_PREVIOUS * vec4(vertexPosition, 1.0);
				previousPos = PROWL_MATRIX_VP_PREVIOUS * prevWorldPos;

				worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;

				vColor = vertexColor;

				vNormal = normalize(mat3(PROWL_MATRIX_M) * vertexNormal);
#ifdef HAS_TANGENTS
				vTangent = normalize(mat3(PROWL_MATRIX_M) * vertexTangent.xyz);
				vBitangent = cross(vNormal, vTangent);
#endif
#endif
			}
		}

		Fragment
		{
            #include "Fragment"
            #include "Lighting"

			#define USEGENERATEDNORMALS

			layout (location = 0) out vec4 gAlbedo;
			layout (location = 1) out vec4 gMotionVector;
			layout (location = 2) out vec4 gNormal;
			layout (location = 3) out vec4 gSurface;

			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 currentPos;
			in vec4 previousPos;
			in float fogCoord;
			in vec4 vColor;
			in vec3 vNormal;
			in vec3 vTangent;
			in vec3 vBitangent;

			uniform sampler2D _MainTex; // diffuse
			uniform sampler2D _NormalTex; // normal
			uniform sampler2D _SurfaceTex; // surface - AO, roughness, metallic
			uniform sampler2D _EmissionTex; // emission
			uniform float _EmissionIntensity; // emission intensity

			uniform sampler2D _ShadowAtlas;

			uniform vec4 _MainColor;

			#define MAX_SPOT_LIGHTS 8
			#define MAX_POINT_LIGHTS 8

			uniform SunLightStruct _Sun;
			uniform SpotLightStruct _SpotLights[MAX_SPOT_LIGHTS];
			uniform int _SpotLightCount;
			uniform PointLightStruct _PointLights[MAX_POINT_LIGHTS];
			uniform int _PointLightCount;

            // Generated Normals implementation (unique to Standard shader)
            const float normalThreshold = 0.05;
            const float normalClamp = 0.5;

            float GetDif(float lOriginalAlbedo, vec2 offsetCoord) {
                float lNearbyAlbedo = length(texture(_MainTex, offsetCoord).rgb);

                float dif = lOriginalAlbedo - lNearbyAlbedo;

                if (dif > 0.0) dif = max(dif - normalThreshold, 0.0);
                else           dif = min(dif + normalThreshold, 0.0);

                return clamp(dif, -normalClamp, normalClamp);
            }

            vec3 GenerateNormals(vec3 color, mat3 TBN) {
                // Calculate texture dimensions
                vec2 texSize = vec2(textureSize(_MainTex, 0));
                vec2 texelSize = 1.0 / texSize;

                float lOriginalAlbedo = length(color.rgb);
                float normalMult = 1.0;

                vec3 normalMap = vec3(0.0, 0.0, 1.0);

                // Sample in four directions around current texel
                vec2 offsetCoord = texCoord0 + vec2(0.0, texelSize.y);
                normalMap.y += GetDif(lOriginalAlbedo, offsetCoord);

                offsetCoord = texCoord0 + vec2(texelSize.x, 0.0);
                normalMap.x += GetDif(lOriginalAlbedo, offsetCoord);

                offsetCoord = texCoord0 + vec2(0.0, -texelSize.y);
                normalMap.y -= GetDif(lOriginalAlbedo, offsetCoord);

                offsetCoord = texCoord0 + vec2(-texelSize.x, 0.0);
                normalMap.x -= GetDif(lOriginalAlbedo, offsetCoord);

                normalMap.xy *= normalMult;
                normalMap.xy = clamp(normalMap.xy, vec2(-1.0), vec2(1.0));

                if (normalMap.xy != vec2(0.0, 0.0)) {
                    return normalize(TBN * normalMap);
                }

                return normalize(vNormal);
            }

			void main()
			{
				// Calculate screen-space motion vector
				// Convert positions to NDC space [-1,1]
				vec2 curNDC = (currentPos.xy / currentPos.w) - _CameraJitter;
				vec2 prevNDC = (previousPos.xy / previousPos.w) - _CameraPreviousJitter;
			    gMotionVector = vec4((curNDC - prevNDC) * 0.5, 0.0, 1.0);

				// Albedo
				vec4 albedo = texture(_MainTex, texCoord0) * vColor;

				// Normals
                vec3 worldNormal;
#ifdef HAS_TANGENTS
				// Create tangent to world matrix
				mat3 TBN = mat3(normalize(vTangent), normalize(vBitangent), normalize(vNormal));


                // Normal mapping with fallback to generated normals
                #ifdef USEGENERATEDNORMALS
                    // Generate normals from albedo texture
                    worldNormal = GenerateNormals(albedo.rgb, TBN);
                #else
                    // Sample the normal map (original approach)
                    vec3 normalMapSample = texture(_NormalTex, texCoord0).rgb;
                    // Convert from [0,1] to [-1,1] range
                    vec3 normalTS = normalMapSample * 2.0 - 1.0;
                    // Transform normal from tangent space to world space
                    worldNormal = normalize(TBN * normalTS);
                #endif
#else
                worldNormal = vNormal;
#endif
                // Transform to view space
                vec3 viewNormal = normalize(mat3(PROWL_MATRIX_V) * worldNormal);

                // Output the normal
                gNormal = vec4(viewNormal, 1.0); // Add explicit alpha

				// AO, roughness, metallic
				vec4 surface = texture(_SurfaceTex, texCoord0);
				float ao = 1.0 - surface.r;
				float roughness = surface.g;
				float metallic = surface.b;
				gSurface = vec4(roughness, metallic, 0.0, 1.0); // Add explicit alpha

				// Emission
				vec4 emission = texture(_EmissionTex, texCoord0) * _EmissionIntensity;

				// Base color
				vec3 baseColor = albedo.rgb;
				baseColor.rgb = GammaToLinearSpace(baseColor.rgb);

				// Calculate lighting
				vec3 lighting = CalculateDirectionalLight(_Sun, worldPos, worldNormal, _WorldSpaceCameraPos.xyz, baseColor, metallic, roughness, ao, _ShadowAtlas, prowl_ShadowAtlasSize);
				lighting += baseColor.rgb * CalculateAmbient(worldNormal);

				// Add spot lights
				for (int i = 0; i < _SpotLightCount && i < MAX_SPOT_LIGHTS; i++) {
					lighting += CalculateSpotLight(_SpotLights[i], worldPos, worldNormal, _WorldSpaceCameraPos.xyz, baseColor, metallic, roughness, ao, _ShadowAtlas, prowl_ShadowAtlasSize);
				}

				// Add point lights
				for (int i = 0; i < _PointLightCount && i < MAX_POINT_LIGHTS; i++) {
					lighting += CalculatePointLight(_PointLights[i], worldPos, worldNormal, _WorldSpaceCameraPos.xyz, baseColor, metallic, roughness, ao, _ShadowAtlas, prowl_ShadowAtlasSize);
				}

				// Add emission
				lighting += emission.rgb * 1.0;

				// Final output
				gAlbedo = vec4(lighting, 1.0);

				// Apply fog
				gAlbedo.rgb = ApplyFog(fogCoord, gAlbedo.rgb);
			}
		}
	ENDGLSL
}

Pass "StandardMotionVector"
{
    Tags { "RenderOrder" = "DepthOnly" }

    // Rasterizer culling mode
    Cull Back

	GLSLPROGRAM

		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			void main()
			{
#ifdef SKINNED
				// Apply skinning for depth pre-pass
				vec4 skinnedPos = GetSkinnedPosition(vertexPosition);
				gl_Position = PROWL_MATRIX_MVP * skinnedPos;
#else
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
#endif
			}
		}

		Fragment
		{
            #include "Fragment"

			void main()
			{
			}
		}
	ENDGLSL
}

Pass "StandardShadow"
{
    Tags { "LightMode" = "ShadowCaster" }

    // Rasterizer culling mode
    Cull Back

	GLSLPROGRAM

		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec3 worldPos;
			out vec3 worldNormal;

			void main()
			{
#ifdef SKINNED
				// Apply skinning for shadows
				vec4 skinnedPos = GetSkinnedPosition(vertexPosition);
				vec3 skinnedNormal = GetSkinnedNormal(vertexNormal);

				gl_Position = PROWL_MATRIX_MVP * skinnedPos;
				worldPos = (PROWL_MATRIX_M * skinnedPos).xyz;
				worldNormal = normalize(mat3(PROWL_MATRIX_M) * skinnedNormal);
#else
				gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
				worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
				worldNormal = normalize(mat3(PROWL_MATRIX_M) * vertexNormal);
#endif
			}
		}

		Fragment
		{
            #include "Fragment"

			in vec3 worldPos;
			in vec3 worldNormal;

			// Point light shadow uniforms (will be -1 for directional/spot lights)
			uniform vec3 _PointLightPosition;
			uniform float _PointLightRange;
			uniform float _PointLightShadowBias;

			void main()
			{
				// Check if we're rendering for a point light (_PointLightRange > 0)
				if (_PointLightRange > 0.0) {
					// Calculate direction from fragment to light
					vec3 lightDir = normalize(_PointLightPosition - worldPos);

					// Calculate slope-scale bias based on surface angle
					float cosTheta = clamp(dot(worldNormal, lightDir), 0.0, 1.0);
					float slopeBias = sqrt(1.0 - cosTheta * cosTheta) / cosTheta; // tan(acos(cosTheta))

					// Calculate distance from light and normalize by range
					float dist = length(worldPos - _PointLightPosition);
					float normalizedDepth = dist / _PointLightRange;

					// Apply adaptive bias (stronger bias for sharper angles)
					float bias = _PointLightShadowBias * 0.01 * (1.0 + slopeBias);
					normalizedDepth += bias;

					// Write normalized depth to gl_FragDepth
					gl_FragDepth = clamp(normalizedDepth, 0.0, 1.0);
				}
				else
                {
                    gl_FragDepth = gl_FragCoord.z;
                }
			}
		}
	ENDGLSL
}
