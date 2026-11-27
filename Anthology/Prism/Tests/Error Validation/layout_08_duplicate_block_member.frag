// a uniform block has two members with the same name
#version 450

layout(binding = 0) uniform Block {
    float value;
    float value;
} b;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(b.value);
}
