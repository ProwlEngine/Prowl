#version 450
layout(location=0) in vec4 v;
layout(binding=0) uniform sampler2D s;
void main(){ gl_Position = textureOffset(s, v.xy, ivec2(1)); }
