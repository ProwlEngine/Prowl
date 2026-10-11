Shader "Graphite/Sky/Cubemap"
{
    Properties
    {
        _CubeRight("Right (+X)", Texture2D) = "white" {}
        _CubeLeft("Left (-X)", Texture2D) = "white" {}
        _CubeTop("Top (+Y)", Texture2D) = "white" {}
        _CubeBottom("Bottom (-Y)", Texture2D) = "white" {}
        _CubeFront("Front (+Z)", Texture2D) = "white" {}
        _CubeBack("Back (-Z)", Texture2D) = "white" {}
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Exposure("Exposure", Float) = 1.0
    }

    Pass
    {
        Tags { "LightMode" = "Sky" }
        GLSLPROGRAM
        Vertex
        {
            #include "Graphite/Sky.glsl"
            void main() { }
        }
        Fragment
        {
            #include "Graphite/Sky.glsl"
            uniform sampler2D _CubeRight;
            uniform sampler2D _CubeLeft;
            uniform sampler2D _CubeTop;
            uniform sampler2D _CubeBottom;
            uniform sampler2D _CubeFront;
            uniform sampler2D _CubeBack;
            uniform vec4 _Tint;
            uniform float _Exposure;
            layout(location = 0) out vec4 outColor;

            // A cube face from six separate images
            vec4 SampleCubemap(vec3 dir)
            {
                vec3 absDir = abs(dir);
                if (absDir.x >= absDir.y && absDir.x >= absDir.z)
                {
                    if (dir.x > 0.0)
                        return texture(_CubeRight, vec2(-dir.z, -dir.y) / absDir.x * 0.5 + 0.5);
                    return texture(_CubeLeft, vec2(dir.z, -dir.y) / absDir.x * 0.5 + 0.5);
                }
                if (absDir.y >= absDir.x && absDir.y >= absDir.z)
                {
                    if (dir.y > 0.0)
                        return texture(_CubeTop, vec2(dir.x, dir.z) / absDir.y * 0.5 + 0.5);
                    return texture(_CubeBottom, vec2(dir.x, -dir.z) / absDir.y * 0.5 + 0.5);
                }
                if (dir.z > 0.0)
                    return texture(_CubeFront, vec2(dir.x, -dir.y) / absDir.z * 0.5 + 0.5);
                return texture(_CubeBack, vec2(-dir.x, -dir.y) / absDir.z * 0.5 + 0.5);
            }

            void main()
            {
                vec4 color = SampleCubemap(Graphite_SkyDirection());
                outColor = vec4(color.rgb * _Tint.rgb * _Exposure, 1.0);
            }
        }
        ENDGLSL
    }
}
