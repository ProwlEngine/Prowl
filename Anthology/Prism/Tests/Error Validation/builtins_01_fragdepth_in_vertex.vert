// gl_FragDepth is a fragment only builtin written here in a vertex shader
#version 450

void main() {
    gl_FragDepth = 0.5;
    gl_Position = vec4(0.0);
}
