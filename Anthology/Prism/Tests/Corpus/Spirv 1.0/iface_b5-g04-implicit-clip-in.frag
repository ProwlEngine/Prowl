#version 450
layout(location=0) out vec4 o;
in float gl_ClipDistance[4];
void main(){ o = vec4(gl_ClipDistance[3]); }
