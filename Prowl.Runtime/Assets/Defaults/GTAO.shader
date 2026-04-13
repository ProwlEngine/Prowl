Shader "Default/GTAO"

Properties
{
}

Pass "CalculateGTAO"
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
            gl_Position = vec4(vertexPosition, 1.0);
            TexCoords = vertexTexCoord;
        }
    }

    Fragment
    {
        #include "Fragment"

        uniform sampler2D _CameraDepthTexture;
        uniform sampler2D _CameraNormalsTexture; // View-space normals from depth pre-pass

        uniform int _Slices;
        uniform int _DirectionSamples;
        uniform float _Radius;
        uniform float _Intensity;
        uniform vec2 _NoiseScale;

        in vec2 TexCoords;

        layout(location = 0) out vec4 aoOutput;

        void SampleHorizonCos(vec2 coord, vec2 offset, vec3 viewPos, vec3 viewDir, vec2 falloff, inout float cHorizonCos) {
            vec2 sTexCoord = coord + offset;

            // Check bounds
            if (sTexCoord.x < 0.0 || sTexCoord.x > 1.0 || sTexCoord.y < 0.0 || sTexCoord.y > 1.0)
                return;

            float sDepth = texture(_CameraDepthTexture, sTexCoord).r;
            if (sDepth >= 1.0) return;

            vec3 sHorizonV = getViewPos(sTexCoord, sDepth) - viewPos;

            float sLenV = sdot(sHorizonV);
            float sNormV = inversesqrt(sLenV);

            float sHorizonCos = dot(sHorizonV, viewDir) * sNormV;
            sHorizonCos = mix(sHorizonCos, cHorizonCos, linearstep(falloff.x, falloff.y, sLenV));
            cHorizonCos = max(sHorizonCos, cHorizonCos);
        }

        float CalculateGTAO(vec2 coord, vec3 viewPos, vec3 normal, vec2 dither) {
            float viewDistance = sdot(viewPos);
            float norm = inversesqrt(viewDistance);
            viewDistance *= norm;

            vec3 viewDir = viewPos * -norm;

            int sliceCount = _Slices;
            float rSliceCount = 1.0 / float(sliceCount);

            int sampleCount = _DirectionSamples;
            float rSampleCount = 1.0 / float(sampleCount);

            float radius = _Radius * saturate(0.25 + viewDistance * rcp(64.0));
            vec2 sRadius = rSampleCount * radius * norm * diagonal2(PROWL_MATRIX_P);
            vec2 falloff = sqr(radius * vec2(1.0, 4.0));

            float visibility = 0.0;

            for (int slice = 0; slice < sliceCount; ++slice) {
                float slicePhi = (float(slice) + dither.x) * (PROWL_PI * rSliceCount);

                vec3 directionV = vec3(cos(slicePhi), sin(slicePhi), 0.0);
                vec3 orthoDirectionV = directionV - dot(directionV, viewDir) * viewDir;
                vec3 axisV = cross(directionV, viewDir);
                vec3 projNormalV = normal - axisV * dot(normal, axisV);

                float lenV = sdot(projNormalV);
                float normV = inversesqrt(lenV);
                lenV *= normV;

                float sgnN = fastSign(dot(orthoDirectionV, projNormalV));
                float cosN = saturate(dot(projNormalV, viewDir) * normV);
                float n = sgnN * fastAcos(cosN);

                vec2 cHorizonCos = vec2(-1.0);

                for (int samp = 0; samp < sampleCount; ++samp) {
                    vec2 stepDir = directionV.xy * sRadius;
                    vec2 offset = (float(samp) + dither.y) * stepDir;

                    SampleHorizonCos(coord, offset, viewPos, viewDir, falloff, cHorizonCos.x);
                    SampleHorizonCos(coord, -offset, viewPos, viewDir, falloff, cHorizonCos.y);
                }

                vec2 h = n + clamp(vec2(fastAcos(cHorizonCos.x), -fastAcos(cHorizonCos.y)) - n, -PROWL_HALF_PI, PROWL_HALF_PI);
                h = cosN + 2.0 * h * sin(n) - cos(2.0 * h - n);

                visibility += lenV * (h.x + h.y);
            }

            return 0.25 * rSliceCount * visibility;
        }

        vec3 ApproxMultiBounce(float ao, vec3 albedo) {
            vec3 a = 2.0404 * albedo - 0.3324;
            vec3 b = 4.7951 * albedo - 0.6417;
            vec3 c = 2.7552 * albedo + 0.6903;

            return max(vec3(ao), ((ao * a - b) * ao + c) * ao);
        }

        void main()
        {
            float depth = texture(_CameraDepthTexture, TexCoords).r;

            // Sky
            if (depth >= 1.0) {
                aoOutput = vec4(1.0);
                return;
            }

            // Get view space data
            vec3 viewPos = getViewPos(TexCoords, depth);

            // Get view space normal from GBuffer
            vec4 normalData = texture(_CameraNormalsTexture, TexCoords);
            vec3 viewNormal = normalize(normalData.xyz * 2.0 - 1.0);

            // Generate temporal dither pattern
            vec2 noise = hash2(TexCoords * _NoiseScale + _Time.x);

            // Calculate GTAO
            float ao = CalculateGTAO(TexCoords, viewPos, viewNormal, noise);

            // Apply intensity
            ao = pow(saturate(ao), _Intensity);

            aoOutput = vec4(ao, ao, ao, 1.0);
        }
    }
    ENDGLSL
}

