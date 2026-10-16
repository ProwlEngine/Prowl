#version 450
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
layout(location=1) flat in int i; layout(location=2) flat in uint u;
precision mediump float; void main(){ float a = v.x * 2.0; mediump vec2 b = v.xy; lowp int c = i; highp float d = a + b.x; o = vec4(a, b, d + float(c)); }
