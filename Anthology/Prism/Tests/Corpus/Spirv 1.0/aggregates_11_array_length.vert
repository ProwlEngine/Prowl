#version 460

layout(location = 0) in float inK;

void main() {
    float a[4] = float[4](1.0, 2.0, 3.0, 4.0);
    float r = inK * float(a.length()) + float(a.length() - 1);
    gl_Position = vec4(r, 0.0, 0.0, 1.0);
}
