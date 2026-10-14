#version 450
#extension GL_EXT_shader_8bit_storage : require
layout(set = 0, binding = 0) uniform U { int8_t a; uint8_t b; } u;
layout(push_constant) uniform P { int8_t c; } pc;
layout(location = 0) out vec4 o;
void main() { o = vec4(float(int(u.a)), float(uint(u.b)), float(int(pc.c)), 1.0); }
