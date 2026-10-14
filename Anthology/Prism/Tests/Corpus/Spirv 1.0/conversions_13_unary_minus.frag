#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    int i;
    uint u;
    float f;
    vec3 v;
};

void main() {
    int a = -i;
    uint b = -u;
    float c = -f;
    vec3 d = -v;
    fragColor = vec4(float(a) + float(b) + c, d);
}
