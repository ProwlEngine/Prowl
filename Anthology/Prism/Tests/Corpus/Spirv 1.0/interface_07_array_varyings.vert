#version 460

layout(location = 0) out vec2 data[4];

void main() {
    data[0] = vec2(0.0);
    data[1] = vec2(1.0);
    data[2] = vec2(2.0);
    data[3] = vec2(3.0);
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
