#version 460

layout(location = 0) flat out int id;

void main() {
    id = 7;
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
