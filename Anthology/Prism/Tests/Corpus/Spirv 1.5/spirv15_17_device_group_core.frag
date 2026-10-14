#version 450
#extension GL_EXT_device_group : require
layout(location = 0) out vec4 o;
void main() { o = vec4(float(gl_DeviceIndex)); }
