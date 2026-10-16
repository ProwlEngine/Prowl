#version 450
struct S { vec4 a; };
layout(location=0) in vec4 p;
layout(location=0) out S s[2];
layout(location=2) out float tail;
void main(){ s[0].a=p; s[1].a=p; tail=p.w; gl_Position=p; }
