Shader "Default/Grass"

Properties
{
    _MainTex ("Grass Texture", Texture2D) = "white"
    _AlphaCutoff ("Alpha Cutoff", Float) = 0.5
    _WindStrength ("Wind Strength", Float) = 0.3
    _WindSpeed ("Wind Speed", Float) = 1.5
    _Billboard ("Billboard", Float) = 1.0
    _AlignToNormal ("Align To Normal", Float) = 0.0
    _GrassDistance ("Grass Max Distance", Float) = 150.0
    _GrassFadeStart ("Grass Fade Start (world units)", Float) = 90.0
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

            uniform float _WindStrength;
            uniform float _WindSpeed;
            uniform float _Billboard;
            uniform float _AlignToNormal;
            uniform float _GrassDistance;
            uniform float _GrassFadeStart;
            uniform vec3 _TerrainUp;
            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

			void main()
			{
#ifdef GPU_INSTANCING
                // Instance matrix is in terrain-local space; transform to world
                mat4 terrainToWorld = _TerrainLocalToWorld;

                // Extract grass blade position (terrain-local) and scale
                vec3 localPosition = instanceModelRow3.xyz;
                vec3 bladePosition = (terrainToWorld * vec4(localPosition, 1.0)).xyz;
                float scaleX = length(instanceModelRow0.xyz); // width
                float scaleY = length(instanceModelRow1.xyz); // height

                // Distance fade shrink blades to zero between _GrassFadeStart and _GrassDistance
                // so patches that the CPU-side culler is about to drop don't pop out suddenly.
                // Horizontal camera distance matches the CPU's XZ-plane patch cull.
                vec3 camFlat = vec3(_WorldSpaceCameraPos.x, bladePosition.y, _WorldSpaceCameraPos.z);
                float camDist = length(bladePosition - camFlat);
                float fadeRange = max(0.0001, _GrassDistance - _GrassFadeStart);
                float fade = 1.0 - clamp((camDist - _GrassFadeStart) / fadeRange, 0.0, 1.0);
                scaleX *= fade;
                scaleY *= fade;

                // Always compute terrain surface normal from heightmap (for lighting).
                // Remap vertex-UV → texel-center-UV so the GPU samples the same values
                // the CPU-side TerrainData.GetInterpolatedHeight would return (which is what
                // placed this grass blade's Y position in the first place).
                vec2 terrainUV = localPosition.xz / _TerrainSize;
                vec2 hmSize2 = vec2(textureSize(_Heightmap, 0));
                float hmSize = hmSize2.x;
                float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;

                vec2 baseUV = terrainUV * (hmSize2 - 1.0) / hmSize2 + 0.5 / hmSize2;
                vec2 stepUV = vec2(vertStep * (hmSize - 1.0) / hmSize, 0.0);
                vec2 stepVV = vec2(0.0, vertStep * (hmSize - 1.0) / hmSize);
                float hR = texture(_Heightmap, baseUV + stepUV).r * _TerrainHeight;
                float hL = texture(_Heightmap, baseUV - stepUV).r * _TerrainHeight;
                float hU = texture(_Heightmap, baseUV + stepVV).r * _TerrainHeight;
                float hD = texture(_Heightmap, baseUV - stepVV).r * _TerrainHeight;

                float wStep = vertStep * _TerrainSize;
                vec3 localNormal = normalize(vec3(-(hR - hL) / (wStep * 2.0), 1.0, -(hU - hD) / (wStep * 2.0)));
                vec3 terrainNormal = normalize((terrainToWorld * vec4(localNormal, 0.0)).xyz);

                // Up direction for blade orientation: terrain normal if AlignToNormal, else terrain's Y axis
                vec3 up = (_AlignToNormal > 0.5) ? terrainNormal : _TerrainUp;

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
                worldPosition += up * 0.01 * scaleY; // Minimal offset to reduce ground clipping
                worldPos = worldPosition;
                vNormal = terrainNormal; // Always use terrain surface normal for lighting
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
            #include "Lighting"

			layout (location = 0) out vec4 fragColor;

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

                if (finalColor.a < _AlphaCutoff)
                    discard;

                vec3 baseColor = gammaToLinearSpace(finalColor.rgb);
                vec3 normal = normalize(vNormal);

                // Forward lighting grass is rough, non-metallic
                vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                vec3 lighting = CalculateForwardLighting(worldPos, normal, viewDir,
                                                         baseColor, 0.0, 0.9, 1.0);
                vec3 ambient = CalculateAmbient(normal) * baseColor * _AmbientStrength;
                vec3 color = ambient + lighting;
                color = ApplyFog(color, worldPos);

				fragColor = vec4(color, 1.0);
			}
		}
	ENDGLSL
}

