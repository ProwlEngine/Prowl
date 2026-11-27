#version 450
layout(location=0) out vec4 c;
layout(binding=0, location=3) uniform sampler2D s;
void main(){ c = texture(s, vec2(0)); }
