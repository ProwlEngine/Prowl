#version 450
// continue statement used outside of any loop
layout(location=0) out vec4 outColor;

void main() {
    continue;
    outColor = vec4(1.0);
}
