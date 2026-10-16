Shader "Skybox/Panorama"

Properties
{
    _Panorama ("Panorama", Texture2D) = "white"
    _Tint ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Exposure ("Exposure", Float) = 1.0
    _Rotation ("Rotation", Range(0, 360)) = 0.0
}

Pass "PanoramaSkybox"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Front
    ZWrite Off
    ZTest LEqual

    GLSLPROGRAM

        Vertex
        {
            #include "ProwlCG"
            #include "VertexAttributes"

            out vec3 vDirection;

            void main()
            {
                // Strip translation from view matrix so skybox stays centered
                mat4 viewNoTranslation = PROWL_MATRIX_V;
                viewNoTranslation[3][0] = 0.0;
                viewNoTranslation[3][1] = 0.0;
                viewNoTranslation[3][2] = 0.0;

                vec4 pos = prowlSkyProjection() * viewNoTranslation * vec4(vertexPosition, 1.0);
                gl_Position = pos.xyww; // depth = 1.0 (far plane)

                vDirection = normalize(vertexPosition);
            }
        }

        Fragment
        {
            #include "ProwlCG"

            layout (location = 0) out vec4 fragColor;

            in vec3 vDirection;

            uniform sampler2D _Panorama;
            uniform vec4 _Tint;
            uniform float _Exposure;
            uniform float _Rotation;

            // Sample a panorama texture
            vec4 samplePanorama(vec3 dir)
            {
                // Convert direction to UV coordinates (simplified equirectangular projection)
                // Note: fract() manually wraps the UV coordinates, effectively ignoring the texture's wrap mode.
                vec2 uv = vec2(
                    fract((atan(dir.x, dir.z) / PROWL_TWO_PI) + (_Rotation / 360.0)),
                    asin(dir.y) / PROWL_PI + 0.5
                );
                vec4 color = texture(_Panorama, uv);
                return color;
            }

            void main()
            {
                vec3 dir = normalize(vDirection);
                vec4 color = samplePanorama(dir);
                color.rgb *= _Tint.rgb * _Exposure;

                fragColor = vec4(color.rgb, 1.0);
            }
        }
    ENDGLSL
}
