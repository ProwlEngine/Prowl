#version 450
layout(location=0, component=0) out float a;
layout(location=0, component=1) out int b;
void main(){ a = 0.0; b = 1; gl_Position = vec4(0); }
