#version 450
// switch selector is a float but it must be an integer type
layout(location=0) out vec4 outColor;

void main() {
    float f = 1.0;
    switch (f) {
        case 1:
            outColor = vec4(1.0);
            break;
        default:
            outColor = vec4(0.0);
            break;
    }
}
