#version 460

out float gl_CullDistance[1];

void main() {
    gl_CullDistance[0] = 0.5;
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
