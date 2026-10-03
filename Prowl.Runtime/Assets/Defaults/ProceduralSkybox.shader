Shader "Skybox/Procedural"

Properties
{
}

Pass "Skybox"
{
    Tags { "RenderOrder" = "Opaque" }

    Blend Off
    Cull None
    ZTest Off
    ZWrite Off

    GLSLPROGRAM

        Vertex
        {
            #include "ProwlCG"

            in vec3 vertexPosition;

            out vec3 vDirection;

            void main()
            {
                // Keep the sky centered on the camera
                mat4 viewNoTranslation = PROWL_MATRIX_V;
                viewNoTranslation[3][0] = 0.0;
                viewNoTranslation[3][1] = 0.0;
                viewNoTranslation[3][2] = 0.0;

                gl_Position = prowlSkyProjection() * viewNoTranslation * vec4(vertexPosition, 1.0);
                gl_Position.z = gl_Position.w;
                vDirection = vertexPosition;
            }
        }

        Fragment
        {
            #include "ProwlCG"

            layout(location = 0) out vec4 fragColor;

            in vec3 vDirection;

            uniform vec3 _SunDir;

            vec3 jodieReinhardTonemap(vec3 c)
            {
                float l = dot(c, vec3(0.2126, 0.7152, 0.0722));
                vec3 tc = c / (c + 1.0);
                return mix(c / (l + 1.0), tc, tc);
            }

            void main()
            {
                vec3 view = normalize(vDirection);
                vec3 color = prowlSky(view, _SunDir, 1.0);

                // Sun disk, and a darker ground below the horizon
                float sunTransmittance = exp(-dot(vec3(0.33), vec3(5.802e-6, 13.558e-6, 33.1e-6)) * 8000.0 / max(view.y, 0.02));
                float disk = smoothstep(0.99985, 0.99999, dot(view, _SunDir)) * step(0.0, view.y);
                color += disk * 2000.0 * sunTransmittance * vec3(1.0, 0.9, 0.8);
                color = mix(color, color * 0.25, smoothstep(0.0, -0.02, view.y));

                fragColor = vec4(jodieReinhardTonemap(color * 40.0), 1.0);
            }
        }
    ENDGLSL
}
