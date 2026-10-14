#version 460

layout(location = 0) in V {
    vec3 c;
    vec2 uv;
} fs;

layout(location = 0) out vec4 color;

void main() {
    color = vec4(fs.c, fs.uv.x);
}
