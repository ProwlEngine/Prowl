#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    float b;
    vec3 v;
    vec3 w;
};

void main() {
    float s = min(a, b) + max(a, b) + clamp(a, b, a + b);
    vec3 sv = min(v, w) + max(v, w) + clamp(v, w, v + w);
    vec3 mixed = min(v, b) + max(v, b) + clamp(v, a, b);
    fragColor = vec4(sv + mixed, s);
}
