#version 450
layout(location=0) out vec4 a;
layout(location=1) invariant out vec4 b;
invariant gl_Position;
void main() { a = vec4(1.0); b = vec4(2.0); gl_Position = vec4(3.0); }
