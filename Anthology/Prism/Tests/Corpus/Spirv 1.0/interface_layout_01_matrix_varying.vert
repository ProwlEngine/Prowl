#version 450
layout(location=0) in vec4 a;
layout(location=0) out mat4 m;
layout(location=4) out vec4 tail;
void main(){ m = mat4(a.x); tail = a; gl_Position = a; }
