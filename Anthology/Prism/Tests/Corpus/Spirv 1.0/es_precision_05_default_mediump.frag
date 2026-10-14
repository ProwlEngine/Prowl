#version 320 es
precision mediump float;
layout(location = 0) in vec2 uv;
layout(location = 0) out vec4 c;
void main() {
    float x = uv.x * 2.0 + uv.y;
    vec2 g = uv * x;
    c = vec4(g, x, 1.0);
}
