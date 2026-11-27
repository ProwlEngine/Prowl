#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    float b;
    float c;
};

void main() {
    float x, y, z;
    x = y = z = a + b + c;
    float w = (x += 1.0, y * 2.0);
    fragColor = vec4(x, y, z, w);
}
