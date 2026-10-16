#version 450
#extension GL_EXT_shader_explicit_arithmetic_types : require
layout(location=0) out vec4 o;
void main(){ i64vec2 a = i64vec2(-7l, 9l); int16_t s = int16_t(-3); ivec3 v = ivec3(1, -2, 3); i64vec2 na = -a; int16_t ns = -s; ivec3 nv = -v; o = vec4(float(na.x), float(na.y), float(ns), float(nv.y + nv.z)); }
