#version 450
layout(location = 0) in vec2 vUV;
layout(location = 0) out vec4 oColor;

void rotate(inout vec2 p, float angle) {
    float s = sin(angle);
    float c = cos(angle);
    p = mat2(c, -s, s, c) * p;
}

float computeAndReport(vec2 p, out float lengthOut) {
    lengthOut = length(p);
    return atan(p.y, p.x);
}

void main() {
    vec2 p = vUV * 2.0 - 1.0;
    rotate(p, 0.785398);
    float len;
    float ang = computeAndReport(p, len);
    oColor = vec4(p, ang, len);
}
