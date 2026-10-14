#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    ivec3 i;
    uvec4 u4;
    dvec2 d;
} u;

layout(location = 0) out vec4 o;

void main() {
    o = vec4(float(u.i.x), float(u.u4.y), float(u.d.x), float(u.d.y));
}
