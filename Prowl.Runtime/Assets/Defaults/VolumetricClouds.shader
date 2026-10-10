Shader "Hidden/Volumetric Clouds"

Properties
{
}

// How much cloud there is at each point of the ground plane, over a square around the camera
Pass "Coverage"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
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
        #include "Clouds"

        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        void main()
        {
            float coverage = CloudCoverageNoise(_CloudCoverageRect.xy + TexCoords * _CloudCoverageRect.w);
            OutputColor = vec4(coverage, coverage, coverage, 1.0);
        }
    }

    ENDGLSL
}

// Scene depth at the clouds' resolution, as distance from the camera. Neighbouring pixels alternate between the
// nearest and furthest of what they cover, so both sides of an edge survive for the depth aware upsample
Pass "Depth"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        void main()
        {
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        #include "ProwlCG"

        layout(location = 0) out vec4 OutputColor;

        uniform sampler2D _CameraDepthTexture;
        uniform vec2 _CloudFullResolution;
        uniform vec2 _CloudLowResolution;
        uniform int _CloudDownsample;
        uniform int _CloudDepthMode;     // 1 takes the one scene pixel under each jittered cloud sample
        uniform vec2 _CloudJitter;       // this frame's sample offset, in pixels of the clouds' target

        float DistanceAt(ivec2 pixel)
        {
            float depth = texelFetch(_CameraDepthTexture, pixel, 0).r;
            if (depth >= 0.99999) return 1e8;
            vec2 uv = (vec2(pixel) + 0.5) / _CloudFullResolution;
            vec4 world = PROWL_MATRIX_I_VP * vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            return distance(world.xyz / world.w, _WorldSpaceCameraPos.xyz);
        }

        void main()
        {
            ivec2 last = ivec2(_CloudFullResolution) - 1;
            if (_CloudDepthMode == 1)
            {
                vec2 at = (gl_FragCoord.xy + _CloudJitter) / _CloudLowResolution * _CloudFullResolution;
                OutputColor = vec4(DistanceAt(min(ivec2(at), last)), 0.0, 0.0, 1.0);
                return;
            }

            ivec2 low = ivec2(gl_FragCoord.xy);
            float spread = float(max(_CloudDownsample - 1, 0));
            vec2 base = gl_FragCoord.xy / _CloudLowResolution * _CloudFullResolution - spread * 0.5;
            bool takeFar = ((low.x + low.y) & 1) == 0;

            float pick = takeFar ? 0.0 : 1e9;
            for (int i = 0; i < 4; i++)
            {
                vec2 offset = vec2(i & 1, i >> 1) * spread;
                float d = DistanceAt(clamp(ivec2(base + offset), ivec2(0), last));
                pick = takeFar ? max(pick, d) : min(pick, d);
            }
            OutputColor = vec4(pick, 0.0, 0.0, 1.0);
        }
    }

    ENDGLSL
}

