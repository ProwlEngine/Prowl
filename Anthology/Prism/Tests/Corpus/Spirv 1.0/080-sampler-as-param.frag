#version 450
layout(binding=0) uniform sampler2D tex0;
layout(location=0) in vec2 uv;
layout(location=0) out vec4 o;
vec4 SampleTwice(sampler2D s, vec2 p){ return texture(s, p) + texture(s, p*2.0); }
void main(){ o = SampleTwice(tex0, uv); }