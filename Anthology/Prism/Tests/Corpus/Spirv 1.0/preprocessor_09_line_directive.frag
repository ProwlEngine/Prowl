#version 460

layout(location = 0) out vec4 fragColor;

void main() {
#line 100
    float l = float(__LINE__);
    fragColor = vec4(l / 1000.0, 0.0, 0.0, 1.0);
}
