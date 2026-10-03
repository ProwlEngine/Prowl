Shader "Default/Particle"

Properties
{
    _MainTex ("Particle Texture", Texture2D) = "white"
    _MainColor ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
}

Pass "Particle"
{
    Tags { "RenderOrder" = "Transparent" }

    Cull Off
    ZWrite Off
    // Premultiplied output serves every blend mode from one state. Alpha blending premultiplies in the
    // shader, additive writes zero alpha so the destination is kept whole.
    Blend { Src One Dst OneMinusSrcAlpha }

	GLSLPROGRAM

		Vertex
		{
            #include "ProwlCG"
            #include "VertexAttributes"

            // 0 draws particles from their instance matrix, 1 draws trail segments.
            uniform int _ParticleMode;
            // Columns, rows, frames per cycle, frame blending (0 off, 1 wraps to the first frame, 2 holds the last).
            uniform vec4 _ParticleSheet;

			out vec2 vUV0;
			out vec2 vUV1;
			out float vFrameBlend;
			out vec4 vColor;
            out vec3 vWorldPos;
            out vec3 vNormal;
            out vec4 vCustom;

            // Frame 0 is the top left tile. Textures are stored bottom row first.
            vec2 SheetUV(vec2 uv, float frame)
            {
                float columns = max(_ParticleSheet.x, 1.0);
                float rows = max(_ParticleSheet.y, 1.0);
                float column = mod(frame, columns);
                float row = floor(frame / columns);
                return vec2((column + uv.x) / columns, 1.0 - (row + 1.0 - uv.y) / rows);
            }

			void main()
			{
#ifdef GPU_INSTANCING
                if (_ParticleMode == 1)
                {
                    // Each instance is one trail segment: both ends carry position and width (rows 0, 1),
                    // tangent and texture coordinate (rows 2, 3), and color (color, custom data). Both
                    // segments meeting at a point build the same edge there, so the strip has no seams.
                    float end = clamp(vertexPosition.x + 0.5, 0.0, 1.0);
                    vec3 point = mix(instanceModelRow0.xyz, instanceModelRow1.xyz, end);
                    float width = mix(instanceModelRow0.w, instanceModelRow1.w, end);
                    vec3 tangent = normalize(mix(instanceModelRow2.xyz, instanceModelRow3.xyz, end));
                    vec3 toCamera = normalize(_WorldSpaceCameraPos.xyz - point);
                    vec3 side = cross(tangent, toCamera);
                    float sideLength = length(side);
                    side = sideLength > 1e-5 ? side / sideLength : normalize(cross(tangent, abs(tangent.y) > 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0)));

                    vec3 world = point + side * (vertexPosition.y * width);
                    gl_Position = PROWL_MATRIX_VP * vec4(world, 1.0);
                    vWorldPos = world;
                    vNormal = toCamera;
                    vUV0 = vec2(mix(instanceModelRow2.w, instanceModelRow3.w, end), vertexTexCoord0.y);
                    vUV1 = vUV0;
                    vFrameBlend = 0.0;
                    vColor = mix(instanceColor, instanceCustomData, end) * vertexColor;
                    vCustom = vec4(0.0);
                    return;
                }

                // Orientation, size, flips and pivot are all baked into the instance matrix on the CPU.
                mat4 model = mat4(instanceModelRow0, instanceModelRow1, instanceModelRow2, instanceModelRow3);
                vec4 world = model * vec4(vertexPosition, 1.0);
                gl_Position = PROWL_MATRIX_VP * world;
                vWorldPos = world.xyz;
                vNormal = mat3(model) * vertexNormal;

                // instanceCustomData: x = frame (fraction blends to the next), y = normalized age, zw = custom data
                float frame = max(instanceCustomData.x, 0.0);
                float period = max(_ParticleSheet.z, 1.0);
                float current = floor(frame);
                float start = floor(current / period) * period;
                float next = _ParticleSheet.w > 1.5
                    ? min(current + 1.0, start + period - 1.0)
                    : start + mod(current - start + 1.0, period);
                vUV0 = SheetUV(vertexTexCoord0, current);
                vUV1 = SheetUV(vertexTexCoord0, next);
                vFrameBlend = _ParticleSheet.w > 0.5 ? fract(frame) : 0.0;

                vColor = vertexColor * instanceColor;
                vCustom = instanceCustomData;
#else
                vec4 world = PROWL_MATRIX_M * vec4(vertexPosition, 1.0);
                gl_Position = PROWL_MATRIX_VP * world;
                vWorldPos = world.xyz;
                vNormal = mat3(PROWL_MATRIX_M) * vertexNormal;
                vUV0 = vertexTexCoord0;
                vUV1 = vertexTexCoord0;
                vFrameBlend = 0.0;
                vColor = vertexColor;
                vCustom = vec4(0.0);
#endif
			}
		}

		Fragment
		{
            #include "ProwlCG"
            #include "Lighting"

			layout (location = 0) out vec4 fragColor;

			in vec2 vUV0;
			in vec2 vUV1;
			in float vFrameBlend;
			in vec4 vColor;
            in vec3 vWorldPos;
            in vec3 vNormal;
            in vec4 vCustom;

			uniform sampler2D _MainTex;
			uniform vec4 _MainColor;
            uniform sampler2D _CameraDepthTexture;

            // 0 alpha, 1 additive, 2 premultiplied
            uniform int _ParticleBlend;
            uniform int _ParticleLit;
            uniform float _ParticleSoft;
            uniform vec2 _ParticleFade;

            // How much of the surface color survives the fog, 1 with no fog.
            float FogVisibility(vec3 worldPos)
            {
                if (_FogStates.x + _FogStates.y + _FogStates.z < 0.5)
                    return 1.0;

                float distance = length(worldPos - _WorldSpaceCameraPos.xyz);
                float fog = 0.0;
                fog += (distance * _FogParams.z + _FogParams.w) * _FogStates.x;
                fog += exp2(-distance * _FogParams.y) * _FogStates.y;
                fog += exp2(-distance * distance * _FogParams.x * _FogParams.x) * _FogStates.z;
                return clamp(fog, 0.0, 1.0);
            }

			void main()
			{
				vec4 texel = texture(_MainTex, vUV0);
                if (vFrameBlend > 0.0)
                    texel = mix(texel, texture(_MainTex, vUV1), vFrameBlend);

                // Texture is sRGB, the tint and particle colors are already linear.
                vec3 rgb = gammaToLinearSpace(texel.rgb) * _MainColor.rgb * vColor.rgb;
                float alpha = texel.a * _MainColor.a * vColor.a;

                float fade = 1.0;
                float depth = linearizeDepthFromProjection(gl_FragCoord.z);
                if (_ParticleSoft > 0.0)
                {
                    // Valid depth sits in [0.5, 1]. Anything lower means no depth texture is bound.
                    float sceneRaw = texture(_CameraDepthTexture, gl_FragCoord.xy / _ScreenParams.xy).r;
                    if (sceneRaw >= 0.5)
                        fade *= saturate((linearizeDepthFromProjection(sceneRaw) - depth) / _ParticleSoft);
                }
                if (_ParticleFade.y > _ParticleFade.x)
                    fade *= saturate((depth - _ParticleFade.x) / (_ParticleFade.y - _ParticleFade.x));

                if (_ParticleLit != 0)
                {
                    vec3 viewDir = normalize(_WorldSpaceCameraPos.xyz - vWorldPos);
                    vec3 normal = normalize(vNormal);
                    if (dot(normal, viewDir) < 0.0)
                        normal = -normal;
                    vec3 lighting = CalculateForwardLighting(vWorldPos, normal, viewDir, rgb, 0.0, 1.0, 1.0);
                    rgb = rgb * CalculateAmbient(normal) * _AmbientStrength + lighting;
                }

                float visibility = FogVisibility(vWorldPos);

                if (_ParticleBlend == 1)
                {
                    float strength = alpha * fade;
                    if (strength < 0.002)
                        discard;
                    // Fog fades additive light out rather than tinting it.
                    fragColor = vec4(rgb * strength * visibility, 0.0);
                }
                else if (_ParticleBlend == 2)
                {
                    float coverage = texel.a * _MainColor.a * vColor.a * fade;
                    // The texture's color already carries its own alpha, so only the tints scale it.
                    vec3 premultiplied = rgb * (_MainColor.a * vColor.a * fade);
                    // Zero alpha texels can still add light, so only drop the ones that add nothing.
                    if (coverage < 0.002 && max(premultiplied.r, max(premultiplied.g, premultiplied.b)) < 0.002)
                        discard;
                    fragColor = vec4(mix(FogColor(vWorldPos) * coverage, premultiplied, visibility), coverage);
                }
                else
                {
                    alpha *= fade;
                    if (alpha < 0.002)
                        discard;
                    fragColor = vec4(mix(FogColor(vWorldPos), rgb, visibility) * alpha, alpha);
                }
			}
		}
	ENDGLSL
}
