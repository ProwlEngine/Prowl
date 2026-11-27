#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    float b;
    vec3 v;
};

void main() {
    float s = pow(a, b) + exp(a) + log(a) + exp2(a) + log2(a);
    vec3 sv = pow(v, v.zyx) + exp(v) + log(v) + exp2(v) + log2(v);
    fragColor = vec4(sv, s);
}
