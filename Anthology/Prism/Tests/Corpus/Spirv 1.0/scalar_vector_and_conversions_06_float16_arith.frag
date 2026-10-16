#version 460
#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require
layout(location = 0) out vec4 c;
void main() {
    float16_t a = float16_t(1.5);
    f16vec3 v = f16vec3(a, a, a);
    v = v * a + f16vec3(float16_t(0.5));
    c = vec4(vec3(v), 1.0);
}
