#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    vec3 a;
    float b;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(u.a, u.b);
}
