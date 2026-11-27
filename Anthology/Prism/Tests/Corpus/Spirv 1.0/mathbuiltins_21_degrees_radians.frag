#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    float s = degrees(a) + radians(a);
    vec3 sv = degrees(v) + radians(v);
    fragColor = vec4(sv, s);
}
