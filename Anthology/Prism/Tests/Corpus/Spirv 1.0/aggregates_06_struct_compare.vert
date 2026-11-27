#version 460

struct S {
    vec2 a;
    float b;
};

layout(location = 0) in vec2 inA;
layout(location = 1) in float inB;

void main() {
    S x = S(inA, inB);
    S y = S(vec2(0.0), 1.0);
    float r = (x == y) ? 1.0 : ((x != y) ? 2.0 : 0.0);
    gl_Position = vec4(r);
}
