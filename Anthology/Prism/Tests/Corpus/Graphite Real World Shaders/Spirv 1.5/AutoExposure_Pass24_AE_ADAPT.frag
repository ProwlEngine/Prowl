#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/AutoExposure"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/AutoExposure"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/AutoExposure"
#line 1 "Graphite/Internal/AutoExposure.glsl"
#pragma variantAEAPPLYEXTRACTDOWNSAMPLEADAPT
#pragma variantAE_LAYERED_SOURCE




float Luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }


































layout(set = 0, binding = 1) uniform sampler2D _MainTex;
layout(set = 0, binding = 2) uniform sampler2D _AdaptedTex;
layout(set = 0, binding = 3, std140) uniform _GraphiteGlobal { float _AdaptSpeedUp; float _AdaptSpeedDown; float _HistoryValid; float _DeltaTime; };




void main()
{
    float currentLum = exp(texture(_MainTex, vec2(0.5)).r);
    float previousLum = texture(_AdaptedTex, vec2(0.5)).r;
    if (_HistoryValid < 0.5)
    {
        outColor = vec4(currentLum, 0.0, 0.0, 1.0);
        return;
    }
    float speed = currentLum > previousLum ? _AdaptSpeedUp : _AdaptSpeedDown;
    float adapted = previousLum + (currentLum - previousLum) * (1.0 - exp(- _DeltaTime * speed));
    outColor = vec4(clamp(adapted, 0.0001, 100.0), 0.0, 0.0, 1.0);
}














#line 10 "Graphite/AutoExposure"

