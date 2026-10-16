#version 310 es
precision mediump float;
layout(location=0) in vec2 uv;
layout(location=0) out vec4 o;
layout(binding=0) uniform sampler2D tex;
float lum(const in vec3 c, highp float k, lowp int n){ return dot(c, vec3(0.3, 0.6, 0.1)) * k + float(n); }
vec4 fetch(sampler2D s, vec2 p){ return texture(s, p); }
void main(){ vec4 c = fetch(tex, uv); o = vec4(lum(c.rgb, 2.0, 1)); }
