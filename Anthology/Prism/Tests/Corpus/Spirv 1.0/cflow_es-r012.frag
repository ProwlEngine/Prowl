#version 310 es
precision mediump float; precision highp int;
layout(location=0) in vec4 vin; layout(location=1) flat in int ii; layout(location=2) flat in uint uu;
layout(location=0) out vec4 o;
float x = 1.0; void main(){ { float x = x + 1.0; o.x = x; } }
