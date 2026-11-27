#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    float s = asinh(a) + acosh(a) + atanh(a);
    vec3 sv = asinh(v) + acosh(v) + atanh(v);
    fragColor = vec4(sv, s);
}
