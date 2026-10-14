#version 460

struct S {
    vec3 a;
    float b;
};

layout(set = 0, binding = 0, std140) uniform Block {
    S arr[3];
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(u.arr[0].a, u.arr[0].b) + vec4(u.arr[2].a, u.arr[2].b);
}
