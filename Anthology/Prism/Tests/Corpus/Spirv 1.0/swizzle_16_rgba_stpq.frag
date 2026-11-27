#version 460
layout(std430, binding=0) buffer B { vec4 items[]; };
layout(location=0) out vec4 fragColor;

void main() {
    int i = int(gl_FragCoord.x);
    vec4 v = items[i];
    vec4 a = v.rgba;
    vec4 b = v.stpq;
    vec2 c = v.rg + v.st;
    fragColor = a + b + vec4(c, 0.0, 0.0);
}
