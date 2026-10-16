#version 320 es
precision mediump float;
layout(location=0) out vec4 o;
layout(location=0) in vec4 v;
struct Light { vec3 dir; highp float range; };
layout(binding=0, std140) uniform U { vec4 tint; highp mat2 rot; Light lights[2]; int count; } u;
layout(location=1) in Varyings { vec2 uv; highp vec3 wpos; } vin;
void main(){ vec2 r = u.rot * vin.uv; o = u.tint * v + vec4(u.lights[1].dir * u.lights[0].range, float(u.count)) + vec4(r, vin.wpos.x, 1.0); }