Pass "GrassDepthNormals"
{
    Tags { "LightMode" = "DepthNormals" }
    Cull Off

	GLSLPROGRAM

		Vertex
		{
            #include "Fragment"
            #include "VertexAttributes"

            out vec3 vNormal;
            out vec2 texCoord0;

            uniform float _WindStrength;
            uniform float _WindSpeed;
            uniform float _Billboard;
            uniform float _AlignToNormal;
            uniform float _GrassDistance;
            uniform float _GrassFadeStart;
            uniform vec3 _TerrainUp;
            uniform sampler2D _Heightmap;
            uniform float _TerrainSize;
            uniform float _TerrainHeight;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

			void main()
			{
#ifdef GPU_INSTANCING
                mat4 terrainToWorld = _TerrainLocalToWorld;
                vec3 localPosition = instanceModelRow3.xyz;
                vec3 bladePosition = (terrainToWorld * vec4(localPosition, 1.0)).xyz;
                float scaleX = length(instanceModelRow0.xyz);
                float scaleY = length(instanceModelRow1.xyz);

                // Must match the main Grass pass fade so DepthNormals geometry stays consistent.
                vec3 camFlat = vec3(_WorldSpaceCameraPos.x, bladePosition.y, _WorldSpaceCameraPos.z);
                float camDist = length(bladePosition - camFlat);
                float fadeRange = max(0.0001, _GrassDistance - _GrassFadeStart);
                float fade = 1.0 - clamp((camDist - _GrassFadeStart) / fadeRange, 0.0, 1.0);
                scaleX *= fade;
                scaleY *= fade;

                vec2 terrainUV = localPosition.xz / _TerrainSize;
                vec2 hmSize2 = vec2(textureSize(_Heightmap, 0));
                float hmSize = hmSize2.x;
                float vertStep = hmSize > 1.0 ? (1.0 / (hmSize - 1.0)) : 0.001;

                vec2 baseUV = terrainUV * (hmSize2 - 1.0) / hmSize2 + 0.5 / hmSize2;
                vec2 stepUV = vec2(vertStep * (hmSize - 1.0) / hmSize, 0.0);
                vec2 stepVV = vec2(0.0, vertStep * (hmSize - 1.0) / hmSize);
                float hR = texture(_Heightmap, baseUV + stepUV).r * _TerrainHeight;
                float hL = texture(_Heightmap, baseUV - stepUV).r * _TerrainHeight;
                float hU = texture(_Heightmap, baseUV + stepVV).r * _TerrainHeight;
                float hD = texture(_Heightmap, baseUV - stepVV).r * _TerrainHeight;

                float wStep = vertStep * _TerrainSize;
                vec3 localNormal = normalize(vec3(-(hR - hL) / (wStep * 2.0), 1.0, -(hU - hD) / (wStep * 2.0)));
                vec3 terrainNormal = normalize((terrainToWorld * vec4(localNormal, 0.0)).xyz);

                vec3 up = (_AlignToNormal > 0.5) ? terrainNormal : _TerrainUp;
                vec3 localOffset;
                if (_Billboard > 0.5) {
                    vec3 cameraRight = vec3(PROWL_MATRIX_V[0][0], PROWL_MATRIX_V[1][0], PROWL_MATRIX_V[2][0]);
                    cameraRight = normalize(cameraRight - up * dot(cameraRight, up));
                    localOffset = cameraRight * vertexPosition.x * scaleX + up * vertexPosition.y * scaleY;
                } else {
                    vec3 right = normalize((terrainToWorld * vec4(normalize(instanceModelRow0.xyz), 0.0)).xyz);
                    right = normalize(right - up * dot(right, up));
                    localOffset = right * vertexPosition.x * scaleX + up * vertexPosition.y * scaleY;
                }

                float windPhase = instanceCustomData.x;
                float bendFactor = instanceCustomData.y;
                float windAmount = max(0.0, vertexPosition.y);
                float wind = sin(_Time.y * _WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * _WindStrength * bendFactor;
                localOffset.x += wind * windAmount;
                localOffset.z += wind * windAmount * 0.3;

                vec3 worldPosition = bladePosition + localOffset;
                worldPosition += up * 0.01 * scaleY; // Must match main Grass pass offset
                vNormal = terrainNormal;
                gl_Position = PROWL_MATRIX_VP * vec4(worldPosition, 1.0);
                texCoord0 = vertexTexCoord0;
#else
                gl_Position = PROWL_MATRIX_MVP * vec4(vertexPosition, 1.0);
                vNormal = vec3(0.0, 1.0, 0.0);
                texCoord0 = vertexTexCoord0;
#endif
			}
		}

		Fragment
		{
            #include "Fragment"

			layout (location = 0) out vec4 normalOut;
            in vec3 vNormal;
            in vec2 texCoord0;

            uniform sampler2D _MainTex;
            uniform float _AlphaCutoff;

			void main()
			{
                vec4 texColor = texture(_MainTex, texCoord0);
                if (texColor.a < _AlphaCutoff)
                    discard;

                normalOut = EncodeViewNormal(normalize(vNormal));
			}
		}
	ENDGLSL
}
