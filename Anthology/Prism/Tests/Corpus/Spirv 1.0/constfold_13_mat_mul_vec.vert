#version 460

void main() {
    mat2 m = mat2(1.0, 2.0, 3.0, 4.0);   // columns (1,2) and (3,4)
    vec2 r = m * vec2(1.0, 1.0);         // (4, 6)
    gl_Position = vec4(r / 6.0, 0.0, 1.0);
}
