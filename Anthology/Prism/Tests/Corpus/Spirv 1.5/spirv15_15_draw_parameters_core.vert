#version 460
layout(location = 0) out flat int o;
void main() { gl_Position = vec4(0.0); o = gl_DrawID + gl_BaseVertex + gl_BaseInstance; }
