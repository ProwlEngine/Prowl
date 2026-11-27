#version 460

struct P {
    vec3 a;
    float b;
};

layout(location = 0) in vec3 inPos;

void main() {
    float v = P(inPos, 2.0).b + P(inPos, 3.0).a.x;
    gl_Position = vec4(inPos, v);
}
