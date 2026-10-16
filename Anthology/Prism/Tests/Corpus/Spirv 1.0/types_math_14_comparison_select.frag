#version 450
layout(location = 0) in vec4 vA;
layout(location = 1) in vec4 vB;
layout(location = 0) out vec4 oColor;
void main() {
    bvec4 lt = lessThan(vA, vB);
    vec4 picked = mix(vB, vA, vec4(lt));
    oColor = picked;
}
