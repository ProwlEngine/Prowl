#version 450
layout(location=0) in vec4 p;
layout(location=0) out vec4 c;
void main(){ c=p; gl_Position=p; gl_PointSize=1.0; }
