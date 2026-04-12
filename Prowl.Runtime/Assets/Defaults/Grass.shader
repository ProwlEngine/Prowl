Shader "Default/Grass"

Properties
{
    _MainTex ("Grass Texture", Texture2D) = "white"
    _AlphaCutoff ("Alpha Cutoff", Float) = 0.5
    _WindStrength ("Wind Strength", Float) = 0.3
    _WindSpeed ("Wind Speed", Float) = 1.5
    _Billboard ("Billboard", Float) = 1.0
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

			void main()
			{
#ifdef GPU_INSTANCING
                // Extract grass blade position and scale
                vec3 bladePosition = instanceModelRow3.xyz;
                float scaleX = length(instanceModelRow0.xyz); // width
                float scaleY = length(instanceModelRow1.xyz); // height

                vec3 localOffset;
                if (_Billboard > 0.5)
                {
                    // Cylindrical billboard: face camera around Y axis only
                    vec3 cameraRight = vec3(PROWL_MATRIX_V[0][0], PROWL_MATRIX_V[1][0], PROWL_MATRIX_V[2][0]);
                    cameraRight.y = 0.0;
                    cameraRight = normalize(cameraRight);
                    vec3 up = vec3(0.0, 1.0, 0.0);
                    localOffset = cameraRight * vertexPosition.x * scaleX
                                 + up * vertexPosition.y * scaleY;
                }
                else
                {
                    // Non-billboard: use the instance matrix orientation
                    vec3 right = normalize(instanceModelRow0.xyz);
                    vec3 up = normalize(instanceModelRow1.xyz);
                    localOffset = right * vertexPosition.x * scaleX
                                + up * vertexPosition.y * scaleY;
                }

                // Wind sway - only affects top vertices (y > 0)
                float windPhase = instanceCustomData.x; // per-blade phase offset
                float bendFactor = instanceCustomData.y; // per-type bend amount
                float windAmount = max(0.0, vertexPosition.y); // 0 at base, 1 at top
                float wind = sin(_Time.y * _WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * _WindStrength * bendFactor;
                localOffset.x += wind * windAmount;
                localOffset.z += wind * windAmount * 0.3;

                vec3 worldPosition = bladePosition + localOffset;
                worldPosition.y += 0.05 * scaleY; // Small offset to prevent ground clipping
                worldPos = worldPosition;
                vNormal = vec3(0.0, 1.0, 0.0); // Upward-facing normal for lighting
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
