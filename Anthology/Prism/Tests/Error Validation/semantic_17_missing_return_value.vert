#version 460
float foo() {
    return;
}
void main() {
    gl_Position = vec4(foo());
}
