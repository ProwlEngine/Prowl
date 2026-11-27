#version 450
layout(set=0,binding=0) uniform sampler2D s;
layout(location=0) out vec4 o;
void main() { o = vec4(textureQueryLod(s, 0.5), 0.0, 0.0); }
