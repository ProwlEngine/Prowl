#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    mat4 m;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = u.m * vec4(1.0, 2.0, 3.0, 4.0);
}
