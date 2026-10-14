#version 320 es
precision highp float;
layout(location = 0) in mediump float a;
layout(location = 1) in mediump float b;
layout(location = 0) out highp vec4 c;
void main() {
    mediump float r = a + b;
    c = vec4(r);
}
