Shader "Graphite/Sky/Panorama"
{
    Properties
    {
        _Panorama("Panorama", Texture2D) = "gray" {} [NoScaleOffset]
        _Tint("Tint", Color) = (1.0, 1.0, 1.0, 1.0)
        intensity("Intensity", Float) = 1.0 [Range(0, 8)]
        _Rotation("Rotation", Float) = 0.0 [Range(0, 360)]
    }

    Surface "Sky"
    {
        GLSLPROGRAM
        #include "Graphite/Skies.glsl"
        uniform sampler2D _Panorama;
        uniform vec4 _Tint;
        uniform float intensity;
        uniform float _Rotation;

        vec3 Sky(vec3 direction)
        {
            vec2 uv = Graphite_PanoramaUV(Graphite_RotateSkyY(direction, -_Rotation));

            // The seam where u wraps takes its gradient from a coordinate that does not wrap there
            float seamless = fract(uv.x + 0.5) - 0.5;
            vec2 dx = vec2(dFdx(uv.x), dFdx(uv.y)), dy = vec2(dFdy(uv.x), dFdy(uv.y));
            if (abs(dFdx(seamless)) < abs(dx.x)) dx.x = dFdx(seamless);
            if (abs(dFdy(seamless)) < abs(dy.x)) dy.x = dFdy(seamless);

            return textureGrad(_Panorama, uv, dx, dy).rgb * _Tint.rgb * intensity;
        }
        ENDGLSL
    }
}
