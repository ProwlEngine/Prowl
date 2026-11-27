#version 450
layout(location=0) in vec4 p;
layout(location=0) out mat2 m[2];
layout(location=4) out float tail;
void main(){ m[0]=mat2(p.x); m[1]=mat2(p.y); tail=p.w; gl_Position=p; }
