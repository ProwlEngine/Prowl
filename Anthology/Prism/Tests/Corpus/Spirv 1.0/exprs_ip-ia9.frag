#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i;
#extension GL_AMD_gpu_shader_half_float : enable
layout(location=2) in f16vec2 h; void main(){ o = vec4(vec2(interpolateAtCentroid(h)), 0, 0); }
