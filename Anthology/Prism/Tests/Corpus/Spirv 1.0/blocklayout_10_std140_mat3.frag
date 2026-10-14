#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    mat3 m;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(u.m * vec3(1.0, 2.0, 3.0), 1.0);
}
