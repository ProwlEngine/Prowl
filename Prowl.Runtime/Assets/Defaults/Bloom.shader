Shader "Hidden/Post Process/Bloom"

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
        #include "ProwlCG"

        uniform sampler2D _MainTex;
        uniform float _Threshold;
        uniform float _SoftKnee;   // 0 = hard cut at the threshold, 1 = widest smooth ramp into it
        uniform float _Clamp;      // brightest color fed into the bloom
        uniform float _AntiFlicker; // 1 = weight taps down by brightness to tame single pixel sparkles

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        // Keeps what is above the threshold, easing in across a quadratic knee instead of a hard cut
        // so values crossing the threshold fade in rather than pop.
        vec3 Prefilter(vec3 color)
        {
            color = min(max(color, vec3(0.0)), vec3(_Clamp));
            float brightness = max(color.r, max(color.g, color.b));
            float knee = _Threshold * _SoftKnee + 1e-5;
            float soft = clamp(brightness - _Threshold + knee, 0.0, 2.0 * knee);
            soft = soft * soft / (4.0 * knee);
            float contribution = max(soft, brightness - _Threshold) / max(brightness, 1e-5);
            return color * contribution;
        }

        void main()
        {
            // Four bilinear taps one source texel out cover a 4x4 block around this half resolution pixel.
            // With anti flicker on, each is weighted by 1 / (1 + luma) so a lone very bright pixel can't
            // dominate the average, at the cost of dimming small bright sources.
            vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0));
            vec2 offsets[4] = vec2[](vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(-1.0, 1.0), vec2(1.0, 1.0));

            vec3 sum = vec3(0.0);
            float weightSum = 0.0;
            for (int i = 0; i < 4; i++)
            {
                vec3 s = Prefilter(texture(_MainTex, TexCoords + offsets[i] * texel).rgb);
                float w = _AntiFlicker > 0.5 ? 1.0 / (1.0 + luminance(s)) : 1.0;
                sum += s * w;
                weightSum += w;
            }

            FragColor = vec4(sum / weightSum, 1.0);
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
        #include "ProwlCG"

        uniform sampler2D _MainTex;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            // One full source texel out, so each corner tap lands between texels and bilinear averages a
            // 2x2 block of its own. Together the taps cover 4x4; at half a texel they all fell inside the
            // centre 2x2 and the chain was a box filter, which aliases and shimmers.
            vec2 texel = 1.0 / vec2(textureSize(_MainTex, 0));

            vec4 sum = texture(_MainTex, TexCoords) * 4.0;
            sum += texture(_MainTex, TexCoords - texel);
            sum += texture(_MainTex, TexCoords + texel);
            sum += texture(_MainTex, TexCoords + vec2(texel.x, -texel.y));
            sum += texture(_MainTex, TexCoords - vec2(texel.x, -texel.y));

            FragColor = sum / 8.0;
        }
    }

    ENDGLSL
}

Pass "Upsample"
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

        uniform sampler2D _MainTex;   // the blurred level below, half this size
        uniform sampler2D _HighTex;   // this level's own downsample
        uniform float _Scatter;       // how much of the wider, blurrier levels to keep

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

            // A weighted mix rather than an add, so the levels fade from sharp to wide instead of every
            // one showing at full strength, and the total stays the same however many levels there are.
            FragColor = mix(texture(_HighTex, TexCoords), sum / 12.0, _Scatter);
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
        #include "ProwlCG"

        uniform sampler2D _MainTex;
        uniform sampler2D _BloomTex;
        uniform float _Intensity;

        layout(location = 0) out vec4 FragColor;

        in vec2 TexCoords;

        void main()
        {
            vec4 originalColor = texture(_MainTex, TexCoords);
            vec3 bloomColor = texture(_BloomTex, TexCoords).rgb;

            vec3 finalColor = originalColor.rgb + bloomColor * _Intensity * 2.0;

            FragColor = vec4(finalColor, originalColor.a);
        }
    }

    ENDGLSL
}
