#version 460
#extension GL_GOOGLE_include_directive : enable
#line 1 "Graphite/Clear"

#extension GL_GOOGLE_cpp_style_line_directive : require
#line 7 "Graphite/Clear"

#line 1 "Graphite/Fullscreen.glsl"


#pragma variantGRAPHITE_LAYERED
#pragma variantGRAPHITE_NO_SOURCE

#extension GL_EXT_multiview : require

































layout(location = 0) in vec2 vUV;

layout(location = 0) out vec4 outColor;


layout(set = 0, binding = 0) uniform sampler2DArray Graphite_Source;
vec4 Graphite_SampleSource(vec2 uv) { return texture(Graphite_Source, vec3(uv, float(gl_ViewIndex))); }


#line 9 "Graphite/Clear"
 layout(set = 0, binding = 1, std140) uniform _GraphiteGlobal { vec4 Graphite_ClearColor; }; void main() { outColor = Graphite_ClearColor; }
