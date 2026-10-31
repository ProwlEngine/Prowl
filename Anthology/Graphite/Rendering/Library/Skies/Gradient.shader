Shader "Graphite/Sky/Gradient"
{
    Properties
    {
        _TopColor("Top Color", Color) = (0.4, 0.6, 0.9, 1.0)
        _BottomColor("Bottom Color", Color) = (0.8, 0.8, 0.7, 1.0)
        _Exponent("Exponent", Float) = 1.0
    }

    Surface "Sky"
    {
        GLSLPROGRAM
        uniform vec4 _TopColor;
        uniform vec4 _BottomColor;
        uniform float _Exponent;

        vec3 Sky(vec3 direction)
        {
            float t = pow(clamp(direction.y * 0.5 + 0.5, 0.0, 1.0), _Exponent);
            return mix(_BottomColor.rgb, _TopColor.rgb, t);
        }
        ENDGLSL
    }
}
