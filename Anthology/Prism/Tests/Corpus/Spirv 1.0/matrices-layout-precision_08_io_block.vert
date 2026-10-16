#version 460
layout(location = 0) in vec3 pos;
layout(location = 0) out VS { vec3 color; vec2 uv; } vs;
void main() { vs.color = pos; vs.uv = pos.xy; gl_Position = vec4(pos, 1.0); }
