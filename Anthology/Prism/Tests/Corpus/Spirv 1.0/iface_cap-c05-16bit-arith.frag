#version 450
#extension GL_EXT_shader_explicit_arithmetic_types : require
layout(location=0) out vec4 o;
layout(location=0) flat in int k;
void main(){ float16_t h = float16_t(k) * 2.0hf; int16_t s = int16_t(k) + 1s; int8_t c = int8_t(s); int64_t l = int64_t(c) * 3l; o = vec4(h, s, c, l); }
