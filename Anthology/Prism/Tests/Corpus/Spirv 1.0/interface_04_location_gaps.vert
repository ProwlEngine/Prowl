#version 460

layout(location = 0) out vec4 a;
layout(location = 2) out vec4 b;
layout(location = 5) out vec4 c;

void main() {
    a = vec4(0.0);
    b = vec4(1.0);
    c = vec4(2.0);
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
