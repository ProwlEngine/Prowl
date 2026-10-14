#version 450
// switch has two identical case constant labels which is a duplicate case
layout(location=0) out vec4 outColor;

void main() {
    int x = 2;
    switch (x) {
        case 1:
            outColor = vec4(1.0);
            break;
        case 1:
            outColor = vec4(0.5);
            break;
        default:
            outColor = vec4(0.0);
            break;
    }
}
