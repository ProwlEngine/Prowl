#version 460

layout(location = 0) out vec3 smoothC;
layout(location = 1) flat out vec3 flatC;

void main() {
    vec3 c = vec3(1.0, 2.0, 3.0);
    smoothC = c;
    flatC = c;
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
