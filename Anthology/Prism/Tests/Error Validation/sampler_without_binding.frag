#version 450
uniform sampler2D s;
layout(location=0) in vec2 uv;
layout(location=0) out vec4 o;
void main() { o = texture(s, uv); }
