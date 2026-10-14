#version 460
#extension GL_ARB_enhanced_layouts : enable

layout(location = 0) out vec3 v;
layout(location = 0, component = 3) out float f;

void main() {
    v = vec3(1.0);
    f = 2.0;
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
