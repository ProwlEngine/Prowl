#version 460

layout(location = 0) out V {
    vec3 c;
    vec2 uv;
} vs;

void main() {
    vs.c = vec3(1.0, 0.5, 0.25);
    vs.uv = vec2(0.5, 0.75);
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
