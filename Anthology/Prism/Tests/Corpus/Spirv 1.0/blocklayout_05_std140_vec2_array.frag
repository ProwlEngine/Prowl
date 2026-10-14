#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    vec2 arr[4];
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(u.arr[0], u.arr[3]);
}
