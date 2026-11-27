#version 450
// switch has two default labels but only one is allowed
layout(location=0) out vec4 outColor;

void main() {
    int x = 2;
    switch (x) {
        case 1:
            outColor = vec4(1.0);
            break;
        default:
            outColor = vec4(0.5);
            break;
        default:
            outColor = vec4(0.0);
            break;
    }
}
