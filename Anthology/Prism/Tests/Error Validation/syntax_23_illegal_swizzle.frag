#version 460

layout(location = 0) out vec4 fragColor;

void main() {
    vec4 v = vec4(1.0, 2.0, 3.0, 4.0);
    float f = v.xq9;
    fragColor = vec4(f);
}
