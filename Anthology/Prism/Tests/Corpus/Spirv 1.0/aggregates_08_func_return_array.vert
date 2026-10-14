#version 460

layout(location = 0) in float inK;

float[3] build(float k) {
    return float[3](k, k * 2.0, k * 3.0);
}

void main() {
    float a[3] = build(inK);
    gl_Position = vec4(a[0], a[1], a[2], 1.0);
}
