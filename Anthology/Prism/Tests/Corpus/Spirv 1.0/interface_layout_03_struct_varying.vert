#version 450
struct S { vec4 a; vec2 b; };
layout(location=0) in vec4 p;
layout(location=0) out S s;
layout(location=2) out float tail;
void main(){ s.a=p; s.b=p.xy; tail=p.w; gl_Position=p; }
