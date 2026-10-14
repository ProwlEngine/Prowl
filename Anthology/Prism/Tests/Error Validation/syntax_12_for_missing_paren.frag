#version 460

layout(location = 0) out vec4 fragColor;

void main() {
    float a = 0.0;
    for (int i = 0; i < 4; i++ {
        a += 1.0;
    }
    fragColor = vec4(a);
}
