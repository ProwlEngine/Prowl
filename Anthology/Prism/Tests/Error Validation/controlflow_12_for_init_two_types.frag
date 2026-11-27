#version 450
// for loop init declares two different types in a single declaration
layout(location=0) out vec4 outColor;

void main() {
    vec4 c = vec4(0.0);
    for (int i = 0, float f = 0.0; i < 4; i++) {
        c += vec4(f);
    }
    outColor = c;
}
