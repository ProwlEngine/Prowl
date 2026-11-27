#version 450
layout(location = 0) in float vX;
layout(location = 0) out vec4 oColor;
void main() {
    float a = abs(vX);
    float sg = sign(vX);
    float fl = floor(vX);
    float ce = ceil(vX);
    float fr = fract(vX);
    float md = mod(vX, 3.0);
    float rd = round(vX);
    float tr = trunc(vX);
    oColor = vec4(a + sg, fl + ce, fr + md, rd + tr);
}
