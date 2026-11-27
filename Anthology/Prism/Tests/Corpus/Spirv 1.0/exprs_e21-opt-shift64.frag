#version 450
#extension GL_ARB_gpu_shader_int64 : enable
layout(location=0) out vec4 o; layout(location=2) flat in uint u;
void main(){ o = vec4(float(uint64_t(u) >> 33)); }