Pass "Blur"
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
            gl_Position = vec4(vertexPosition, 1.0);
            TexCoords = vertexTexCoord;
        }
    }

    Fragment
    {
        #include "Fragment"

        uniform sampler2D _MainTex;
        uniform sampler2D _CameraDepthTexture;
        uniform vec2 _BlurDirection;
        uniform float _BlurRadius;

        in vec2 TexCoords;

        layout(location = 0) out vec4 fragColor;

        void main()
        {
            vec2 texelSize = 1.0 / _ScreenParams.xy;
            float centerDepth = texture(_CameraDepthTexture, TexCoords).r;

            vec4 result = texture(_MainTex, TexCoords);
            float totalWeight = 1.0;

            // Depth-aware bilateral blur
            for (int i = -2; i <= 2; i++) {
                if (i == 0) continue;

                float offset = float(i) * _BlurRadius;
                vec2 sampleUV = TexCoords + _BlurDirection * texelSize * offset;

                float sampleDepth = texture(_CameraDepthTexture, sampleUV).r;
                float depthDiff = abs(centerDepth - sampleDepth);

                // Weight based on depth similarity
                float weight = exp(-depthDiff * 100.0) * exp(-0.5 * float(i * i) / 2.0);

                result += texture(_MainTex, sampleUV) * weight;
                totalWeight += weight;
            }

            fragColor = result / totalWeight;
        }
    }
    ENDGLSL
}

Pass "Composite"
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
            gl_Position = vec4(vertexPosition, 1.0);
            TexCoords = vertexTexCoord;
        }
    }

    Fragment
    {
        #include "Fragment"

        uniform sampler2D _MainTex;
        uniform sampler2D _AOTex;
        uniform float _Intensity;

        in vec2 TexCoords;

        layout(location = 0) out vec4 fragColor;

        vec3 ApproxMultiBounce(float ao, vec3 albedo) {
            vec3 a = 2.0404 * albedo - 0.3324;
            vec3 b = 4.7951 * albedo - 0.6417;
            vec3 c = 2.7552 * albedo + 0.6903;

            return max(vec3(ao), ((ao * a - b) * ao + c) * ao);
        }

        void main()
        {
            vec4 sceneColor = texture(_MainTex, TexCoords);
            float ao = texture(_AOTex, TexCoords).r;

            vec3 finalColor = sceneColor.rgb;

            finalColor *= ao;

            fragColor = vec4(finalColor, sceneColor.a);
        }
    }
    ENDGLSL
}
