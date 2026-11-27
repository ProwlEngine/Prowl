#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    float b;
    float c;
    vec3 va;
    vec3 vb;
    vec3 vc;
};

void main() {
    float s = fma(a, b, c);
    vec3 sv = fma(va, vb, vc);
    fragColor = vec4(sv, s);
}
