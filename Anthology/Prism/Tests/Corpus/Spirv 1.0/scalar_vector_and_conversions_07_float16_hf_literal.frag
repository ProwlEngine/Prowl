#version 460
#extension GL_EXT_shader_explicit_arithmetic_types_float16 : require
layout(location = 0) out vec4 c;
void main() {
    f16vec2 v = f16vec2(1.5hf, -0.25hf);
    v = v * 2.0hf;
    c = vec4(vec2(v), 0.0, 1.0);
}
