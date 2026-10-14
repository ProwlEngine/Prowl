#version 460
layout(std430, binding=0) buffer B { vec4 items[]; };
layout(location=0) out vec4 fragColor;

void main() {
    int i = int(gl_FragCoord.x);
    vec4 v = items[i];
    vec2 a = v.xyz.yz;
    fragColor = vec4(a, 0.0, 0.0);
}
