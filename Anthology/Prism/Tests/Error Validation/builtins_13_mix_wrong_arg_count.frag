// mix called with only two arguments instead of the required three
#version 450

layout(location=0) out vec4 outColor;

void main() {
    outColor = mix(vec4(0.0), vec4(1.0));
}
