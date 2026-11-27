#version 460

layout(location = 0) out mat4 m;

void main() {
    m = mat4(1.0);
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
