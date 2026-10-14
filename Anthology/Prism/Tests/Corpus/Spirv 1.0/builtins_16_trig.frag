#version 450
layout(location = 0) in float vT;
layout(location = 0) out vec4 oColor;
void main() {
    float s = sin(vT);
    float c = cos(vT);
    float t = tan(vT);
    float a = atan(s, c);
    oColor = vec4(s, c, t, a);
}
