#version 450
layout(location=0x1) out vec4 c;
layout(binding=010) uniform sampler2D s;
void main(){ c = texture(s, vec2(0)); }
