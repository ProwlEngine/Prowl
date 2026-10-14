#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    uint u;
    int s;
    float f;
};

void main() {
    float a = u + f;
    uint c = s + u;
    fragColor = vec4(a, float(c), 0.0, 1.0);
}