// A flat cloud layer, a sheet curved with the planet at one altitude
Pass "Layer"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend { Src One Dst OneMinusSrcAlpha }
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        void main()
        {
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        #include "ProwlCG"
        #include "Lighting"
        #include "Clouds"

        layout(location = 0) out vec4 OutputColor;
        layout(location = 1) out vec4 OutputDistance;

        uniform sampler2D _CloudSceneDepth;
        uniform vec2 _CloudLowResolution;
        uniform vec2 _CloudJitter;
        uniform vec4 _CloudLighting;  // extinction per meter, sun, ambient, silver lining
        uniform vec4 _CloudAlbedo;
        uniform float _CloudHorizonFade;

        uniform vec4 _LayerShape;     // altitude, coverage, opacity, 1 / scale
        uniform vec4 _LayerWind;      // offset x, offset z, wind direction x, z
        uniform int _LayerStyle;      // 0 cirrus, 1 stratus
        uniform vec4 _LayerTint;
        uniform int _LayerUseTexture;
        uniform sampler2D _LayerTexture;

        // Wispy patches: broad noise decides where cirrus is, and fibres drawn out along the wind and curved by a slow
        // bend give it texture inside
        float Cirrus(vec2 q)
        {
            vec2 along = _LayerWind.zw;
            vec2 across = vec2(-along.y, along.x);
            vec2 s = vec2(dot(q, along), dot(q, across));
            s.y += (texture(_CloudNoise2D, q * 0.08).r - 0.5) * 0.8;

            float fibres = 0.0, amplitude = 0.5, total = 0.0;
            vec2 frequency = vec2(0.45, 1.5);
            for (int o = 0; o < 3; o++)
            {
                fibres += texture(_CloudNoise2D, s * frequency + vec2(0.37, 0.71) * float(o)).b * amplitude;
                total += amplitude;
                amplitude *= 0.55;
                frequency *= 2.0;
            }
            fibres = clamp((fibres / total - 0.42) * 2.5, 0.0, 1.0);

            float threshold = 1.0 - _LayerShape.y;
            float patches = smoothstep(threshold - 0.1, threshold + 0.25, texture(_CloudNoise2D, q * 0.3).r);
            return patches * fibres;
        }

        // Soft puffs drawn out along the wind, a few times longer than wide, with fine streaks running through them
        float Stratus(vec2 q)
        {
            vec2 along = _LayerWind.zw;
            vec2 s = vec2(dot(q, along), dot(q, vec2(-along.y, along.x)));
            vec2 bend = vec2(texture(_CloudNoise2D, s * 0.05).r, texture(_CloudNoise2D, s * 0.05 + vec2(0.31, 0.67)).r) - 0.5;
            s += bend * vec2(0.6, 1.2);

            float threshold = 1.0 - _LayerShape.y;
            float lumps = texture(_CloudNoise2D, s * vec2(0.7, 5.0)).r * 0.55 + texture(_CloudNoise2D, s * vec2(1.1, 7.0) + vec2(0.5, 0.25)).g * 0.45;
            float puffs = smoothstep(threshold - 0.12, threshold + 0.3, lumps);

            float streaks = texture(_CloudNoise2D, s * vec2(0.14, 2.8)).b * 0.6 + texture(_CloudNoise2D, s * vec2(0.28, 6.3) + vec2(0.13, 0.57)).b * 0.4;
            return clamp(puffs * mix(0.0, 1.6, streaks), 0.0, 1.0);
        }

        float LayerDensity(vec2 p)
        {
            vec2 q = (p - _LayerWind.xy) * _LayerShape.w;
            if (_LayerUseTexture != 0)
            {
                float threshold = 1.0 - _LayerShape.y;
                return smoothstep(threshold - 0.05, threshold + 0.25, texture(_LayerTexture, q).r);
            }
            return _LayerStyle == 0 ? Cirrus(q) : Stratus(q);
        }

        void main()
        {
            vec2 uv = (gl_FragCoord.xy + _CloudJitter) / _CloudLowResolution;
            vec3 cam = _WorldSpaceCameraPos.xyz;
            vec4 farPoint = PROWL_MATRIX_I_VP * vec4(uv * 2.0 - 1.0, 1.0, 1.0);
            vec3 dir = normalize(farPoint.xyz / farPoint.w - cam);

            float t = ShellDistance(dir, cam.y, _LayerShape.x);
            float sceneDist = texelFetch(_CloudSceneDepth, ivec2(gl_FragCoord.xy), 0).r;
            if (t <= 0.0 || t > sceneDist) discard;

            vec3 p = cam + dir * t;
            float density = LayerDensity(p.xz);
            float alpha = density * _LayerShape.z;
            if (alpha < 0.002) discard;
            if (_CloudMomentPass == 1)
            {
                CloudMoments(alpha, t, OutputColor, OutputDistance);
                return;
            }

            vec3 toSun = _CloudToSun;
            vec3 sun = _DirectionalLightColor * _DirectionalLightIntensity * float(_DirectionalLightEnabled) * _CloudLighting.y;
            sun *= smoothstep(-0.1, 0.05, toSun.y);

            // A thin sheet passes most light on, brightest looking toward the sun
            float cosTheta = dot(dir, toSun);
            float phase = mix(1.0, HenyeyGreenstein(cosTheta, 0.75) * 4.0 * 3.14159265, _CloudLighting.w * 0.5);
            vec3 ambient = (ReflectionProbesReady() ? SampleProbeLayer(0.0, vec3(0.0, 1.0, 0.0), 1.0) : CalculateAmbient(vec3(0.0, 1.0, 0.0))) * _CloudLighting.z;
            vec3 color = _CloudAlbedo.rgb * _LayerTint.rgb * (sun * phase * (1.0 - 0.4 * density) + ambient);

            if (ReflectionProbesReady())
                color = mix(SampleProbeLayer(0.0, dir, 0.0), color, exp(-t * _CloudHorizonFade));

            CloudOutput(color, alpha, t, ivec2(gl_FragCoord.xy), OutputColor, OutputDistance);
        }
    }

    ENDGLSL
}

