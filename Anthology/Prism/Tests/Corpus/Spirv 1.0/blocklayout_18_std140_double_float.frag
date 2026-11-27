#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    double d;
    float f;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(float(u.d), u.f, 0.0, 1.0);
}
