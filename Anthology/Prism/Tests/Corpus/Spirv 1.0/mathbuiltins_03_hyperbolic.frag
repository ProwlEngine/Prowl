#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    float s = sinh(a) + cosh(a) + tanh(a);
    vec3 sv = sinh(v) + cosh(v) + tanh(v);
    fragColor = vec4(sv, s);
}
