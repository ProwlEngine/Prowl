#version 450
#extension GL_ARB_gpu_shader_int64 : enable
layout(location=0) out vec4 o;
const uint64_t a = 0x100000000UL * 3UL; void main(){ o = vec4(float(a / 3UL), float(1UL << 40), float(0xffffffffffffffffUL > 1UL), 0); }