// The volumetric clouds. Each particle opens into a camera facing disc around its cell of the grid, samples the cloud
// where its pixels are and reads how much sunlight reaches there
Pass "Particles"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend { Src One Dst OneMinusSrcAlpha }
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        #include "ProwlCG"
        #include "VertexAttributes"
        #include "Clouds"

        uniform vec4 _CloudGrid;         // cell span, particle radius, radius it fades out by, radius it fades in from
        uniform vec2 _CloudLattice;      // the grid centre's cell, counted from where the wind has carried the clouds
        uniform float _CloudFlatten;
        uniform vec3 _CloudCameraForward;
        uniform vec3 _CloudCameraRight;
        uniform float _CloudHorizonFade;
        uniform float _CloudAmbientSaturation;
        uniform vec2 _CloudJitter;       // this frame's sample offset, in pixels of the clouds' target
        uniform vec2 _CloudLowResolution;

        // The sky probe, read here rather than through the lighting include, which only builds for fragments
        uniform int _ReflectionProbesReady;
        uniform samplerCubeArray _ReflectionProbes;
        uniform float _ReflectionProbeMips;
        uniform vec2 _AmbientMode;
        uniform vec4 _AmbientColor;
        uniform vec4 _AmbientSkyColor;

        out vec3 vWorld;
        out vec3 vCloudPos;
        out vec2 vCorner;
        out float vFade;
        out float vRadius;
        out vec4 vPuff;
        out vec3 vOffset;
        out vec3 vFacing;
        out vec3 vSkyLight;
        out vec4 vHorizon;

        vec3 Sky(vec3 dir, float roughness)
        {
            return textureLod(_ReflectionProbes, vec4(dir, 0.0), roughness * (_ReflectionProbeMips - 1.0)).rgb;
        }

        vec2 Hash(vec2 lattice)
        {
            uint h = uint(int(lattice.x)) * 0x8DA6B343u ^ uint(int(lattice.y)) * 0xD8163841u;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return vec2(float(h & 0xFFFFu), float(h >> 16)) / 65535.0;
        }

        void main()
        {
            vec3 cam = _WorldSpaceCameraPos.xyz;
            vec3 centre = (PROWL_MATRIX_M * vec4(vertexPosition, 1.0)).xyz;

            // The lattice moves with the wind, so a particle's cell, and its random offset, stay its own. Counted in
            // whole cells from the CPU, since working it out from positions lands on rounding boundaries
            float cellSpan = _CloudGrid.x;
            vec2 lattice = _CloudLattice + floor(vertexPosition.xz);
            vec2 jitter = Hash(lattice) - 0.5;
            vec2 lift = Hash(lattice + vec2(7919.0, 104729.0));
            centre.xz += jitter * cellSpan;
            centre.y += (lift.x - 0.5) * _CloudShape.y * 0.4;

            float coverage = CloudCoverageAt(centre.xz);

            float reach = length(centre.xz - cam.xz);
            float fade = 1.0 - smoothstep(_CloudGrid.z * 0.5, _CloudGrid.z, reach);
            if (_CloudGrid.w > 0.0) fade *= smoothstep(_CloudGrid.w * 0.5, _CloudGrid.w, reach);
            fade *= smoothstep(0.03, 0.12, coverage);
            // The maps are drawn around the main camera, so another camera far from it fades out before their edge
            vec2 mapUV = CloudMapUV(centre.xz);
            vec2 edge = min(mapUV, 1.0 - mapUV);
            fade *= smoothstep(0.0, 0.02, min(edge.x, edge.y));
            // Sizes vary, so the edges of neighbouring particles never line up into rows
            float radius = fade > 0.001 ? _CloudGrid.y * mix(0.7, 1.3, lift.y) : 0.0;

            // Face the camera without rolling, turning to the view direction up close so flying through stays calm
            vec3 toCentre = centre - cam;
            float centreDistance = length(toCentre);
            vec3 forward = toCentre / max(centreDistance, 1e-3);
            forward = normalize(mix(forward, _CloudCameraForward, clamp(1.0 - centreDistance / (_CloudGrid.y * 2.0), 0.0, 1.0)));
            vec3 right = cross(vec3(0.0, 1.0, 0.0), forward);
            float rightLength = length(right);
            right = rightLength > 1e-3 ? right / rightLength : _CloudCameraRight;
            vec3 up = cross(forward, right);

            // Large far particles squash into the layer when seen from the side
            float flatten = mix(_CloudFlatten, 1.0, abs(forward.y));

            vec2 corner = vertexTexCoord0 * 2.0 - 1.0;
            vec3 offset = right * (corner.x * radius) + up * (corner.y * radius * flatten);
            vec3 world = centre + offset;
            vCloudPos = world;
            vOffset = offset / max(_CloudGrid.y, 1e-3);
            vFacing = forward;

            // The planet curves away under the clouds toward the horizon
            float ground = length(world.xz - cam.xz);
            world.y -= ground * ground / (2.0 * _CloudShape.w);

            vWorld = world;
            vCorner = corner;
            vFade = fade;
            vRadius = radius;

            // Sky light and the sky behind the cloud change little across a particle, so each corner reads them once
            // rather than every pixel. Sky light comes from above, partly greyed so the clouds do not turn blue
            bool probes = _ReflectionProbesReady != 0;
            vec3 sky = probes ? Sky(vec3(0.0, 1.0, 0.0), 1.0) : _AmbientColor.rgb * _AmbientMode.x + _AmbientSkyColor.rgb * _AmbientMode.y;
            vSkyLight = mix(vec3(dot(sky, vec3(0.2126, 0.7152, 0.0722))), sky, _CloudAmbientSaturation);

            // Each particle reads the puff texture at its own spot and turn, so neighbours never repeat it
            vec2 spot = Hash(lattice + vec2(-3301.0, 6211.0));
            float turn = spot.y * 6.2831853;
            vPuff = vec4(spot.x * 7.31, spot.y * 3.77, cos(turn), sin(turn));

            // Far away clouds take on the sky behind them
            vec3 toCorner = world - cam;
            float cornerDistance = length(toCorner);
            vHorizon = probes
                ? vec4(Sky(toCorner / max(cornerDistance, 1e-3), 0.0), exp(-cornerDistance * _CloudHorizonFade))
                : vec4(0.0, 0.0, 0.0, 1.0);

            // Nothing is depth tested here, so the far plane must not cut the clouds off
            gl_Position = PROWL_MATRIX_VP * vec4(world, 1.0);
            gl_Position.z = 0.0;

            // Shifted so each pixel of the target samples this frame's offset within it
            gl_Position.xy -= 2.0 * _CloudJitter / _CloudLowResolution * gl_Position.w;
        }
    }

    Fragment
    {
        #include "ProwlCG"
        #include "Lighting"
        #include "Clouds"

        layout(location = 0) out vec4 OutputColor;
        layout(location = 1) out vec4 OutputDistance;

        in vec3 vWorld;
        in vec3 vCloudPos;
        in vec2 vCorner;
        in float vFade;
        in float vRadius;
        in vec4 vPuff;
        in vec3 vOffset;
        in vec3 vFacing;
        in vec3 vSkyLight;
        in vec4 vHorizon;

        uniform sampler2D _CloudSceneDepth;
        uniform vec4 _CloudLighting;  // extinction per meter, sun, ambient, silver lining
        uniform vec4 _CloudAlbedo;
        uniform int _CloudLightSamples; // 0 reads the sun map, otherwise this many steps are marched toward the sun
        uniform float _CloudPixelAngle;  // how wide a pixel of the clouds' target is, in radians
        uniform vec2 _CloudPuff;         // strength, repeats across a particle

        float PuffAt(vec2 uv)
        {
            mat2 turn = mat2(vPuff.z, vPuff.w, -vPuff.w, vPuff.z);
            return texture(_CloudNoise2D, turn * uv * (_CloudPuff.y * 0.25) + vPuff.xy).g;
        }

        // Billowy puffs across a particle, detail at screen resolution for a texture read or two. They are read from the
        // particle's world planes the disc faces most, so they stay put as the disc turns toward a moving camera rather
        // than sliding over the cloud
        float Puff(vec3 offset, vec3 facing)
        {
            vec3 w = facing * facing;
            w *= w;
            w /= w.x + w.y + w.z;
            float puff = 0.0;
            if (w.x > 0.01) puff += PuffAt(offset.zy) * w.x;
            if (w.y > 0.01) puff += PuffAt(offset.xz) * w.y;
            if (w.z > 0.01) puff += PuffAt(offset.xy) * w.z;
            return puff;
        }

        void main()
        {
            vec3 cam = _WorldSpaceCameraPos.xyz;
            vec3 viewDir = normalize(vWorld - cam);
            float r = length(vCorner);
            if (r > 1.0) discard;
            float viewDist = distance(vWorld, cam);

            // The middle of the disc samples a little nearer the camera, which gives each particle some depth
            float r2 = r * r;
            vec3 p = vCloudPos - viewDir * ((1.0 - r2) * (1.0 - r2) * vRadius * 0.25);
            vec3 puffOffset = vOffset;
            vec3 puffFacing = vFacing;
            float sceneDist = texelFetch(_CloudSceneDepth, ivec2(gl_FragCoord.xy), 0).r;
            if (viewDist > sceneDist) discard;

            // Every term over the disc eases to flat at its middle and its rim. A linear falloff peaks in the middle and
            // creases at the rim, and the creases of many overlapping discs add up to visible lines
            float bulge = (1.0 - r * r) * (1.0 - r * r);

            float height;
            float shape = CloudShape(p, height);
            if (shape < 0.003) discard;
            // Detail noise smaller than a pixel would shimmer as the clouds drift, so far away a blurrier mip is used
            float texelsPerPixel = viewDist * _CloudPixelAngle * _CloudPattern.z * _CloudErosionSize;
            float lod = max(log2(texelsPerPixel * 2.0), 0.0);
            float density = Erode(p, shape, true, lod);
            if (_CloudPuff.x > 0.0)
            {
                float cut = Puff(puffOffset, puffFacing) * _CloudPuff.x;
                density = clamp((density - cut) / max(1.0 - cut, 1e-3), 0.0, 1.0);
            }
            if (density < 0.003) discard;

            float alpha = smoothstep(0.02, 0.45, density) * vFade * bulge;
            alpha *= clamp((sceneDist - viewDist) / 50.0, 0.0, 1.0);
            alpha *= clamp(viewDist / (_CloudShape.y * 0.5), 0.0, 1.0);
            if (_CloudMomentPass == 1)
            {
                CloudMoments(alpha, viewDist, OutputColor, OutputDistance);
                return;
            }

            // How much cloud the sunlight crossed to get here, read from the sun map or marched for every pixel
            vec3 toSun = _CloudToSun;
            float optical = _CloudLightSamples > 0 ? MarchToSun(p, toSun, _CloudLightSamples, lod + 1.0) : SunDepthAt(p);

            float tau = optical * _CloudLighting.x;
            float cosTheta = dot(viewDir, toSun);

            float transmitted = CloudScattering(tau, cosTheta, _CloudLighting.w);

            // Edges facing away from the sun hold little light, their dark rims
            float powder = mix(1.0 - exp(-density * 6.0), 1.0, cosTheta * 0.5 + 0.5);

            vec3 sun = _DirectionalLightColor * _DirectionalLightIntensity * float(_DirectionalLightEnabled) * _CloudLighting.y;
            sun *= smoothstep(-0.1, 0.05, toSun.y);

            // Sky light dims deep inside the cloud and toward its base
            vec3 ambient = vSkyLight * _CloudLighting.z * exp(-density * 0.6) * mix(0.55, 1.0, height);
            vec3 color = _CloudAlbedo.rgb * (sun * transmitted * powder + ambient);
            color = mix(vHorizon.rgb, color, vHorizon.a);

            CloudOutput(color, alpha, viewDist, ivec2(gl_FragCoord.xy), OutputColor, OutputDistance);
        }
    }

    ENDGLSL
}

