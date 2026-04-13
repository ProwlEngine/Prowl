Shader "Default/Bloom"

Pass "Threshold"
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
        #include "Fragment"

        uniform sampler2D _MainTex;
        uniform float _Threshold;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec4 base = texture(_MainTex, TexCoords);
            vec3 color = base.rgb;

            float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
            float contribution = max(0.0, luminance - _Threshold);
            contribution /= max(luminance, 0.00001);

            FragColor = vec4(color * contribution, base.a);
        }
    }

    ENDGLSL
}

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
        #include "Fragment"

        uniform sampler2D _MainTex;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec2 halfpixel = 0.5 / vec2(textureSize(_MainTex, 0));

            vec4 sum = texture(_MainTex, TexCoords) * 4.0;
            sum += texture(_MainTex, TexCoords - halfpixel);
            sum += texture(_MainTex, TexCoords + halfpixel);
            sum += texture(_MainTex, TexCoords + vec2(halfpixel.x, -halfpixel.y));
            sum += texture(_MainTex, TexCoords - vec2(halfpixel.x, -halfpixel.y));

            FragColor = sum / 8.0;
        }
    }

    ENDGLSL
}

Pass "Upsample"
{
    Blend Additive
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
        #include "Fragment"

        uniform sampler2D _MainTex;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec2 halfpixel = 0.5 / vec2(textureSize(_MainTex, 0));

            vec4 sum = texture(_MainTex, TexCoords + vec2(-halfpixel.x * 2.0, 0.0));
            sum += texture(_MainTex, TexCoords + vec2(-halfpixel.x, halfpixel.y)) * 2.0;
            sum += texture(_MainTex, TexCoords + vec2(0.0, halfpixel.y * 2.0));
            sum += texture(_MainTex, TexCoords + vec2(halfpixel.x, halfpixel.y)) * 2.0;
            sum += texture(_MainTex, TexCoords + vec2(halfpixel.x * 2.0, 0.0));
            sum += texture(_MainTex, TexCoords + vec2(halfpixel.x, -halfpixel.y)) * 2.0;
            sum += texture(_MainTex, TexCoords + vec2(0.0, -halfpixel.y * 2.0));
            sum += texture(_MainTex, TexCoords + vec2(-halfpixel.x, -halfpixel.y)) * 2.0;

            FragColor = sum / 12.0;
        }
    }

    ENDGLSL
}

Pass "Composite"
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
        #include "Fragment"

        uniform sampler2D _MainTex;
        uniform sampler2D _BloomTex;
        uniform float _Intensity;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec4 originalColor = texture(_MainTex, TexCoords);
            vec3 bloomColor = texture(_BloomTex, TexCoords).rgb;

            vec3 finalColor = originalColor.rgb + bloomColor * _Intensity;

            FragColor = vec4(finalColor, originalColor.a);
        }
    }

    ENDGLSL
}
