#version 450
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aColor;
layout(location = 0) out vec3 vColor;
void main() {
    vColor = aColor;
    gl_Position = vec4(aPosition, 1.0);
}
