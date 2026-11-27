#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    float s;
    vec2 v2;
    vec4 v4;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(u.s + u.v2.x, u.v2.y, u.v4.z, u.v4.w);
}
