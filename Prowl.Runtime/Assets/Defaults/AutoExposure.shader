Shader "Default/AutoExposure"

// Pass 0: Extract log-luminance from HDR scene and downsample to half-res
Pass "LuminanceExtract"
{
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
        #include "ProwlCG"

        uniform sampler2D _MainTex;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec2 texelSize = 1.0 / vec2(textureSize(_MainTex, 0));

            // 4-tap box filter at half resolution
            vec3 c0 = texture(_MainTex, TexCoords + vec2(-0.5, -0.5) * texelSize).rgb;
            vec3 c1 = texture(_MainTex, TexCoords + vec2( 0.5, -0.5) * texelSize).rgb;
            vec3 c2 = texture(_MainTex, TexCoords + vec2(-0.5,  0.5) * texelSize).rgb;
            vec3 c3 = texture(_MainTex, TexCoords + vec2( 0.5,  0.5) * texelSize).rgb;

            // Compute log-luminance for each sample (log of geometric mean)
            float l0 = log(max(luminance(c0), 0.0001));
            float l1 = log(max(luminance(c1), 0.0001));
            float l2 = log(max(luminance(c2), 0.0001));
            float l3 = log(max(luminance(c3), 0.0001));

            float avgLogLum = (l0 + l1 + l2 + l3) * 0.25;
            FragColor = vec4(avgLogLum, 0.0, 0.0, 1.0);
        }
    }

    ENDGLSL
}

// Pass 1: Downsample log-luminance (box filter, reused in chain)
Pass "Downsample"
{
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
        uniform sampler2D _MainTex;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec2 texelSize = 1.0 / vec2(textureSize(_MainTex, 0));

            // 4-tap box filter
            float s0 = texture(_MainTex, TexCoords + vec2(-0.5, -0.5) * texelSize).r;
            float s1 = texture(_MainTex, TexCoords + vec2( 0.5, -0.5) * texelSize).r;
            float s2 = texture(_MainTex, TexCoords + vec2(-0.5,  0.5) * texelSize).r;
            float s3 = texture(_MainTex, TexCoords + vec2( 0.5,  0.5) * texelSize).r;

            FragColor = vec4((s0 + s1 + s2 + s3) * 0.25, 0.0, 0.0, 1.0);
        }
    }

    ENDGLSL
}

// Pass 2: Temporal adaptation - smoothly blend current luminance toward measured value
Pass "Adapt"
{
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
        #include "ShaderVariables"

        uniform sampler2D _MainTex;      // Current measured log-luminance (1x1 or small)
        uniform sampler2D _AdaptedTex;   // Previous adapted luminance
        uniform float _AdaptSpeedUp;     // Speed when going brighter (EV/s)
        uniform float _AdaptSpeedDown;   // Speed when going darker (EV/s)
        uniform float _HistoryValid;     // 0.0 = first frame, snap to current

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            // Current geometric mean luminance from downsample chain
            float currentLogLum = texture(_MainTex, vec2(0.5)).r;
            float currentLum = exp(currentLogLum);

            // Previous adapted luminance
            float prevLum = texture(_AdaptedTex, vec2(0.5)).r;

            if (_HistoryValid < 0.5)
            {
                // First frame - snap to current
                FragColor = vec4(currentLum, 0.0, 0.0, 1.0);
                return;
            }

            // Asymmetric adaptation speed: faster going dark->bright, slower bright->dark
            // (or vice versa depending on user config)
            float speed = (currentLum > prevLum) ? _AdaptSpeedUp : _AdaptSpeedDown;

            float dt = prowl_DeltaTime.x;
            float adaptFactor = 1.0 - exp(-dt * speed);
            float adaptedLum = prevLum + (currentLum - prevLum) * adaptFactor;

            // Clamp to reasonable range to prevent extreme values
            adaptedLum = clamp(adaptedLum, 0.0001, 100.0);

            FragColor = vec4(adaptedLum, 0.0, 0.0, 1.0);
        }
    }

    ENDGLSL
}

// Pass 3: Apply exposure to HDR scene color
Pass "ApplyExposure"
{
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
        uniform sampler2D _MainTex;      // HDR scene color
        uniform sampler2D _AdaptedTex;   // 1x1 adapted luminance
        uniform float _ExposureComp;     // Exposure compensation in EV stops
        uniform float _MinExposure;      // Minimum exposure multiplier
        uniform float _MaxExposure;      // Maximum exposure multiplier

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec4 sceneColor = texture(_MainTex, TexCoords);
            float adaptedLum = texture(_AdaptedTex, vec2(0.5)).r;

            // Standard exposure formula: key / luminance
            // 0.18 is the standard "middle gray" key value
            float exposure = 0.18 / max(adaptedLum, 0.0001);

            // Apply EV compensation (each stop doubles/halves exposure)
            exposure *= exp2(_ExposureComp);

            // Clamp exposure to user-defined range
            exposure = clamp(exposure, _MinExposure, _MaxExposure);

            FragColor = vec4(sceneColor.rgb * exposure, sceneColor.a);
        }
    }

    ENDGLSL
}
