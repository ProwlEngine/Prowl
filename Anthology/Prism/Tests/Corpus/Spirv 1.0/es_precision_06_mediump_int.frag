#version 320 es
precision highp float;
precision mediump int;
layout(location = 0) flat in mediump int a;
layout(location = 1) flat in mediump int b;
layout(location = 0) out vec4 c;
void main() {
    mediump int r = (a << 2) + (b & 7);
    highp int h = a * b;
    c = vec4(float(r), float(h), 0.0, 1.0);
}
