#version 450
layout(push_constant) uniform Push {
    mat4 model;
    vec4 tint;
} pc;
layout(location = 0) in vec3 aPosition;
layout(location = 0) out vec4 vTint;
void main() {
    vTint = pc.tint;
    gl_Position = pc.model * vec4(aPosition, 1.0);
}
