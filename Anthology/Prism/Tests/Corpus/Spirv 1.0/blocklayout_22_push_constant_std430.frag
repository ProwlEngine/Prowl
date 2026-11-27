#version 460

layout(push_constant, std430) uniform PC {
    vec3 pos;
    float scale;
    mat4 m;
    int mode;
} pc;

layout(location = 0) out vec4 o;

void main() {
    o = pc.m * vec4(pc.pos * pc.scale, float(pc.mode));
}
