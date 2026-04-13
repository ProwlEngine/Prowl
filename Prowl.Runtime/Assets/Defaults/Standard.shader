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
            #include "Lighting"

			layout (location = 0) out vec4 fragColor;

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

			void main()
			{
				// Albedo
				vec4 albedo = texture(_MainTex, texCoord0) * vColor * _MainColor;

				// Normals
                vec3 worldNormal;
#ifdef HAS_TANGENTS
				mat3 TBN = mat3(normalize(vTangent), normalize(vBitangent), normalize(vNormal));
                vec3 normalMapSample = texture(_NormalTex, texCoord0).rgb;
                vec3 normalTS = normalMapSample * 2.0 - 1.0;
                worldNormal = normalize(TBN * normalTS);
#else
                worldNormal = normalize(vNormal);
#endif

				// AO, roughness, metallic
				vec4 surface = texture(_SurfaceTex, texCoord0);
				float ao = 1.0 - surface.r;
				float roughness = surface.g;
				float metallic = surface.b;

				// Emission
				vec3 emission = texture(_EmissionTex, texCoord0).rgb * _EmissionIntensity;

				// Convert albedo to linear space
				vec3 baseColor = gammaToLinearSpace(albedo.rgb);

				// View direction
				vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);

				// Forward lighting
				vec3 lighting = CalculateForwardLighting(worldPos, worldNormal, viewDir,
				                                         baseColor, metallic, roughness, ao);

				// Ambient
				vec3 ambient = CalculateAmbient(worldNormal) * baseColor * ao * _AmbientStrength;

				// Combine
				vec3 color = ambient + lighting + emission;

				// Fog
				color = ApplyFog(color, worldPos);

				fragColor = vec4(color, albedo.a);
			}
		}
	ENDGLSL
}

Pass "DepthNormals"
{
    Tags { "LightMode" = "DepthNormals" }

    Cull Back

	GLSLPROGRAM

		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec3 vNormal;
			out vec3 vTangent;
			out vec3 vBitangent;
			out vec2 texCoord0;

#ifdef GPU_INSTANCING
            layout(location = 8) in vec4 instanceModelRow0;
            layout(location = 9) in vec4 instanceModelRow1;
            layout(location = 10) in vec4 instanceModelRow2;
            layout(location = 11) in vec4 instanceModelRow3;
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
				vNormal = normalize(mat3(modelMatrix) * skinnedNormal);
#ifdef HAS_TANGENTS
				vec3 skinnedTangent = GetSkinnedNormal(vertexTangent.xyz);
				vTangent = normalize(mat3(modelMatrix) * skinnedTangent);
				vBitangent = cross(vNormal, vTangent);
#endif
#else
				gl_Position = mvpMatrix * vec4(vertexPosition, 1.0);
				vNormal = normalize(mat3(modelMatrix) * vertexNormal);
#ifdef HAS_TANGENTS
				vTangent = normalize(mat3(modelMatrix) * vertexTangent.xyz);
				vBitangent = cross(vNormal, vTangent);
#endif
#endif
				texCoord0 = vertexTexCoord0;
			}
		}

		Fragment
		{
            #include "Fragment"

			layout (location = 0) out vec4 normalOut;

			in vec3 vNormal;
			in vec3 vTangent;
			in vec3 vBitangent;
			in vec2 texCoord0;

			uniform sampler2D _NormalTex;

			void main()
			{
                vec3 worldNormal;
#ifdef HAS_TANGENTS
				mat3 TBN = mat3(normalize(vTangent), normalize(vBitangent), normalize(vNormal));
                vec3 normalMapSample = texture(_NormalTex, texCoord0).rgb;
                vec3 normalTS = normalMapSample * 2.0 - 1.0;
                worldNormal = normalize(TBN * normalTS);
#else
                worldNormal = normalize(vNormal);
#endif
				// Encode view-space normal to [0,1]
				vec3 viewNormal = normalize(mat3(PROWL_MATRIX_V) * worldNormal);
				normalOut = vec4(viewNormal * 0.5 + 0.5, 1.0);
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
