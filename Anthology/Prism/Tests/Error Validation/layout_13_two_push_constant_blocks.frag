// only one push_constant block is allowed per stage but two are declared
#version 450

layout(push_constant) uniform PushA {
    float a;
} pushA;

layout(push_constant) uniform PushB {
    float b;
} pushB;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(pushA.a + pushB.b);
}
