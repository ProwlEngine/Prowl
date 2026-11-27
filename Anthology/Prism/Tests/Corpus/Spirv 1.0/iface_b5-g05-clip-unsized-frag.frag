#version 450
layout(location=0) out vec4 o;
void main(){ o = vec4(gl_ClipDistance[2], gl_CullDistance[1], 0, 0); }
