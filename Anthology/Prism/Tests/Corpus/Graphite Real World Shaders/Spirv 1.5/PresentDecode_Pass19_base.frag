#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/PresentDecode"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/PresentDecode"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE



































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2D Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, uv); }


#line 9 "Graphite/PresentDecode"
 void main()
{
    vec4 c = Graphite_SampleSource(vec2(vUV.x, 1.0 - vUV.y));
    outColor = vec4(pow( (max(c.rgb, vec3(0.0)) + 0.055) / 1.055, vec3(2.4)), c.a);
}
