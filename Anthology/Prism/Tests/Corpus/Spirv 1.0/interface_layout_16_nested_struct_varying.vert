#version 450
struct In { vec2 a; }; struct Out2 { In i; vec4 b; };
layout(location=0) in vec4 p;
layout(location=0) out Out2 s;
layout(location=2) out float tail;
void main(){ s.i.a=p.xy; s.b=p; tail=p.w; gl_Position=p; }
