#version 460

layout(location = 0) out vec4 fragColor;

helper() {
    return 1.0;
}

void main() {
    fragColor = vec4(helper());
}
