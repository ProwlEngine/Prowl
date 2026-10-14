#version 460

layout(location = 0) in float inScale;

void main() {
    float a[3] = float[3](1.0, 2.0, 3.0);
    float s = a[0] + a[1] + a[2];
    gl_Position = vec4(s * inScale, 0.0, 0.0, 1.0);
}
