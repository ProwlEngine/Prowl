#version 320 es
precision highp float;
layout(location = 0) in mediump vec3 v;
layout(location = 0) out highp vec4 c;
void main() {
    mediump float r = length(v) + dot(v, v);
    mediump vec3 n = normalize(v);
    c = vec4(n, r);
}
