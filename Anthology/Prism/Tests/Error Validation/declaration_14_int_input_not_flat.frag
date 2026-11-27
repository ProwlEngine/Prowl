#version 460

layout(location = 0) in int vId;
layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = vec4(float(vId));
}
