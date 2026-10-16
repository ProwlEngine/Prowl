#version 450
layout(location=0) out vec4 c;
layout(binding=0) uniform sampler2D s[];
void main(){ c = texture(s[2], vec2(0)); }
