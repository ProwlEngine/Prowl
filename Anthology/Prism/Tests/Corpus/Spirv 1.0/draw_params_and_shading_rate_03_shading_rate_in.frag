#version 450
#extension GL_EXT_fragment_shading_rate : require
layout(location=0) out vec4 o;
void main() { o = vec4(float(gl_ShadingRateEXT)); }