// The clouds scaled up over the scene, each pixel weighting the nearby cloud pixels whose depth matches its own
Pass "Composite"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend { Src One Dst OneMinusSrcAlpha }
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
        #include "ProwlCG"

        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform sampler2D _CloudTex;
        uniform sampler2D _CloudTexDistance; // kilometers times coverage
        uniform sampler2D _CloudSceneDepth;
        uniform sampler2D _CameraDepthTexture;
        uniform vec2 _CloudLowResolution;

        void main()
        {
            float depth = texture(_CameraDepthTexture, TexCoords).r;
            float own = 1e8;
            if (depth < 0.99999)
            {
                vec4 world = PROWL_MATRIX_I_VP * vec4(TexCoords * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
                own = distance(world.xyz / world.w, _WorldSpaceCameraPos.xyz);
            }

            vec2 position = TexCoords * _CloudLowResolution - 0.5;
            ivec2 first = ivec2(floor(position));
            vec2 f = position - vec2(first);
            ivec2 last = ivec2(_CloudLowResolution) - 1;

            vec4 sum = vec4(0.0);
            float sumDistance = 0.0, total = 0.0;
            for (int i = 0; i < 4; i++)
            {
                ivec2 offset = ivec2(i & 1, i >> 1);
                ivec2 pixel = clamp(first + offset, ivec2(0), last);
                float bilinear = (offset.x == 1 ? f.x : 1.0 - f.x) * (offset.y == 1 ? f.y : 1.0 - f.y);
                float difference = abs(texelFetch(_CloudSceneDepth, pixel, 0).r - own) / max(own, 1.0);
                float weight = (bilinear + 1e-3) / (difference + 1e-3);
                sum += texelFetch(_CloudTex, pixel, 0) * weight;
                sumDistance += texelFetch(_CloudTexDistance, pixel, 0).r * weight;
                total += weight;
            }

            // Wherever geometry is nearer than the cloud, the cloud fades out over the same distance the particles
            // fade into geometry
            vec4 color = sum / total;
            float cloud = sumDistance / total / max(color.a, 1e-4) * 1000.0;
            float visible = color.a > 1e-3 ? clamp((own - cloud) / 50.0, 0.0, 1.0) : 1.0;
            OutputColor = color * visible;
        }
    }

    ENDGLSL
}

