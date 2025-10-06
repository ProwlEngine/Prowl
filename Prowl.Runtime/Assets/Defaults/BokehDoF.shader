Shader "Default/EnhancedBokehdoF"

Properties
{
}

Pass "BokehDoF"
{
    Tags { "RenderOrder" = "Opaque" }

    // Rasterizer culling mode
    Cull None
    ZTest Off
    ZWrite Off

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
        #include "Fragment"
        
        layout(location = 0) out vec4 OutputColor;
        
        in vec2 TexCoords;

        uniform sampler2D _MainTex;
        uniform sampler2D _CameraDepthTexture;
        
        uniform float _BlurRadius;
        uniform float _FocusStrength;
        uniform float _Quality;
        uniform float _ManualFocusPoint;
        uniform vec2 _Resolution;

        float getBlurSize(float depth, float focusPoint, float focusScale)
        {
            // Calculate Circle of Confusion using the same approach as the reference shader
            float coc = clamp((1.0 / focusPoint - 1.0 / depth) * focusScale, -1.0, 1.0);
            return abs(coc) * _BlurRadius;
        }
        
        vec3 depthOfField(vec2 texCoord, float focusPoint, float focusScale)
        {
            vec3 color = texture(_MainTex, texCoord).rgb;
            float centerDepth = texture(_CameraDepthTexture, texCoord).x;
            float centerSize = getBlurSize(centerDepth, focusPoint, focusScale);
            float tot = 1.0;
            
            vec2 texelSize = vec2(1.0, 1.0) / _Resolution * 1.5;
            
            float quality = 1.0 - _Quality;
            float radius = quality;
            
            // Golden angle spiral sampling
            const float GOLDEN_ANGLE = 2.39996323;
            
            for (float ang = 0.0; radius < _BlurRadius; ang += GOLDEN_ANGLE)
            {
                vec2 tc = texCoord + vec2(cos(ang), sin(ang)) * texelSize * radius;
                
                // Get sample depth and calculate its blur size
                float sampleDepth = texture(_CameraDepthTexture, tc).x;
                float sampleSize = getBlurSize(sampleDepth, focusPoint, focusScale);
                
                vec3 sampleColor = texture(_MainTex, tc).rgb;
                
                // Depth-aware blending to reduce bleeding from background to foreground
                if (sampleDepth > centerDepth)
                {
                    sampleSize = clamp(sampleSize, 0.0, centerSize * 2.0);
                }
                
                // Smooth blending based on blur size
                float m = smoothstep(radius - 0.5, radius + 0.5, sampleSize);
                color += mix(color/tot, sampleColor, m);
                tot += 1.0;
                
                // Increase radius for next sample - this creates an adaptive sampling pattern
                radius += quality / radius;
            }
            
            return color / tot;
        }

        void main()
        {
            // Get focus point - center of screen for auto focus or manual value
        #ifdef AUTOFOCUS
            float focusPoint = texture(_CameraDepthTexture, vec2(0.5, 0.5)).x;
        #else
            float focusPoint = _ManualFocusPoint;
        #endif
            
            // Apply depth of field effect
            vec3 finalColor = depthOfField(TexCoords, focusPoint, _FocusStrength);
            
            // Output final color
            OutputColor = vec4(finalColor, 1.0);
        }
    }

    ENDGLSL
}

            
Pass "DoFCombine"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull None
    ZTest Off
    ZWrite Off

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
        layout(location = 0) out vec4 OutputColor;
        
        in vec2 TexCoords;

        uniform sampler2D _MainTex;           // Original full-res image
        uniform sampler2D _DownsampledDoF;    // Downsampled DoF result
        uniform sampler2D _CameraDepthTexture;
        
        uniform float _BlurRadius;
        uniform float _FocusStrength;
        uniform float _ManualFocusPoint;
        uniform float _UseAutoFocus;

        float getBlurSize(float depth, float focusPoint, float focusScale)
        {
            float coc = clamp((1.0 / focusPoint - 1.0 / depth) * focusScale, -1.0, 1.0);
            return abs(coc) * _BlurRadius;
        }

        void main()
        {
            // Get focus point - center of screen for auto focus or manual value
        #ifdef AUTOFOCUS
            float focusPoint = texture(_CameraDepthTexture, vec2(0.5, 0.5)).x;
        #else
            float focusPoint = _ManualFocusPoint;
        #endif
            
            // Get original color and depth
            vec3 originalColor = texture(_MainTex, TexCoords).rgb;
            float depth = texture(_CameraDepthTexture, TexCoords).x;
            
            // Calculate current pixel's CoC
            float cocSize = getBlurSize(depth, focusPoint, _FocusStrength);
            
            // Get downsampled DoF result
            vec4 dofResult = texture(_DownsampledDoF, TexCoords);
            vec3 blurredColor = dofResult.rgb;
            
            // Use CoC to blend between original and blurred image
            // Small CoC = sharp original image
            // Large CoC = blurred image
            float blendFactor = smoothstep(0.0, 0.2, cocSize / _BlurRadius);
            
            // Combine
            vec3 finalColor = mix(originalColor, blurredColor, blendFactor);
            
            OutputColor = vec4(finalColor, 1.0);
        }
    }

    ENDGLSL
}