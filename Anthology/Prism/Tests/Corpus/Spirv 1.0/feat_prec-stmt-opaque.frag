#version 450
layout(binding=0) uniform sampler2D s;
layout(location=0) out vec4 o;
void main(){ precision mediump sampler2D; precision lowp int; precision highp image2D; o = texture(s, vec2(0.5)); }
