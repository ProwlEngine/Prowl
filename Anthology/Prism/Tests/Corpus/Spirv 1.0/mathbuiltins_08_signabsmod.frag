#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    float b;
    vec3 v;
    vec3 w;
};

void main() {
    float s = abs(a) + sign(a) + mod(a, b);
    vec3 sv = abs(v) + sign(v) + mod(v, w) + mod(v, b);
    fragColor = vec4(sv, s);
}
