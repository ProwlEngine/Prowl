Shader "Default/Particle"
{
    Properties
    {
        _MainTex("Particle Texture", Texture2D) = "white"
        _MainColor("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    }

    // Particles and trail segments are instances of one shared object. Each instance reads its record from
    // ProwlParticleRecords and is turned to face the view here, so every view sees it square on.
    // Custom1.x is the draw's header record, custom1.y its view's sort order plus one (0 draws in record order).
    // Premultiplied output serves every blend mode from one state: additive writes zero alpha.
    Pass
    {
        Name "Forward"
        Tags { "LightMode" = "Forward" "RenderQueue" = "3000" }
        Cull Off
        ZWrite Off
        Blend One InverseSourceAlpha
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Vertex.glsl"

            layout(std430) readonly buffer ProwlParticleRecords { vec4 prowlParticleRecords[]; };
            layout(std430) readonly buffer ProwlParticleOrder { uint prowlParticleOrder[]; };

            out vec2 vUV0;
            out vec2 vUV1;
            out float vFrameBlend;
            out vec4 vColor;
            out vec3 vWorldPos;
            out vec3 vNormal;
            out vec4 vCustom;
            flat out vec4 vShading;

            vec4 Row(uint record, uint row) { return prowlParticleRecords[record * 6u + row]; }

            vec3 Rotate(vec4 q, vec3 v)
            {
                vec3 t = 2.0 * cross(q.xyz, v);
                return v + q.w * t + cross(q.xyz, t);
            }

            vec3 SafeNormalize(vec3 v, vec3 fallback)
            {
                float len = length(v);
                return len > 1e-6 ? v / len : fallback;
            }

            // Frame 0 is the top left tile. Textures are stored bottom row first.
            vec2 SheetUV(vec2 sheet, vec2 uv, float frame)
            {
                float columns = max(sheet.x, 1.0);
                float rows = max(sheet.y, 1.0);
                float column = mod(frame, columns);
                float row = floor(frame / columns);
                return vec2((column + uv.x) / columns, 1.0 - (row + 1.0 - uv.y) / rows);
            }

            // World height the screen covers depth in front of the camera
            float ScreenHeight(mat4 projection, float depth)
            {
                bool orthographic = projection[3][3] > 0.5;
                return 2.0 / max(abs(projection[1][1]), 1e-6) * (orthographic ? 1.0 : max(depth, 1e-4));
            }

            void main()
            {
                Graphite_ViewData view = Graphite_View();
                vec3 cameraRight = vec3(view.view[0][0], view.view[1][0], view.view[2][0]);
                vec3 cameraUp = vec3(view.view[0][1], view.view[1][1], view.view[2][1]);
                vec3 cameraForward = vec3(view.view[0][2], view.view[1][2], view.view[2][2]);
                vec3 towardCamera = -cameraForward;

                vec4 custom = Graphite_Object().custom1;
                uint header = uint(custom.x + 0.5);
                uint orderBase = uint(custom.y + 0.5);
                uint instance = Graphite_InstanceIndex();
                uint record = header + 1u + (orderBase == 0u ? instance : prowlParticleOrder[orderBase - 1u + instance]);

                // Header: mode, render mode, alignment, unused / sheet / blend, lit, fade start, fade end / min size, max size, length scale, velocity scale / pivot
                vec4 h0 = Row(header, 0u);
                vec4 sheet = Row(header, 1u);
                vec4 shading = Row(header, 2u);
                vec4 sizing = Row(header, 3u);
                vec3 pivot = Row(header, 4u).xyz;
                vShading = shading;

                vec3 vertex = vertexPosition;
                vec4 r0 = Row(record, 0u), r1 = Row(record, 1u), r2 = Row(record, 2u), r3 = Row(record, 3u), r4 = Row(record, 4u), r5 = Row(record, 5u);
                int mode = int(h0.x + 0.5);

                if (mode == 1)
                {
                    // A trail segment: both ends carry position and width, tangent and texture coordinate, and color.
                    // Segments meeting at a point build the same edge there, so the strip has no seams.
                    float end = clamp(vertex.x + 0.5, 0.0, 1.0);
                    vec3 local = mix(r0.xyz, r1.xyz, end);
                    vec3 point = Graphite_ObjectToWorld(local);
                    float width = mix(r0.w, r1.w, end);
                    vec3 tangent = normalize(mix(r2.xyz, r3.xyz, end));
                    vec3 toCamera = SafeNormalize(-point, towardCamera);
                    vec3 side = cross(tangent, toCamera);
                    float sideLength = length(side);
                    side = sideLength > 1e-5 ? side / sideLength : normalize(cross(tangent, abs(tangent.y) > 0.9 ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0)));

                    vec3 offset = side * (vertex.y * width);
                    vWorldPos = point + offset;
                    gl_Position = Graphite_RelativeToClip(vWorldPos);
                    Graphite_SetPreviousPosition(local + offset);
                    vNormal = toCamera;
                    vUV0 = vec2(mix(r2.w, r3.w, end), vertexTexCoord0.y);
                    vUV1 = vUV0;
                    vFrameBlend = 0.0;
                    vColor = mix(r4, r5, end) * vertexColor;
                    vCustom = vec4(0.0);
                    return;
                }

                // A particle: position, velocity, signed size (negative is mirrored), rotation, color, frame data
                vec3 position = Graphite_ObjectToWorld(r0.xyz);
                vec3 velocity = r1.xyz;
                vec2 size = vec2(r0.w, r1.w);
                vec4 rotation = r2;
                float sizeZ = r5.x;
                float angle = r5.y;
                int renderMode = int(h0.y + 0.5);
                int alignment = int(h0.z + 0.5);
                vec3 world;
                vec3 normal;

                if (renderMode == 4)
                {
                    vec3 local = (vertex + pivot) * vec3(size, sizeZ);
                    vec3 turned = Rotate(rotation, local);
                    vec3 turnedNormal = Rotate(rotation, vertexNormal);
                    if (alignment <= 1)
                    {
                        vec3 meshRight = SafeNormalize(cross(cameraUp, cameraForward), cameraRight);
                        mat3 look = mat3(meshRight, cross(cameraForward, meshRight), cameraForward);
                        turned = look * turned;
                        turnedNormal = look * turnedNormal;
                    }
                    world = position + turned;
                    normal = turnedNormal;
                }
                else
                {
                    float largest = max(abs(size.x), abs(size.y));
                    if (sizing.y < 1.0 || sizing.x > 0.0)
                    {
                        float screenHeight = ScreenHeight(view.projection, dot(position, cameraForward));
                        if (largest > 0.0)
                            size *= clamp(largest, sizing.x * screenHeight, max(sizing.x, sizing.y) * screenHeight) / largest;
                    }

                    float c = cos(angle), s = sin(angle);
                    float width = size.x, height = size.y;
                    vec3 right, up;
                    bool placed = false;

                    if (renderMode == 1)
                    {
                        float speed = length(velocity);
                        vec3 facing = SafeNormalize(-position, towardCamera);
                        vec3 sideways = cross(facing, velocity);
                        float sideLength = length(sideways);
                        if (speed >= 1e-5 && sideLength >= 1e-6)
                        {
                            up = velocity / speed;
                            right = sideways / sideLength;
                            normal = facing;
                            height = sign(height) * (abs(height) * sizing.z + speed * sizing.w);
                            placed = true;
                        }
                    }
                    else if (renderMode == 2)
                    {
                        normal = vec3(0.0, 1.0, 0.0);
                        right = vec3(c, 0.0, -s);
                        up = vec3(s, 0.0, c);
                        placed = true;
                    }
                    else if (renderMode == 3)
                    {
                        vec3 flatDir = vec3(-position.x, 0.0, -position.z);
                        normal = SafeNormalize(flatDir,SafeNormalize(vec3(towardCamera.x, 0.0, towardCamera.z), vec3(0.0, 0.0, 1.0)));
                        vec3 flatRight = SafeNormalize(vec3(cameraRight.x, 0.0, cameraRight.z), cross(vec3(0.0, 1.0, 0.0), normal));
                        right = flatRight * c + vec3(0.0, 1.0, 0.0) * s;
                        up = vec3(0.0, 1.0, 0.0) * c - flatRight * s;
                        placed = true;
                    }

                    if (!placed && (alignment == 2 || alignment == 3))
                    {
                        right = Rotate(rotation, vec3(1.0, 0.0, 0.0));
                        up = Rotate(rotation, vec3(0.0, 1.0, 0.0));
                        normal = Rotate(rotation, vec3(0.0, 0.0, 1.0));
                        placed = true;
                    }

                    if (!placed)
                    {
                        if (alignment == 1)
                            normal = SafeNormalize(-position, towardCamera);
                        else if (alignment == 4)
                            normal = SafeNormalize(velocity, towardCamera);
                        else
                            normal = towardCamera;

                        // The camera's screen axes bent onto the chosen plane, so textures stay upright
                        vec3 baseRight = SafeNormalize(cameraRight - normal * dot(cameraRight, normal), cross(cameraUp, normal));
                        vec3 baseUp = SafeNormalize(cross(normal, baseRight), cameraUp);
                        if (dot(baseUp, cameraUp) < 0.0)
                            baseUp = -baseUp;
                        right = baseRight * c + baseUp * s;
                        up = baseUp * c - baseRight * s;
                    }

                    vec3 center = position + right * (pivot.x * width) + up * (pivot.y * height) + normal * (pivot.z * sizeZ);
                    world = center + right * (vertex.x * width) + up * (vertex.y * height) + normal * vertex.z;
                }

                vWorldPos = world;
                vNormal = normal;
                gl_Position = Graphite_RelativeToClip(world);
                Graphite_SetPreviousPosition(r0.xyz + (world - position));

                // r4: frame (the fraction blends to the next), normalized age, custom data
                float frame = max(r4.x, 0.0);
                float period = max(sheet.z, 1.0);
                float current = floor(frame);
                float start = floor(current / period) * period;
                float next = sheet.w > 1.5 ? min(current + 1.0, start + period - 1.0) : start + mod(current - start + 1.0, period);
                vUV0 = SheetUV(sheet.xy, vertexTexCoord0, current);
                vUV1 = SheetUV(sheet.xy, vertexTexCoord0, next);
                vFrameBlend = sheet.w > 0.5 ? fract(frame) : 0.0;
                vColor = vertexColor * r3;
                vCustom = r4;
            }
        }
        Fragment
        {
            #include "Graphite/Lighting.glsl"
            #include "Prowl/Common.glsl"

            layout(location = 0) out vec4 fragColor;

            in vec2 vUV0;
            in vec2 vUV1;
            in float vFrameBlend;
            in vec4 vColor;
            in vec3 vWorldPos;
            in vec3 vNormal;
            in vec4 vCustom;
            flat in vec4 vShading;

            uniform sampler2D _MainTex;
            uniform vec4 _MainColor;

            // How much of the surface color survives the fog, 1 with no fog
            float FogVisibility(vec3 worldPos)
            {
                vec4 states = graphite_World.fogStates;
                if (states.x + states.y + states.z < 0.5)
                    return 1.0;

                vec4 params = graphite_World.fogParams;
                float dist = length(worldPos);
                float fog = 0.0;
                fog += (dist * params.z + params.w) * states.x;
                fog += exp2(-dist * params.y) * states.y;
                fog += exp2(-dist * dist * params.x * params.x) * states.z;
                return clamp(fog, 0.0, 1.0);
            }

            void main()
            {
                vec4 texel = texture(_MainTex, vUV0);
                if (vFrameBlend > 0.0)
                    texel = mix(texel, texture(_MainTex, vUV1), vFrameBlend);

                // The texture is sRGB, the tint and particle colors are already linear
                vec3 rgb = texel.rgb * _MainColor.rgb * vColor.rgb;
                float alpha = texel.a * _MainColor.a * vColor.a;

                int blend = int(vShading.x + 0.5);
                vec2 cameraFade = vShading.zw;
                float fade = 1.0;
                if (cameraFade.y > cameraFade.x)
                {
                    Graphite_ViewData view = Graphite_View();
                    vec3 forward = vec3(view.view[0][2], view.view[1][2], view.view[2][2]);
                    fade *= clamp((dot(vWorldPos, forward) - cameraFade.x) / (cameraFade.y - cameraFade.x), 0.0, 1.0);
                }

                if (vShading.y > 0.5)
                {
                    vec3 viewDir = normalize(-vWorldPos);
                    vec3 normal = normalize(vNormal);
                    if (dot(normal, viewDir) < 0.0)
                        normal = -normal;
                    vec3 lighting = CalculateForwardLighting(vWorldPos, normal, viewDir, rgb, 0.0, 1.0, 1.0);
                    rgb = rgb * CalculateAmbient(normal) + lighting;
                }

                float visibility = FogVisibility(vWorldPos);

                if (blend == 1)
                {
                    float strength = alpha * fade;
                    if (strength < 0.002)
                        discard;
                    // Fog fades additive light out rather than tinting it
                    fragColor = vec4(rgb * strength * visibility, 0.0);
                }
                else if (blend == 2)
                {
                    float coverage = alpha * fade;
                    // The texture's color already carries its own alpha, so only the tints scale it
                    vec3 premultiplied = rgb * (_MainColor.a * vColor.a * fade);
                    // Zero alpha texels can still add light, so only drop the ones that add nothing
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
}
