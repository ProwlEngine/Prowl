#version 460

struct S {
    vec4 val;
};

layout(location = 0) in vec4 inV;

void main() {
    S a;
    a.val = inV;
    S b;
    b = a;
    gl_Position = b.val;
}
