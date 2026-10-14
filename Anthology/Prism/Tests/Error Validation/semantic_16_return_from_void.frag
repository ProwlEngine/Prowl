#version 460
layout(location=0) out vec4 o;
void foo() {
    return 1.0;
}
void main() {
    foo();
    o = vec4(1.0);
}
