#version 460
#extension GL_ARB_enhanced_layouts : enable

layout(location = 0, component = 0) out float x;
layout(location = 0, component = 1) out float y;
layout(location = 0, component = 2) out float z;

void main() {
    x = 1.0;
    y = 2.0;
    z = 3.0;
    gl_Position = vec4(0.0, 0.0, 0.0, 1.0);
}
