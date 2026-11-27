#version 460
layout(location=0) out vec4 o;
struct A { float x; };
struct B { float x; };
void main() {
    A a;
    B b;
    a.x = 1.0;
    b.x = 1.0;
    bool eq = (a == b);
    o = vec4(eq ? 1.0 : 0.0);
}
