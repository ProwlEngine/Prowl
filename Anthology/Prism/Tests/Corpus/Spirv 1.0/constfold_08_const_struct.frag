#version 460
layout(location=0) out vec4 fragColor;

struct S { float a; vec2 b; int c; };

const S s = S(0.5, vec2(1.0, 2.0), 7);

void main() {
    fragColor = vec4(s.a, s.b.x, s.b.y / 2.0, float(s.c) / 7.0);
}
