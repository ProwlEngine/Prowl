#version 460
layout(location=0) out float o;
void main() { o = float(gl_BaseVertex + gl_BaseInstance + gl_DrawID); gl_Position = vec4(0.0); }
