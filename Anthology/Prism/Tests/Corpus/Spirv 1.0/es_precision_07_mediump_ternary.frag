#version 320 es
precision highp float;
layout(location = 0) in mediump float a;
layout(location = 1) in mediump float b;
layout(location = 2) flat in int sel;
layout(location = 0) out vec4 c;
void main() {
    mediump float r = sel > 0 ? a : b;
    c = vec4(r);
}
