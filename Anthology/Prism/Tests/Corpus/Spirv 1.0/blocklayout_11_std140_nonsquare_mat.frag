#version 460

layout(set = 0, binding = 0, std140) uniform Block {
    mat2x3 a;
    mat3x2 b;
} u;

layout(location = 0) out vec4 o;

void main() {
    vec3 r = u.a * vec2(1.0, 2.0);
    vec2 s = u.b * vec3(1.0, 2.0, 3.0);
    o = vec4(r.xy + s, r.z, 1.0);
}
