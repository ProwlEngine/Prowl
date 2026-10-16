#version 450
layout(location=0) in vec4 p;
layout(location=0, component=0) out float x;
layout(location=0, component=1) out float y;
layout(location=0, component=2) out vec2 zw;
void main(){ x=p.x; y=p.y; zw=p.zw; gl_Position=p; }