// How much cloud the sunlight crosses to reach four heights through the layer, over the coverage map's square, so the
// particles read it once rather than marching toward the sun for every pixel
Pass "SunDepth"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
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
        #include "Clouds"

        layout(location = 0) out vec4 OutputColor;

        in vec2 TexCoords;

        uniform float _CloudSunMapLod;

        const int SunMapSteps = 8;

        void main()
        {
            vec2 xz = _CloudCoverageRect.xy + TexCoords * _CloudCoverageRect.w;
            vec3 toSun = _CloudToSun;
            vec4 depths;
            for (int i = 0; i < 4; i++)
            {
                float height = _CloudShape.x + (float(i) + 0.5) * 0.25 * _CloudShape.y;
                depths[i] = MarchToSun(vec3(xz.x, height, xz.y), toSun, SunMapSteps, _CloudSunMapLod);
            }
            OutputColor = depths;
        }
    }

    ENDGLSL
}

// Builds the clouds at their full target resolution from a fraction of the pixels a frame. Each frame's samples land on
// one pixel of every 2x2 block, in turn, and the other pixels carry last frame's result, reprojected by how far away
// the cloud is and how far the wind carried it, and held within what the clouds around it look like now
Pass "Temporal"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        void main()
        {
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        #include "ProwlCG"

        layout(location = 0) out vec4 OutputColor;
        layout(location = 1) out vec4 OutputDistance;

        uniform sampler2D _CloudCurrent;
        uniform sampler2D _CloudCurrentDistance;  // kilometers times coverage, so dividing gives the average
        uniform sampler2D _CloudTargetDepth;      // scene distance at this pass's own resolution
        uniform sampler2D _CloudPreviousTargetDepth; // the same, last frame
        uniform sampler2D _CloudLowDepth;         // scene distance under each of this frame's samples
        uniform sampler2D _CloudHistory;
        uniform sampler2D _CloudHistoryDistance;
        uniform vec2 _CloudLowResolution;
        uniform vec2 _CloudHistoryResolution;
        uniform vec2 _CloudJitterCell;   // which pixel of each block this frame's samples land on
        uniform int _CloudBlock;         // pixels along each side of a block
        uniform float _CloudHistoryValid;
        uniform vec2 _CloudWindStep;     // how far the wind carried the clouds since the history was drawn
        uniform mat4 _CloudPreviousViewProjection;
        // The view projections without the anti aliasing jitter, so a still camera never reads as moving
        uniform mat4 _CloudStillViewProjection, _CloudPreviousStillViewProjection;
        // The world points under the screen's corners on two planes across the view, worked out in double precision
        uniform vec3 _CloudNearOrigin, _CloudNearRight, _CloudNearUp;
        uniform vec3 _CloudFarOrigin, _CloudFarRight, _CloudFarUp;

        // This frame's samples interpolated to any pixel. Each sample sits at the centre of its block's jittered cell, and
        // counts by how well the scene behind it matches the pixel's, so samples on geometry do not leak into sky beside
        // it and the other way round
        void CurrentAt(vec2 pixelCentre, ivec2 last, float sceneDistance, out vec4 color, out float dist)
        {
            vec2 at = (pixelCentre - _CloudJitterCell - 0.5) / float(_CloudBlock);
            ivec2 first = ivec2(floor(at));
            vec2 f = at - vec2(first);
            color = vec4(0.0);
            dist = 0.0;
            float total = 0.0;
            for (int i = 0; i < 4; i++)
            {
                ivec2 offset = ivec2(i & 1, i >> 1);
                ivec2 tap = clamp(first + offset, ivec2(0), last);
                float bilinear = (offset.x == 1 ? f.x : 1.0 - f.x) * (offset.y == 1 ? f.y : 1.0 - f.y);
                float difference = abs(texelFetch(_CloudLowDepth, tap, 0).r - sceneDistance) / max(sceneDistance, 1.0);
                float weight = (bilinear + 1e-3) / (difference + 1e-3);
                color += texelFetch(_CloudCurrent, tap, 0) * weight;
                dist += texelFetch(_CloudCurrentDistance, tap, 0).r * weight;
                total += weight;
            }
            color /= total;
            dist /= total;
        }

        // History read with a sharpening cubic from five bilinear reads, so reprojecting it every frame does not blur it
        vec4 HistoryAt(vec2 uv)
        {
            vec2 position = uv * _CloudHistoryResolution;
            vec2 centre = floor(position - 0.5) + 0.5;
            vec2 f = position - centre;
            vec2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
            vec2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
            vec2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
            vec2 w3 = f * f * (-0.5 + 0.5 * f);
            vec2 w12 = w1 + w2;
            vec2 middle = (centre + w2 / w12) / _CloudHistoryResolution;
            vec2 before = (centre - 1.0) / _CloudHistoryResolution;
            vec2 after = (centre + 2.0) / _CloudHistoryResolution;

            vec4 sum = texture(_CloudHistory, vec2(middle.x, before.y)) * (w12.x * w0.y)
                     + texture(_CloudHistory, vec2(before.x, middle.y)) * (w0.x * w12.y)
                     + texture(_CloudHistory, middle) * (w12.x * w12.y)
                     + texture(_CloudHistory, vec2(after.x, middle.y)) * (w3.x * w12.y)
                     + texture(_CloudHistory, vec2(middle.x, after.y)) * (w12.x * w3.y);
            float total = w12.x * w0.y + w0.x * w12.y + w12.x * w12.y + w3.x * w12.y + w12.x * w3.y;
            return max(sum / total, vec4(0.0));
        }

        vec2 Project(mat4 viewProjection, vec3 world)
        {
            vec4 clip = viewProjection * vec4(world, 1.0);
            return clip.xy / clip.w * 0.5 + 0.5;
        }

        // Nothing that is not a number survives into the history, where it would stay until the next reset
        void Output(vec4 color, float dist)
        {
            bool bad = any(isnan(color)) || any(isinf(color)) || isnan(dist) || isinf(dist);
            OutputColor = bad ? vec4(0.0) : max(color, vec4(0.0));
            OutputDistance = vec4(bad ? 0.0 : max(dist, 0.0), 0.0, 0.0, 1.0);
        }

        void main()
        {
            ivec2 pixel = ivec2(gl_FragCoord.xy);
            ivec2 last = ivec2(_CloudLowResolution) - 1;
            ivec2 historyLast = ivec2(_CloudHistoryResolution) - 1;
            ivec2 low = min(pixel / _CloudBlock, last);
            vec4 current = texelFetch(_CloudCurrent, low, 0);
            float currentDist = texelFetch(_CloudCurrentDistance, low, 0).r;
            float sceneDistance = texelFetch(_CloudTargetDepth, pixel, 0).r;
            vec4 smoothed;
            float smoothedDist;
            CurrentAt(gl_FragCoord.xy, last, sceneDistance, smoothed, smoothedDist);

            // The pixel this frame's samples landed on takes its own sample, the others this frame's samples around them
            bool fresh = all(equal(pixel - low * _CloudBlock, ivec2(_CloudJitterCell)));
            vec4 own = fresh ? current : smoothed;
            float ownDist = fresh ? currentDist : smoothedDist;
            if (_CloudHistoryValid < 0.5)
            {
                Output(own, ownDist);
                return;
            }

            // How far away this pixel's cloud is, from this frame's samples where they hold cloud, else from what the
            // history held here. Clear sky is as if infinitely far
            float guideAlpha = own.a, guideDist = ownDist;
            if (guideAlpha <= 0.01)
            {
                guideAlpha = texelFetch(_CloudHistory, pixel, 0).a;
                guideDist = texelFetch(_CloudHistoryDistance, pixel, 0).r;
            }
            bool cloudy = guideAlpha > 0.01;
            float distance = cloudy ? guideDist / guideAlpha * 1000.0 : 1e6;

            // Where this pixel's cloud was last frame: found from how far away it is, then moved back against the wind.
            // The pixel's ray runs between its points on two planes across the view. A float inverse of the view
            // projection lands a tenth of a pixel off, which history going through it every frame would turn into drift
            vec2 uv = gl_FragCoord.xy / _CloudHistoryResolution;
            vec3 start = _CloudNearOrigin + _CloudNearRight * uv.x + _CloudNearUp * uv.y;
            vec3 end = _CloudFarOrigin + _CloudFarRight * uv.x + _CloudFarUp * uv.y;
            vec3 dir = normalize(end - start);
            vec3 now = start + dir * distance;
            vec3 world = now;
            if (cloudy) world.xz -= _CloudWindStep;
            vec4 previous = _CloudPreviousViewProjection * vec4(world, 1.0);
            vec2 previousUV = previous.xy / previous.w * 0.5 + 0.5;
            if (previous.w <= 0.0 || any(lessThan(previousUV, vec2(0.0))) || any(greaterThanEqual(previousUV, vec2(1.0))))
            {
                Output(own, ownDist);
                return;
            }

            // Where geometry hid this pixel's cloud last frame there is no history for it, only this frame's samples
            ivec2 previousPixel = clamp(ivec2(previousUV * _CloudHistoryResolution), ivec2(0), historyLast);
            if (texelFetch(_CloudPreviousTargetDepth, previousPixel, 0).r < distance - 50.0)
            {
                Output(own, ownDist);
                return;
            }

            vec4 history = HistoryAt(previousUV);
            float historyDist = texture(_CloudHistoryDistance, previousUV).r;

            // How far this pixel's cloud moved on screen since last frame. The guards against stale history below only
            // act on movement, since both depend on which pixel of the block was sampled, and would make a still sky
            // shimmer through the four frame cycle
            vec2 moved = Project(_CloudPreviousStillViewProjection, world) - Project(_CloudStillViewProjection, now);
            float motion = clamp(length(moved * _CloudHistoryResolution) * 2.0, 0.0, 1.0);

            // Held within what the clouds around it look like this frame, so history cannot cling to cloud that moved.
            // Centred on this frame's sample nearest the pixel, so the bounds do not change in steps of whole blocks
            ivec2 nearest = clamp(ivec2(floor((gl_FragCoord.xy - _CloudJitterCell - 0.5) / float(_CloudBlock) + 0.5)), ivec2(0), last);
            vec4 lowest = smoothed, highest = smoothed;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    vec4 around = texelFetch(_CloudCurrent, clamp(nearest + ivec2(x, y), ivec2(0), last), 0);
                    lowest = min(lowest, around);
                    highest = max(highest, around);
                }
            history = mix(history, clamp(history, lowest, highest), motion);

            // A pixel sampled this frame takes a large share of its sample. While moving, every other pixel takes a
            // little of this frame's samples around it, so neighbours move together rather than each catching up on
            // its own frame
            float amount = fresh ? 0.5 : 0.1 * motion;
            Output(mix(history, own, amount), mix(historyDist, ownDist, amount));
        }
    }

    ENDGLSL
}

