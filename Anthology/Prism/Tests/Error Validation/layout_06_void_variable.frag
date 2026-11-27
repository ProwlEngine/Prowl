// a variable cannot be declared with type void
#version 450

void v;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(1.0);
}
