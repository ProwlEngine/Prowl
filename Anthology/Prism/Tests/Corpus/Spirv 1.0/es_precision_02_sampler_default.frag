#version 320 es
precision highp float;
precision mediump int;
layout(location = 0) in highp vec2 uv;
layout(location = 0) out highp vec4 c;
layout(binding = 0) uniform sampler2D tex;
void main() {
    c = texture(tex, uv);
}
