#version 450
layout(location=0) in float f;
layout(location=1) in vec2 uv;
layout(binding=0) uniform sampler2D s;
layout(location=0) out vec4 o;
const float A = 3;
void main(){ o = vec4(pow(f, A)); }
