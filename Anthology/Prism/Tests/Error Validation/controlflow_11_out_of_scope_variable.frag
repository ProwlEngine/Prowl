#version 450
// variable referenced after the block it was declared in has already closed
layout(location=0) out vec4 outColor;

void main() {
    {
        float inner = 2.0;
    }
    outColor = vec4(inner);
}
