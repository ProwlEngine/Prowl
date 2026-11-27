#version 460

struct S {
    vec3 pos;
    float w;
};

layout(location = 0) in vec3 inPos;

S make(vec3 p) {
    S s;
    s.pos = p * 2.0;
    s.w = 1.0;
    return s;
}

void main() {
    S s = make(inPos);
    gl_Position = vec4(s.pos, s.w);
}
