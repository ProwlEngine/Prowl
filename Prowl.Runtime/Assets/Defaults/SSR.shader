Shader "Default/SSR"

Pass "RayMarch"
{
    Tags { "RenderOrder" = "Opaque" }
    Blend Override
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
        uniform sampler2D _CameraNormalsTexture;

        uniform float _MaxSteps;
        uniform float _BinarySearchIterations;
        uniform float _ScreenEdgeFade;

        in vec2 TexCoords;

        layout(location = 0) out vec4 reflectionData;


        // Project view space position to screen space (UV + depth)
        vec3 projectToScreenSpace(vec3 viewPos)
        {
            vec4 clipPos = prowl_MatP * vec4(viewPos, 1.0);
            clipPos.xyz /= clipPos.w;
            return vec3(clipPos.xy * 0.5 + 0.5, clipPos.z);
        }

        // Screen space ray marching
        bool traceScreenSpaceRay(
            vec3 startScreen, // xy = UV, z = depth
            vec3 reflectionDir,
            out vec2 hitUV,
            out float hitDepth
        )
        {
            // Calculate end point in view space and project to screen
            vec3 startView = getViewPos(startScreen.xy, startScreen.z);
            vec3 endView = startView + reflectionDir * 100.0; // Ray length in view space
            vec3 endScreen = projectToScreenSpace(endView);

            // Calculate screen space ray direction
            vec3 rayDelta = endScreen - startScreen;

            // Avoid degenerate rays
            if (length(rayDelta.xy) < 0.0001)
                return false;

            // Calculate step size to ensure we sample at least once per pixel
            vec2 screenSize = _ScreenParams.xy;
            float screenSteps = max(abs(rayDelta.x) * screenSize.x, abs(rayDelta.y) * screenSize.y);
            float stepCount = min(screenSteps, _MaxSteps);
            vec3 rayStep = rayDelta / max(stepCount, 1.0);

            // March the ray
            vec3 currentScreen = startScreen + rayStep; // Start one step forward

            for (float i = 1.0; i < stepCount; i += 1.0)
            {
                // Check bounds
                if (currentScreen.x < 0.0 || currentScreen.x > 1.0 ||
                    currentScreen.y < 0.0 || currentScreen.y > 1.0)
                    return false;

                // Sample scene depth
                float sceneDepth = texture(_CameraDepthTexture, currentScreen.xy).r;

                // Check intersection
                float depthDiff = currentScreen.z - sceneDepth;

                if (depthDiff >= 0)
                {
                    if (depthDiff < rayStep.z)
                    {
                        break;
                    }

                    currentScreen -= rayStep;
                    rayStep *= 0.5;
                }

                currentScreen += rayStep;
                
                hitUV = currentScreen.xy;
                hitDepth = currentScreen.z;
            }

            return true;
        }

        void main()
        {
            // If starting depth is >= 1.0, no reflection
            float startDepth = texture(_CameraDepthTexture, TexCoords).r;
            if (startDepth >= 1.0)
            {
                reflectionData = vec4(0.0, 0.0, 0.0, 0.0);
                return;
            }

            // Sample normals from pre-pass
            vec4 normalData = texture(_CameraNormalsTexture, TexCoords);
            vec3 viewNormal = normalData.xyz * 2.0 - 1.0;

            // Skip pixels with no valid normal (e.g. sky)
            if (length(normalData.xyz) < 0.01)
                return;

            // Get view space position and calculate reflection direction
            vec3 viewPos = getViewPos(TexCoords, startDepth);
            vec3 viewDir = normalize(viewPos);
            vec3 reflectionDir = reflect(viewDir, viewNormal);

            // Create start position in screen space (UV + depth)
            vec3 startScreen = vec3(TexCoords, startDepth);

            // Trace the ray in screen space
            vec2 hitUV;
            float hitDepth;
            bool hit = traceScreenSpaceRay(startScreen, reflectionDir, hitUV, hitDepth);

            // Calculate confidence/mask based on various factors
            float confidence = 1.0;

            if (hit)
            {
                // Fade based on screen edge distance
                vec2 edgeDist = abs(hitUV * 2.0 - 1.0);
                float edgeFactor = 1.0 - pow(max(edgeDist.x, edgeDist.y), _ScreenEdgeFade);
                confidence *= edgeFactor;

                // Output: hitUV (xy), confidence (z), hit flag (w)
                reflectionData = vec4(hitUV, confidence, 1.0);
            }
            else
            {
                reflectionData = vec4(0.0, 0.0, 0.0, 0.0);
            }
        }
    }
    ENDGLSL
}

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
        uniform sampler2D _ReflectionData;
        uniform float _MipBias;

        in vec2 TexCoords;

        layout(location = 0) out vec4 fragColor;

        void main()
        {
            vec4 reflData = texture(_ReflectionData, TexCoords);

            if (reflData.w > 0.0) // Hit
            {
                vec2 hitUV = reflData.xy;
                float confidence = reflData.z;

                // Sample the scene color at the hit point with mip bias for roughness
                vec3 reflectionColor = texture(_MainTex, hitUV).rgb;

                fragColor = vec4(reflectionColor, confidence);
            }
            else
            {
                fragColor = vec4(0.0, 0.0, 0.0, 0.0);
            }
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
        uniform vec2 _BlurDirection;
        uniform float _BlurRadius;

        in vec2 TexCoords;

        layout(location = 0) out vec4 fragColor;

        void main()
        {
            vec2 texelSize = 1.0 / _ScreenParams.xy;
            vec4 result = vec4(0.0);
            float totalWeight = 0.0;

            // Simple gaussian-ish blur
            for (int i = -4; i <= 4; i++)
            {
                float offset = float(i) * _BlurRadius;
                vec2 sampleUV = TexCoords + _BlurDirection * texelSize * offset;

                float weight = exp(-0.5 * float(i * i) / 4.0);
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
        uniform sampler2D _ReflectionTex;
        uniform float _Intensity;

        in vec2 TexCoords;

        layout(location = 0) out vec4 fragColor;

        void main()
        {
            vec4 sceneColor = texture(_MainTex, TexCoords);
            vec4 reflectionColor = texture(_ReflectionTex, TexCoords);
            
            vec3 finalColor = mix(sceneColor.rgb, reflectionColor.rgb, reflectionColor.a);
            fragColor = vec4(finalColor, sceneColor.a);

            //// Get material properties
            //vec4 pbrData = texture(_GBufferC, TexCoords);
            //float roughness = pbrData.r;
            //float metalness = pbrData.g;
            //float specular = pbrData.b;
            //
            //// Calculate fresnel-like term
            //float reflectivity = mix(specular, 1.0, metalness);
            //
            //// Apply intensity
            //float finalAlpha = reflectionColor.a * _Intensity * reflectivity;
            //
            //// Blend reflection with scene
            //vec3 finalColor = mix(sceneColor.rgb, reflectionColor.rgb, 0.5);
            //
            //fragColor = vec4(finalColor, sceneColor.a);
        }
    }
    ENDGLSL
}
