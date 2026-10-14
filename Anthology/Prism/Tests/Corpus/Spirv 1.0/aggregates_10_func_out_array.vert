#version 460

layout(location = 0) in float inK;

void fill(out float a[3], float k) {
    a[0] = k;
    a[1] = k * 2.0;
    a[2] = k * 3.0;
}

void main() {
    float arr[3];
    fill(arr, inK);
    gl_Position = vec4(arr[0], arr[1], arr[2], 1.0);
}
