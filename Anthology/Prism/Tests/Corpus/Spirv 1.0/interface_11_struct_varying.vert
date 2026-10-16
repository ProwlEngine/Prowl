#version 460

struct S {
    vec3 a;
    vec2 b;
};

layout(location = 0) out S s;

void main() {
    s.a = vec3(1.0);
    s.b = vec2(2.0);
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
