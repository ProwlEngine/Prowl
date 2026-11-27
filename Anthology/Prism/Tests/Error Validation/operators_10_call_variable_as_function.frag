#version 450
// A plain variable is being called as if it were a function
layout(location=0) out vec4 outColor;
void main() {
    float x = 1.0;
    float y = x();
    outColor = vec4(y);
}
