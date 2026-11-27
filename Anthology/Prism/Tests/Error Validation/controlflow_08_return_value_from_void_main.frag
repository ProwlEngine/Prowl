#version 450
// return with a value inside main but main has a void return type
layout(location=0) out vec4 outColor;

void main() {
    outColor = vec4(1.0);
    return 5;
}
