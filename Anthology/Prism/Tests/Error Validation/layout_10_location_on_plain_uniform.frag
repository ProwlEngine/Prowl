// location is not allowed on a plain non opaque uniform in Vulkan GLSL
#version 450

layout(location = 0) uniform vec4 c;

layout(location = 0) out vec4 fragColor;

void main() {
    fragColor = c;
}
