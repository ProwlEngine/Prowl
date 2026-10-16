#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    bool flag;
    vec4 a;
    vec4 b;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = u.flag ? u.a : u.b;
}
