// gl_TessCoord is a tessellation evaluation builtin read here in a vertex shader
#version 450

void main() {
    gl_Position = vec4(gl_TessCoord, 1.0);
}
