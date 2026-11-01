Shader "Graphite/Sky/SixSided"
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
        intensity("Intensity", Float) = 20000.0
    }

    Surface "Sky"
    {
        GLSLPROGRAM
        uniform sampler2D _CubeRight;
        uniform sampler2D _CubeLeft;
        uniform sampler2D _CubeTop;
        uniform sampler2D _CubeBottom;
        uniform sampler2D _CubeFront;
        uniform sampler2D _CubeBack;
        uniform vec4 _Tint;
        uniform float intensity;

        // A cube face from six separate images
        vec3 SampleCubemap(vec3 dir)
        {
            vec3 absDir = abs(dir);
            if (absDir.x >= absDir.y && absDir.x >= absDir.z)
            {
                if (dir.x > 0.0)
                    return texture(_CubeRight, vec2(-dir.z, -dir.y) / absDir.x * 0.5 + 0.5).rgb;
                return texture(_CubeLeft, vec2(dir.z, -dir.y) / absDir.x * 0.5 + 0.5).rgb;
            }
            if (absDir.y >= absDir.x && absDir.y >= absDir.z)
            {
                if (dir.y > 0.0)
                    return texture(_CubeTop, vec2(dir.x, dir.z) / absDir.y * 0.5 + 0.5).rgb;
                return texture(_CubeBottom, vec2(dir.x, -dir.z) / absDir.y * 0.5 + 0.5).rgb;
            }
            if (dir.z > 0.0)
                return texture(_CubeFront, vec2(dir.x, -dir.y) / absDir.z * 0.5 + 0.5).rgb;
            return texture(_CubeBack, vec2(-dir.x, -dir.y) / absDir.z * 0.5 + 0.5).rgb;
        }

        vec3 Sky(vec3 direction)
        {
            return SampleCubemap(direction) * _Tint.rgb * intensity;
        }
        ENDGLSL
    }
}
