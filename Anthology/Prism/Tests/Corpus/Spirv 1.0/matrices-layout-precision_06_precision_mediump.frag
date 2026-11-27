#version 460
precision mediump float;
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 o;
void main() { mediump float x = uv.x * uv.y; o = vec4(x); }
