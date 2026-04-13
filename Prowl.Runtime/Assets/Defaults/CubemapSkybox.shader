Shader "Default/CubemapSkybox"

Properties
{
    _CubeRight ("Right (+X)", Texture2D) = "white"
    _CubeLeft ("Left (-X)", Texture2D) = "white"
    _CubeTop ("Top (+Y)", Texture2D) = "white"
    _CubeBottom ("Bottom (-Y)", Texture2D) = "white"
    _CubeFront ("Front (+Z)", Texture2D) = "white"
    _CubeBack ("Back (-Z)", Texture2D) = "white"
    _Tint ("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
    _Exposure ("Exposure", Float) = 1.0
}

Pass "CubemapSkybox"
{
    Tags { "RenderOrder" = "Opaque" }
    Cull Front
    ZWrite Off
    ZTest LEqual

    GLSLPROGRAM

        Vertex
        {
            #include "Fragment"
            #include "VertexAttributes"

            out vec3 vDirection;

            void main()
            {
                mat4 viewNoTranslation = PROWL_MATRIX_V;
                viewNoTranslation[3][0] = 0.0;
                viewNoTranslation[3][1] = 0.0;
                viewNoTranslation[3][2] = 0.0;

                vec4 pos = PROWL_MATRIX_P * viewNoTranslation * vec4(vertexPosition, 1.0);
                gl_Position = pos.xyww;

                vDirection = vertexPosition;
            }
        }

        Fragment
        {
            #include "Fragment"

            layout (location = 0) out vec4 fragColor;

            in vec3 vDirection;

            uniform sampler2D _CubeRight;
            uniform sampler2D _CubeLeft;
            uniform sampler2D _CubeTop;
            uniform sampler2D _CubeBottom;
            uniform sampler2D _CubeFront;
            uniform sampler2D _CubeBack;
            uniform vec4 _Tint;
            uniform float _Exposure;

            // Sample a cubemap face from 6 separate 2D textures
            vec4 sampleCubemap(vec3 dir)
            {
                vec3 absDir = abs(dir);
                vec2 uv;
                vec4 color;

                if (absDir.x >= absDir.y && absDir.x >= absDir.z)
                {
                    // X dominant
                    if (dir.x > 0.0)
                    {
                        uv = vec2(-dir.z, -dir.y) / absDir.x * 0.5 + 0.5;
                        color = texture(_CubeRight, uv);
                    }
                    else
                    {
                        uv = vec2(dir.z, -dir.y) / absDir.x * 0.5 + 0.5;
                        color = texture(_CubeLeft, uv);
                    }
                }
                else if (absDir.y >= absDir.x && absDir.y >= absDir.z)
                {
                    // Y dominant
                    if (dir.y > 0.0)
                    {
                        uv = vec2(dir.x, dir.z) / absDir.y * 0.5 + 0.5;
                        color = texture(_CubeTop, uv);
                    }
                    else
                    {
                        uv = vec2(dir.x, -dir.z) / absDir.y * 0.5 + 0.5;
                        color = texture(_CubeBottom, uv);
                    }
                }
                else
                {
                    // Z dominant
                    if (dir.z > 0.0)
                    {
                        uv = vec2(dir.x, -dir.y) / absDir.z * 0.5 + 0.5;
                        color = texture(_CubeFront, uv);
                    }
                    else
                    {
                        uv = vec2(-dir.x, -dir.y) / absDir.z * 0.5 + 0.5;
                        color = texture(_CubeBack, uv);
                    }
                }

                return color;
            }

            void main()
            {
                vec3 dir = normalize(vDirection);
                vec4 color = sampleCubemap(dir);
                color.rgb *= _Tint.rgb * _Exposure;

                fragColor = vec4(color.rgb, 1.0);
            }
        }
    ENDGLSL
}
