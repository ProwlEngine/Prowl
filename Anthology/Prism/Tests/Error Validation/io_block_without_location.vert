#version 450
out Blk { vec3 a; float b; } blkOut;
layout(location=2) out vec2 plain;
void main() { blkOut.a = vec3(1.0); blkOut.b = 2.0; plain = vec2(3.0); gl_Position = vec4(0.0); }
