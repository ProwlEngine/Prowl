// mix takes 3 arguments but is called with 4
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in float a;
layout(location=1) in float b;
layout(location=2) in float c;
layout(location=3) in float d;
void main() {
    float r = mix(a, b, c, d);
    outColor = vec4(r, r, r, 1.0);
}
