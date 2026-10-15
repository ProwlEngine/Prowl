#version 450
layout(location=0) out vec4 c;
layout(location=1, component=3) in vec2 w;
void main(){ c = vec4(w,0,0); }
