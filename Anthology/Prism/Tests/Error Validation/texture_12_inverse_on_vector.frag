// inverse is given a vec2 but it requires a matrix argument
#version 450
layout(location=0) out vec4 outColor;
layout(location=0) in vec2 v;
void main() {
    vec2 r = inverse(v);
    outColor = vec4(r, 0.0, 1.0);
}
