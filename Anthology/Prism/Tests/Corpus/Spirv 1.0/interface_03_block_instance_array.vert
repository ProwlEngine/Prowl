#version 460

layout(location = 0) out V {
    vec3 c;
} vs[2];

void main() {
    vs[0].c = vec3(1.0);
    vs[1].c = vec3(2.0);
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