// The moment sums turned back into the blended colour and distance the later passes read. The lit samples are
// normalised by their total weight, so errors in the rebuilt transmittance shade rather than brighten or darken
Pass "Resolve"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    ZTest Off
    ZWrite Off
    Cull Off

    GLSLPROGRAM

    Vertex
    {
        layout (location = 0) in vec3 vertexPosition;
        layout (location = 1) in vec2 vertexTexCoord;

        void main()
        {
            gl_Position = vec4(vertexPosition, 1.0);
        }
    }

    Fragment
    {
        layout(location = 0) out vec4 OutputColor;
        layout(location = 1) out vec4 OutputDistance;

        uniform sampler2D _CloudAccumulated;      // colour times weight
        uniform sampler2D _CloudAccumulatedExtra; // kilometers times weight, weight
        uniform sampler2D _CloudMoments0;         // total optical depth first

        void main()
        {
            ivec2 pixel = ivec2(gl_FragCoord.xy);
            vec3 color = texelFetch(_CloudAccumulated, pixel, 0).rgb;
            vec3 extra = texelFetch(_CloudAccumulatedExtra, pixel, 0).rgb;
            float alpha = 1.0 - exp(-texelFetch(_CloudMoments0, pixel, 0).r);
            if (extra.g < 1e-8 || alpha < 1e-4)
            {
                OutputColor = vec4(0.0);
                OutputDistance = vec4(0.0);
                return;
            }
            OutputColor = vec4(color / extra.g * alpha, alpha);
            OutputDistance = vec4(extra.r / extra.g * alpha, 0.0, 0.0, alpha);
        }
    }

    ENDGLSL
}
