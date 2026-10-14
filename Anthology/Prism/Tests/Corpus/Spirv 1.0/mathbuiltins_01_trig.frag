#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    float s = sin(a) + cos(a) + tan(a);
    vec3 sv = sin(v) + cos(v) + tan(v);
    fragColor = vec4(sv, s);
}
