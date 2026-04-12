Shader "Default/Grass"

Properties
{
    _MainTex ("Grass Texture", Texture2D) = "white"
    _AlphaCutoff ("Alpha Cutoff", Float) = 0.5
    _WindStrength ("Wind Strength", Float) = 0.3
    _WindSpeed ("Wind Speed", Float) = 1.5
    _Billboard ("Billboard", Float) = 1.0
    _AlignToNormal ("Align To Normal", Float) = 0.0
}

Pass "Grass"
{
    Tags { "RenderOrder" = "Opaque" }

    Cull Off
    ZWrite On
    Blend Off

	GLSLPROGRAM

		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec4 vColor;
            out vec3 worldPos;
            out vec3 vNormal;

#ifdef GPU_INSTANCING
            layout(location = 8) in vec4 instanceModelRow0;
            layout(location = 9) in vec4 instanceModelRow1;
            layout(location = 10) in vec4 instanceModelRow2;
            layout(location = 11) in vec4 instanceModelRow3;
            layout(location = 12) in vec4 instanceColor;
            layout(location = 13) in vec4 instanceCustomData;
#endif

            uniform float _WindStrength;
            uniform float _WindSpeed;
            uniform float _Billboard;
            uniform float _AlignToNormal;
            uniform vec3 _TerrainUp;
            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;

			void main()
			{
#ifdef GPU_INSTANCING
                // Instance matrix is in terrain-local space; transform to world
                mat4 terrainToWorld = inverse(_TerrainWorldToLocal);

                // Extract grass blade position (terrain-local) and scale
                vec3 localPosition = instanceModelRow3.xyz;
                vec3 bladePosition = (terrainToWorld * vec4(localPosition, 1.0)).xyz;
                float scaleX = length(instanceModelRow0.xyz); // width
                float scaleY = length(instanceModelRow1.xyz); // height

                // Determine up direction
                vec3 up = _TerrainUp;

                // Optionally align to terrain heightmap normal
                if (_AlignToNormal > 0.5)
                {
                    // Terrain UV from local position (already in terrain space)
                    vec2 terrainUV = localPosition.xz / _TerrainSize;
                    float hmSize = float(textureSize(_Heightmap, 0).x);
                    float texelSize = hmSize > 0.0 ? (1.0 / hmSize) : 0.001;

                    float hR = texture(_Heightmap, terrainUV + vec2(texelSize, 0.0)).r * _TerrainHeight;
                    float hL = texture(_Heightmap, terrainUV - vec2(texelSize, 0.0)).r * _TerrainHeight;
                    float hU = texture(_Heightmap, terrainUV + vec2(0.0, texelSize)).r * _TerrainHeight;
                    float hD = texture(_Heightmap, terrainUV - vec2(0.0, texelSize)).r * _TerrainHeight;

                    float wStep = texelSize * _TerrainSize;
                    vec3 localNormal = normalize(vec3(-(hR - hL) / (wStep * 2.0), 1.0, -(hU - hD) / (wStep * 2.0)));
                    // Transform local normal to world space
                    up = normalize((terrainToWorld * vec4(localNormal, 0.0)).xyz);
                }

                vec3 localOffset;
                if (_Billboard > 0.5)
                {
                    // Cylindrical billboard around terrain up axis
                    vec3 cameraRight = vec3(PROWL_MATRIX_V[0][0], PROWL_MATRIX_V[1][0], PROWL_MATRIX_V[2][0]);
                    // Project camera right perpendicular to up
                    cameraRight = normalize(cameraRight - up * dot(cameraRight, up));
                    localOffset = cameraRight * vertexPosition.x * scaleX
                                 + up * vertexPosition.y * scaleY;
                }
                else
                {
                    // Non-billboard: transform instance orientation from terrain-local to world
                    vec3 right = normalize((terrainToWorld * vec4(normalize(instanceModelRow0.xyz), 0.0)).xyz);
                    // Re-orthogonalize right to be perpendicular to up
                    right = normalize(right - up * dot(right, up));
                    localOffset = right * vertexPosition.x * scaleX
                                + up * vertexPosition.y * scaleY;
                }

                // Wind sway - only affects top vertices (y > 0)
                float windPhase = instanceCustomData.x;
                float bendFactor = instanceCustomData.y;
                float windAmount = max(0.0, vertexPosition.y);
                float wind = sin(_Time.y * _WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * _WindStrength * bendFactor;
                localOffset.x += wind * windAmount;
                localOffset.z += wind * windAmount * 0.3;

                vec3 worldPosition = bladePosition + localOffset;
                worldPosition += up * 0.05 * scaleY; // Small offset to prevent ground clipping
                worldPos = worldPosition;
                vNormal = up; // Normal matches terrain surface
                vColor = instanceColor;

                gl_Position = PROWL_MATRIX_VP * vec4(worldPosition, 1.0);
                texCoord0 = vertexTexCoord0;
#else
                gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
                texCoord0 = vertexTexCoord0;
                worldPos = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;
                vNormal = vec3(0.0, 1.0, 0.0);
                vColor = vec4(1.0);
#endif
			}
		}

		Fragment
		{
            #include "Fragment"

			layout (location = 0) out vec4 gBufferA;
			layout (location = 1) out vec4 gBufferB;
			layout (location = 2) out vec4 gBufferC;
			layout (location = 3) out vec4 gBufferD;

			in vec2 texCoord0;
			in vec4 vColor;
            in vec3 worldPos;
            in vec3 vNormal;

            uniform sampler2D _MainTex;
            uniform float _AlphaCutoff;

			void main()
			{
                vec4 texColor = texture(_MainTex, texCoord0);
                vec4 finalColor = texColor * vColor;

                // Alpha cutout
                if (finalColor.a < _AlphaCutoff)
                    discard;

                vec3 baseColor = gammaToLinearSpace(finalColor.rgb);
                vec3 viewNormal = normalize((PROWL_MATRIX_V * vec4(vNormal, 0.0)).xyz);

				gBufferA = vec4(baseColor, 1.0);
				gBufferB = vec4(viewNormal * 0.5 + 0.5, 1.0); // shading mode = 1 (lit)
				gBufferC = vec4(0.9, 0.0, 0.0, 0.0); // rough, non-metallic
				gBufferD = vec4(0.0);
			}
		}
	ENDGLSL
}

// No shadow pass — billboard grass shadows face the shadow camera which causes misalignment.
// Mesh-based details use the Standard shader which has its own shadow pass.
