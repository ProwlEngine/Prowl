#version 460

layout(location = 0) out vec4 fragColor;

layout(binding = 0) uniform U {
    float a;
    vec3 v;
};

void main() {
    float s = floor(a) + ceil(a) + trunc(a) + round(a) + roundEven(a) + fract(a);
    vec3 sv = floor(v) + ceil(v) + trunc(v) + round(v) + roundEven(v) + fract(v);
    fragColor = vec4(sv, s);
}
