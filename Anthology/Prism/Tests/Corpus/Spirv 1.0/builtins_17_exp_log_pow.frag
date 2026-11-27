#version 450
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    float p = pow(vX, 2.2);
    float e = exp(vX);
    float l = log(vX + 1.0);
    float e2 = exp2(vX);
    oColor = vec4(p, e, l, e2);
}
