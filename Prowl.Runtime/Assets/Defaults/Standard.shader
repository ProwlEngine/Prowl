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

			out vec4 vColor;
			out vec3 vNormal;
			out vec3 vTangent;
			out vec3 vBitangent;

#ifdef GPU_INSTANCING
            layout(location = 8) in vec4 instanceModelRow0;
            layout(location = 9) in vec4 instanceModelRow1;
            layout(location = 10) in vec4 instanceModelRow2;
            layout(location = 11) in vec4 instanceModelRow3;
            layout(location = 12) in vec4 instanceColor;
            layout(location = 13) in vec4 instanceCustomData;
#endif

			void main()
			{
				// Determine model matrix (instanced or per-object)
#ifdef GPU_INSTANCING
				mat4 modelMatrix = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
				mat4 mvpMatrix = PROWL_MATRIX_VP * modelMatrix;
#else
				mat4 modelMatrix = PROWL_MATRIX_M;
				mat4 mvpMatrix = PROWL_MATRIX_MVP;
#endif

#ifdef SKINNED
				vec4 skinnedPos = GetSkinnedPosition(vertexPosition);
				vec3 skinnedNormal = GetSkinnedNormal(vertexNormal);

				gl_Position = mvpMatrix * skinnedPos;
				texCoord0 = vertexTexCoord0;
				worldPos = (modelMatrix * skinnedPos).xyz;
				vColor = vertexColor;
				vNormal = normalize(mat3(modelMatrix) * skinnedNormal);
#ifdef HAS_TANGENTS
				vec3 skinnedTangent = GetSkinnedNormal(vertexTangent.xyz);
				vTangent = normalize(mat3(modelMatrix) * skinnedTangent);
				vBitangent = cross(vNormal, vTangent);
#endif
#else
				gl_Position = mvpMatrix * vec4(vertexPosition, 1.0);
				texCoord0 = vertexTexCoord0;
				worldPos = (modelMatrix * vec4(vertexPosition, 1.0)).xyz;
				vColor = vertexColor;
				vNormal = normalize(mat3(modelMatrix) * vertexNormal);
#ifdef HAS_TANGENTS
				vTangent = normalize(mat3(modelMatrix) * vertexTangent.xyz);
				vBitangent = cross(vNormal, vTangent);
#endif
#endif

#ifdef GPU_INSTANCING
				vColor *= instanceColor;
#endif
			}
		}

		Fragment
		{
            #include "Fragment"

			//#define USEGENERATEDNORMALS

			// GBuffer layout:
			// BufferA: RGB = Albedo, A = AO
			// BufferB: RGB = Normal (view space), A = ShadingMode
			// BufferC: R = Roughness, G = Metalness, B = Specular, A = Unused
			// BufferD: Custom Data per Shading Mode (e.g., shading mode 0 = Unlit with RGBA as Emissive)
			layout (location = 0) out vec4 gBufferA;
			layout (location = 1) out vec4 gBufferB;
			layout (location = 2) out vec4 gBufferC;
			layout (location = 3) out vec4 gBufferD;

			in vec2 texCoord0;
			in vec3 worldPos;
			in vec4 vColor;
			in vec3 vNormal;
			in vec3 vTangent;
			in vec3 vBitangent;

			uniform sampler2D _MainTex; // diffuse
			uniform sampler2D _NormalTex; // normal
			uniform sampler2D _SurfaceTex; // surface - AO, roughness, metallic
			uniform sampler2D _EmissionTex; // emission
			uniform float _EmissionIntensity; // emission intensity

			uniform vec4 _MainColor;

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
				// Albedo
				vec4 albedo = texture(_MainTex, texCoord0) * vColor * _MainColor;

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

				// AO, roughness, metallic
				vec4 surface = texture(_SurfaceTex, texCoord0);
				float ao = 1.0 - surface.r;
				float roughness = surface.g;
				float metallic = surface.b;

				// Emission
				vec4 emission = texture(_EmissionTex, texCoord0) * _EmissionIntensity;

				// Convert albedo to linear space
				vec3 baseColor = albedo.rgb;
				baseColor.rgb = gammaToLinearSpace(baseColor.rgb);

				// Calculate specular from metallic workflow
				// For non-metals, specular is 0.04 (4% reflectance)
				// For metals, specular is derived from albedo
				float specular = mix(0.04, 1.0, metallic);

				// Output to GBuffer
				// BufferA: RGB = Albedo, A = AO
				gBufferA = vec4(baseColor, ao);

				// BufferB: RGB = Normal (view space), A = ShadingMode
				// ShadingMode: 0 = Unlit, 1 = Lit
				float shadingMode = 1.0; // Lit by default for Standard shader
				gBufferB = vec4(viewNormal * 0.5 + 0.5, shadingMode); // Encode normal to [0,1] range

				// BufferC: R = Roughness, G = Metalness, B = Specular, A = Unused
				gBufferC = vec4(roughness, metallic, specular, 0.0);

				// BufferD: Custom Data per Shading Mode
				// For Lit mode (1), we store emission data
				gBufferD = vec4(emission.rgb, 0.0);
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

#ifdef GPU_INSTANCING
            layout(location = 8) in vec4 instanceModelRow0;
            layout(location = 9) in vec4 instanceModelRow1;
            layout(location = 10) in vec4 instanceModelRow2;
            layout(location = 11) in vec4 instanceModelRow3;
            layout(location = 12) in vec4 instanceColor;
#endif

			void main()
			{
#ifdef GPU_INSTANCING
				mat4 modelMatrix = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
				mat4 mvpMatrix = PROWL_MATRIX_VP * modelMatrix;
#else
				mat4 modelMatrix = PROWL_MATRIX_M;
				mat4 mvpMatrix = PROWL_MATRIX_MVP;
#endif

#ifdef SKINNED
				vec4 skinnedPos = GetSkinnedPosition(vertexPosition);
				vec3 skinnedNormal = GetSkinnedNormal(vertexNormal);
				gl_Position = mvpMatrix * skinnedPos;
				worldPos = (modelMatrix * skinnedPos).xyz;
				worldNormal = normalize(mat3(modelMatrix) * skinnedNormal);
#else
				gl_Position = mvpMatrix * vec4(vertexPosition, 1.0);
				worldPos = (modelMatrix * vec4(vertexPosition, 1.0)).xyz;
				worldNormal = normalize(mat3(modelMatrix) * vertexNormal);
#endif
			}
		}

		Fragment
		{
            #include "Fragment"

			void main()
			{
                    gl_FragDepth = gl_FragCoord.z;
			}
		}
	ENDGLSL
}
