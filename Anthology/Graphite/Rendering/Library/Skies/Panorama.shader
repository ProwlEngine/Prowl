Shader "Graphite/Sky/Panorama"
{
    Properties
    {
        _Panorama("Panorama", Texture2D) = "gray" {} [NoScaleOffset]
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        _Exposure("Exposure", Float) = 1.0 [Range(0, 8)]
        _Rotation("Rotation", Float) = 0.0 [Range(0, 360)]
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
            #include "Graphite/Skies.glsl"
            uniform sampler2D _Panorama;
            uniform vec4 _Tint;
            uniform float _Exposure;
            uniform float _Rotation;
            layout(location = 0) out vec4 outColor;
            void main()
            {
                vec2 uv = Graphite_PanoramaUV(Graphite_RotateSkyY(Graphite_SkyDirection(), -_Rotation));

                // The seam where u wraps takes its gradient from a coordinate that does not wrap there
                float seamless = fract(uv.x + 0.5) - 0.5;
                vec2 dx = vec2(dFdx(uv.x), dFdx(uv.y)), dy = vec2(dFdy(uv.x), dFdy(uv.y));
                if (abs(dFdx(seamless)) < abs(dx.x)) dx.x = dFdx(seamless);
                if (abs(dFdy(seamless)) < abs(dy.x)) dy.x = dFdy(seamless);

                vec3 color = textureGrad(_Panorama, uv, dx, dy).rgb;
                outColor = vec4(Graphite_SaturateHalf(color * _Tint.rgb * (_Exposure * Graphite_Exposure())), 1.0);
            }
        }
        ENDGLSL
    }
}
