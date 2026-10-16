#version 310 es
precision mediump float;
layout(location=0) out vec4 o; layout(location=0) in vec4 v;
layout(binding=0) uniform highp sampler2D s; void main(){ o = texture(s, v.xy); }
