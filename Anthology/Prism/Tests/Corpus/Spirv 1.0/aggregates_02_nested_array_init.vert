#version 460

layout(location = 0) in float inK;

void main() {
    vec2 v[2] = { vec2(1.0), vec2(2.0) };
    vec2 r = v[0] + v[1] * inK;
    gl_Position = vec4(r, 0.0, 1.0);
}
