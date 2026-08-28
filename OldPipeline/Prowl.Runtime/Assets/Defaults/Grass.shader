Shader "Default/Grass"

Properties
{
    _MainTex ("Grass Texture", Texture2D) = "white"
    _AlphaCutoff ("Alpha Cutoff", Float) = 0.5
    _WindStrength ("Wind Strength", Float) = 0.3
    _WindSpeed ("Wind Speed", Float) = 1.5
    _Billboard ("Billboard", Float) = 1.0
    _AlignToNormal ("Align To Normal", Float) = 0.0
    _Translucency ("Translucency", Float) = 15.0
    _ScatterPower ("Scattering Power", Float) = 0.0
    _ScatterDistortion ("Scattering Distortion", Float) = 0.5
    _ScatterScale ("Scattering Scale", Float) = 1.0
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
            #include "ProwlCG"
            #include "VertexAttributes"

			out vec2 texCoord0;
			out vec4 vColor;
            out vec3 worldPos;
            out vec3 vNormal;

            uniform float _WindStrength;
            uniform float _WindSpeed;
            uniform float _Billboard;
            uniform float _AlignToNormal;
            uniform vec3 _TerrainUp;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

            // Prototype parameters. The CPU path bakes these into instance data, the procedural
            // path reads them straight from the prototype.
            uniform vec4 _ProtoSize;          // min width, max width, min height, max height
            uniform vec4 _ProtoHealthyColor;
            uniform vec4 _ProtoDryColor;
            uniform float _ProtoNoiseSpread;
            uniform float _ProtoBendFactor;

            #include "TerrainScatter"

            // Spherical wind zones, nearest first. xyz = centre, w = radius.
            #define MAX_WIND_ZONES 4
            uniform int _WindZoneCount;
            uniform vec4 _WindZoneSphere[MAX_WIND_ZONES];
            uniform vec4 _WindZoneParams[MAX_WIND_ZONES]; // strength, turbulence, pulse magnitude, pulse frequency

            // Downwash, the way air behaves under something hovering: it comes straight down through
            // the middle, spreads outward across the ground and dies at the rim. The middle is calm,
            // not the strongest point. Returns a horizontal push, matching WindZone.SampleWind.
            vec2 sampleWindZones(vec3 worldPosition)
            {
                vec2 wind = vec2(0.0);
                for (int i = 0; i < _WindZoneCount; i++)
                {
                    vec3 centre = _WindZoneSphere[i].xyz;
                    float radius = max(_WindZoneSphere[i].w, 1e-4);

                    vec2 toPoint = worldPosition.xz - centre.xz;
                    float dist = length(toPoint);
                    float height = abs(worldPosition.y - centre.y);
                    if (dist >= radius || height >= radius) continue;

                    // Height falls off on its own, so a zone parked high overhead barely stirs the ground
                    float vertical = 1.0 - smoothstep(0.0, radius, height);
                    float r = dist / radius;
                    vec2 outward = dist > 1e-4 ? toPoint / dist : vec2(0.0);

                    // Calm eye, peaking where the column spreads, then a long decay to the rim
                    float outflow = smoothstep(0.0, 0.22, r) * (1.0 - smoothstep(0.35, 1.0, r));

                    vec4 p = _WindZoneParams[i]; // strength, turbulence, pulse magnitude, pulse frequency

                    // Gust fronts sweep outward. Time enters as a phase on a wave running out
                    // from the middle, and the pattern is indexed by the outward direction, which is
                    // continuous all the way round with no seam. Nothing here multiplies the zone's
                    // own position by elapsed time: doing that made a nudge of the zone scramble the
                    // whole field, and worse the longer the game had been running.
                    float gustSpeed = 0.35 * (1.0 + p.x);
                    float ringPhase = r * 3.0 - _Time.y * gustSpeed;
                    float front = scatterNoise(outward.x * 2.5 + ringPhase, outward.y * 2.5) * 2.0 - 1.0;
                    float wobble = scatterNoise(outward.x * 2.5, outward.y * 2.5 + ringPhase * 1.3 + 17.0) * 2.0 - 1.0;

                    float gust = 1.0 + front * p.y
                               + sin((_Time.y * p.w - r * 2.0) * 6.28318531) * p.z;

                    // Once a blade is flat, pushing harder is invisible but turning the push is not,
                    // so the gusts twist the flow rather than only scaling it.
                    float twist = wobble * p.y * 0.8;
                    float cs = cos(twist), sn = sin(twist);

                    vec2 flow = vec2(outward.x * cs - outward.y * sn, outward.x * sn + outward.y * cs);

                    wind += flow * max(p.x * outflow * vertical * gust, 0.0);
                }
                return wind;
            }

			void main()
			{
#ifdef GPU_INSTANCING
                // Instance matrix is in terrain-local space; transform to world
                mat4 terrainToWorld = _TerrainLocalToWorld;

                vec3 localPosition;
                vec3 localRight;
                float scaleX;
                float scaleY;
                float windPhase;
                float bendFactor;
                vec4 bladeColor;

                ScatterBlade blade = scatterResolve(gl_InstanceID, _TerrainSize, _ProtoNoiseSpread);
                if (!blade.valid)
                {
                    // Collapsed behind the near plane, so an absent blade costs no fragment work
                    gl_Position = vec4(0.0, 0.0, -2.0, 1.0);
                    texCoord0 = vec2(0.0);
                    vColor = vec4(0.0);
                    worldPos = vec3(0.0);
                    vNormal = vec3(0.0, 1.0, 0.0);
                    return;
                }

                float sizeT = blade.noise * min(1.0, blade.density * 2.0);
                scaleX = mix(_ProtoSize.x, _ProtoSize.y, sizeT) * blade.fade;
                scaleY = mix(_ProtoSize.z, _ProtoSize.w, sizeT) * blade.fade;
                localPosition = vec3(blade.localXZ.x, scatterSampleHeight(blade.terrainUV), blade.localXZ.y);
                localRight = vec3(cos(blade.rotation), 0.0, sin(blade.rotation));
                windPhase = blade.windPhase;
                bendFactor = _ProtoBendFactor;
                bladeColor = mix(_ProtoHealthyColor, _ProtoDryColor, 1.0 - blade.noise);

                vec3 bladePosition = (terrainToWorld * vec4(localPosition, 1.0)).xyz;

                // Compute terrain surface normal from heightmap for blade orientation.
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

                vec3 quadRight;
                vec3 localOffset;
                if (_Billboard > 0.5)
                {
                    // Cylindrical billboard around terrain up axis
                    vec3 cameraRight = vec3(PROWL_MATRIX_V[0][0], PROWL_MATRIX_V[1][0], PROWL_MATRIX_V[2][0]);
                    // Project camera right perpendicular to up
                    cameraRight = normalize(cameraRight - up * dot(cameraRight, up));
                    quadRight = cameraRight;
                    localOffset = cameraRight * vertexPosition.x * scaleX
                                 + up * vertexPosition.y * scaleY;
                }
                else
                {
                    // Non-billboard: transform instance orientation from terrain-local to world
                    vec3 right = normalize((terrainToWorld * vec4(localRight, 0.0)).xyz);
                    // Re-orthogonalize right to be perpendicular to up
                    right = normalize(right - up * dot(right, up));
                    quadRight = right;
                    localOffset = right * vertexPosition.x * scaleX
                                + up * vertexPosition.y * scaleY;
                }

                // Everything pushing the blade sideways, gathered in world XZ before it bends
                float sway = sin(_Time.y * _WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * _WindStrength;
                vec2 windForce = (vec2(sway, sway * 0.3) + sampleWindZones(bladePosition)) * bendFactor;

                float windMag = length(windForce);
                vec3 bendDir = windMag > 1e-4 ? vec3(windForce.x, 0.0, windForce.y) / windMag : vec3(0.0);

                // Bend along an arc rather than dragging the tip sideways: a blade lays over in
                // strong wind, it does not stretch. Saturates at a right angle, so it can go flat
                // against the ground but never inside out.
                float bendAngle = 1.7453293 * windMag / (1.0 + windMag);
                float bladeY = max(vertexPosition.y, 0.0);
                float alongUp = bladeY;
                float alongWind = 0.0;
                if (bendAngle > 1e-4)
                {
                    alongUp = sin(bendAngle * bladeY) / bendAngle;
                    alongWind = (1.0 - cos(bendAngle * bladeY)) / bendAngle;
                }

                localOffset = quadRight * vertexPosition.x * scaleX
                            + up * (alongUp * scaleY)
                            + bendDir * (alongWind * scaleY);

                // Quad face normal: perpendicular to the plane defined by right and up
                vec3 quadNormal = normalize(cross(up, quadRight));

                vec3 worldPosition = bladePosition + localOffset;
                worldPosition += up * 0.01 * scaleY; // Minimal offset to reduce ground clipping
                worldPos = worldPosition;
                vNormal = quadNormal;
                vColor = bladeColor;

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
            #include "ProwlCG"
            #include "Lighting"

			layout (location = 0) out vec4 fragColor;

			in vec2 texCoord0;
			in vec4 vColor;
            in vec3 worldPos;
            in vec3 vNormal;

            uniform sampler2D _MainTex;
            uniform float _AlphaCutoff;
            uniform float _Translucency;
            uniform float _ScatterPower;
            uniform float _ScatterDistortion;
            uniform float _ScatterScale;

			void main()
			{
                vec4 texColor = texture(_MainTex, texCoord0);
                vec4 finalColor = texColor * vColor;

                if (finalColor.a < _AlphaCutoff)
                    discard;

                vec3 baseColor = gammaToLinearSpace(finalColor.rgb);
                // Flip normal for back faces since grass is double-sided
                vec3 normal = normalize(vNormal) * (gl_FrontFacing ? 1.0 : -1.0);

                // Unified PBR + translucency in a single light loop
                vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                vec3 lighting = CalculateForwardLighting(worldPos, normal, viewDir,
                                                         baseColor, 0.0, 0.9, 1.0,
                                                         _Translucency, _ScatterPower,
                                                         _ScatterDistortion, _ScatterScale);
                vec3 ambient = CalculateAmbient(normal) * baseColor * _AmbientStrength;

                vec3 color = ambient + lighting;
                color = ApplyFog(color, worldPos);

				fragColor = vec4(color, 1.0);
			}
		}
	ENDGLSL
}

Pass "GrassPrepass"
{
    Tags { "LightMode" = "Prepass" }
    Cull Off
    ZWrite On

	GLSLPROGRAM

		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

            out vec3 vNormal;
            out vec2 texCoord0;

            uniform float _WindStrength;
            uniform float _WindSpeed;
            uniform float _Billboard;
            uniform float _AlignToNormal;
            uniform vec3 _TerrainUp;
            uniform mat4 _TerrainWorldToLocal;
            uniform mat4 _TerrainLocalToWorld;

            // Prototype parameters. The CPU path bakes these into instance data, the procedural
            // path reads them straight from the prototype.
            uniform vec4 _ProtoSize;          // min width, max width, min height, max height
            uniform vec4 _ProtoHealthyColor;
            uniform vec4 _ProtoDryColor;
            uniform float _ProtoNoiseSpread;
            uniform float _ProtoBendFactor;

            #include "TerrainScatter"

            // Spherical wind zones, nearest first. xyz = centre, w = radius.
            #define MAX_WIND_ZONES 4
            uniform int _WindZoneCount;
            uniform vec4 _WindZoneSphere[MAX_WIND_ZONES];
            uniform vec4 _WindZoneParams[MAX_WIND_ZONES]; // strength, turbulence, pulse magnitude, pulse frequency

            // Downwash, the way air behaves under something hovering: it comes straight down through
            // the middle, spreads outward across the ground and dies at the rim. The middle is calm,
            // not the strongest point. Returns a horizontal push, matching WindZone.SampleWind.
            vec2 sampleWindZones(vec3 worldPosition)
            {
                vec2 wind = vec2(0.0);
                for (int i = 0; i < _WindZoneCount; i++)
                {
                    vec3 centre = _WindZoneSphere[i].xyz;
                    float radius = max(_WindZoneSphere[i].w, 1e-4);

                    vec2 toPoint = worldPosition.xz - centre.xz;
                    float dist = length(toPoint);
                    float height = abs(worldPosition.y - centre.y);
                    if (dist >= radius || height >= radius) continue;

                    // Height falls off on its own, so a zone parked high overhead barely stirs the ground
                    float vertical = 1.0 - smoothstep(0.0, radius, height);
                    float r = dist / radius;
                    vec2 outward = dist > 1e-4 ? toPoint / dist : vec2(0.0);

                    // Calm eye, peaking where the column spreads, then a long decay to the rim
                    float outflow = smoothstep(0.0, 0.22, r) * (1.0 - smoothstep(0.35, 1.0, r));

                    vec4 p = _WindZoneParams[i]; // strength, turbulence, pulse magnitude, pulse frequency

                    // Gust fronts sweep outward. Time enters as a phase on a wave running out
                    // from the middle, and the pattern is indexed by the outward direction, which is
                    // continuous all the way round with no seam. Nothing here multiplies the zone's
                    // own position by elapsed time: doing that made a nudge of the zone scramble the
                    // whole field, and worse the longer the game had been running.
                    float gustSpeed = 0.35 * (1.0 + p.x);
                    float ringPhase = r * 3.0 - _Time.y * gustSpeed;
                    float front = scatterNoise(outward.x * 2.5 + ringPhase, outward.y * 2.5) * 2.0 - 1.0;
                    float wobble = scatterNoise(outward.x * 2.5, outward.y * 2.5 + ringPhase * 1.3 + 17.0) * 2.0 - 1.0;

                    float gust = 1.0 + front * p.y
                               + sin((_Time.y * p.w - r * 2.0) * 6.28318531) * p.z;

                    // Once a blade is flat, pushing harder is invisible but turning the push is not,
                    // so the gusts twist the flow rather than only scaling it.
                    float twist = wobble * p.y * 0.8;
                    float cs = cos(twist), sn = sin(twist);

                    vec2 flow = vec2(outward.x * cs - outward.y * sn, outward.x * sn + outward.y * cs);

                    wind += flow * max(p.x * outflow * vertical * gust, 0.0);
                }
                return wind;
            }

			void main()
			{
#ifdef GPU_INSTANCING
                mat4 terrainToWorld = _TerrainLocalToWorld;
                vec3 localPosition;
                vec3 localRight;
                float scaleX;
                float scaleY;
                float windPhase;
                float bendFactor;
                vec4 bladeColor;

                ScatterBlade blade = scatterResolve(gl_InstanceID, _TerrainSize, _ProtoNoiseSpread);
                if (!blade.valid)
                {
                    // Collapsed behind the near plane, so an absent blade costs no fragment work
                    gl_Position = vec4(0.0, 0.0, -2.0, 1.0);
                    texCoord0 = vec2(0.0);
                    vNormal = vec3(0.0, 1.0, 0.0);
                    return;
                }

                float sizeT = blade.noise * min(1.0, blade.density * 2.0);
                scaleX = mix(_ProtoSize.x, _ProtoSize.y, sizeT) * blade.fade;
                scaleY = mix(_ProtoSize.z, _ProtoSize.w, sizeT) * blade.fade;
                localPosition = vec3(blade.localXZ.x, scatterSampleHeight(blade.terrainUV), blade.localXZ.y);
                localRight = vec3(cos(blade.rotation), 0.0, sin(blade.rotation));
                windPhase = blade.windPhase;
                bendFactor = _ProtoBendFactor;
                bladeColor = mix(_ProtoHealthyColor, _ProtoDryColor, 1.0 - blade.noise);

                vec3 bladePosition = (terrainToWorld * vec4(localPosition, 1.0)).xyz;

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
                vec3 quadRight;
                vec3 localOffset;
                if (_Billboard > 0.5) {
                    vec3 cameraRight = vec3(PROWL_MATRIX_V[0][0], PROWL_MATRIX_V[1][0], PROWL_MATRIX_V[2][0]);
                    cameraRight = normalize(cameraRight - up * dot(cameraRight, up));
                    quadRight = cameraRight;
                } else {
                    vec3 right = normalize((terrainToWorld * vec4(localRight, 0.0)).xyz);
                    right = normalize(right - up * dot(right, up));
                    quadRight = right;
                }

                // Must match the main Grass pass bend exactly, or the prepass writes depth for
                // geometry that is not where the shaded pass draws it.
                float sway = sin(_Time.y * _WindSpeed + bladePosition.x * 0.7 + bladePosition.z * 0.4 + windPhase) * _WindStrength;
                vec2 windForce = (vec2(sway, sway * 0.3) + sampleWindZones(bladePosition)) * bendFactor;

                float windMag = length(windForce);
                vec3 bendDir = windMag > 1e-4 ? vec3(windForce.x, 0.0, windForce.y) / windMag : vec3(0.0);

                float bendAngle = 1.7453293 * windMag / (1.0 + windMag);
                float bladeY = max(vertexPosition.y, 0.0);
                float alongUp = bladeY;
                float alongWind = 0.0;
                if (bendAngle > 1e-4)
                {
                    alongUp = sin(bendAngle * bladeY) / bendAngle;
                    alongWind = (1.0 - cos(bendAngle * bladeY)) / bendAngle;
                }

                localOffset = quadRight * vertexPosition.x * scaleX
                            + up * (alongUp * scaleY)
                            + bendDir * (alongWind * scaleY);

                vec3 worldPosition = bladePosition + localOffset;
                worldPosition += up * 0.01 * scaleY; // Must match main Grass pass offset
                vNormal = normalize(cross(up, quadRight));
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
            #include "ProwlCG"

			layout (location = 0) out vec4 normalOut;
			layout (location = 1) out vec4 motionRM;
            in vec3 vNormal;
            in vec2 texCoord0;

            uniform sampler2D _MainTex;
            uniform float _AlphaCutoff;

			void main()
			{
                vec4 texColor = texture(_MainTex, texCoord0);
                if (texColor.a < _AlphaCutoff)
                    discard;

                vec3 n = normalize(vNormal) * (gl_FrontFacing ? 1.0 : -1.0);
                normalOut = EncodeViewNormal(n);

                // Grass is procedurally wind-animated with no stable previous position, so motion
                // stays zero (it was absent from the motion buffer before). Diffuse: roughness 1, metallic 0.
                motionRM = vec4(0.0, 0.0, 1.0, 0.0);
			}
		}
	ENDGLSL
}
