#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    float a;
    layout(align = 64) vec4 b;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(u.a) + u.b;
}
