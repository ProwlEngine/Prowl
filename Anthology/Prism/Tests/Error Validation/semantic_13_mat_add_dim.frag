#version 460
layout(location=0) out vec4 o;
void main() {
    mat3 a = mat3(1.0);
    mat4 b = mat4(1.0);
    mat4 c = a + b;
    o = vec4(c[0]);
}
