#version 460

layout(location = 0) in float inK;

float sum3(float a[3]) {
    return a[0] + a[1] + a[2];
}

void main() {
    float arr[3] = float[3](inK, inK + 1.0, inK + 2.0);
    gl_Position = vec4(sum3(arr), 0.0, 0.0, 1.0);
}
