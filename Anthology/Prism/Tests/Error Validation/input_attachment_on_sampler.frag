#version 450
layout(input_attachment_index=0, set=0, binding=0) uniform sampler2D s;
layout(location=0) in vec2 uv;
layout(location=0) out vec4 o;
void main() { o = texture(s, uv); }
