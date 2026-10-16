Shader "Graphite/Sky/Gradient"
{
    Properties
    {
        _TopColor("Top Color", Color) = (0.4, 0.6, 0.9, 1.0)
        _BottomColor("Bottom Color", Color) = (0.8, 0.8, 0.7, 1.0)
        _Exponent("Exponent", Float) = 1.0
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
            uniform vec4 _TopColor;
            uniform vec4 _BottomColor;
            uniform float _Exponent;
            layout(location = 0) out vec4 outColor;
            void main()
            {
                float t = pow(clamp(Graphite_SkyDirection().y * 0.5 + 0.5, 0.0, 1.0), _Exponent);
                outColor = vec4(mix(_BottomColor.rgb, _TopColor.rgb, t), 1.0);
            }
        }
        ENDGLSL
    }
}
